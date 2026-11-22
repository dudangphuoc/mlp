using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using MLPlatform.Contracts.V1;

namespace MLPlatform.Agent.Ops;

/// <summary>File browser operations inside the allowed roots (docs/agent-upgrade.md §1.2).</summary>
public sealed class HostFs(IReadOnlyList<string> roots)
{
    public const int MaxRead = 4 * 1024 * 1024, MaxWrite = 1024 * 1024, MaxEntries = 5000;

    public CommandResult List(FsList l)
    {
        if (string.IsNullOrEmpty(l.Path)) return RootListing();
        if (PathGuard.Resolve(roots, l.Path) is not { } p) return OpsResult.Outside();
        if (!Directory.Exists(p)) return OpsResult.NotFound();
        var listing = new FsListing { Path = p };
        var all = new DirectoryInfo(p).EnumerateFileSystemInfos().OrderBy(i => i is not DirectoryInfo).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var i in all.Take(MaxEntries + 1))
        {
            if (listing.Entries.Count == MaxEntries) { listing.Truncated = true; break; }
            listing.Entries.Add(Entry(i));
        }
        return new CommandResult { Ok = true, FsListing = listing };
    }

    private CommandResult RootListing()
    {
        var listing = new FsListing();
        listing.Entries.AddRange(roots.Where(Directory.Exists).Select(r => Entry(new DirectoryInfo(r)) is var e ? Named(e, r) : e));
        return new CommandResult { Ok = true, FsListing = listing };
    }

    private static FsEntry Named(FsEntry e, string fullPath) { e.Name = fullPath; return e; }

    private static FsEntry Entry(FileSystemInfo i)
    {
        var e = new FsEntry { Name = i.Name, IsDir = i is DirectoryInfo, IsLink = i.LinkTarget is not null };
        try
        {
            e.ModifiedAt = Timestamp.FromDateTime(i.LastWriteTimeUtc);
            if (i is FileInfo f) e.Size = (ulong)f.Length;
        }
        catch (Exception x) when (x is IOException or UnauthorizedAccessException) { }
        return e;
    }

    public async Task<CommandResult> ReadAsync(FsRead r, CancellationToken ct)
    {
        if (PathGuard.Resolve(roots, r.Path) is not { } p) return OpsResult.Outside();
        if (!File.Exists(p)) return OpsResult.NotFound();
        await using var fs = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (PathGuard.Resolve(roots, fs.Name) is null) return OpsResult.Outside();
        var len = (int)Math.Min(r.Length is > 0 and <= MaxRead ? r.Length : MaxRead, Math.Max(0, fs.Length - (long)r.Offset));
        var buf = new byte[len];
        fs.Seek((long)r.Offset, SeekOrigin.Begin);
        await fs.ReadExactlyAsync(buf, ct);
        var stat = Stat(p, fs.Length);
        stat.IsText = TextSniff.IsText(buf);
        return new CommandResult { Ok = true, Data = ByteString.CopyFrom(buf), FsStat = stat };
    }

    public async Task<CommandResult> WriteAsync(FsWrite w, CancellationToken ct)
    {
        if (w.Data.Length > MaxWrite) return OpsResult.Fail("FLEET_CHUNK_TOO_LARGE", "Chunk is larger than 1 MiB");
        if (PathGuard.Resolve(roots, w.Path) is not { } p || PathGuard.IsRoot(roots, p)) return OpsResult.Outside();
        if (Directory.Exists(p)) return OpsResult.Fail("FLEET_PATH_IS_DIRECTORY", "Path is a folder");
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        await using (var fs = new FileStream(p, w.Truncate ? FileMode.Create : FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read))
        {
            if (PathGuard.Resolve(roots, fs.Name) is null) return OpsResult.Outside();
            fs.Seek((long)w.Offset, SeekOrigin.Begin);
            await fs.WriteAsync(w.Data.Memory, ct);
        }
        return new CommandResult { Ok = true, FsStat = await HashedStatAsync(p, ct) };
    }

    public CommandResult Mkdir(FsMkdir m)
    {
        if (PathGuard.Resolve(roots, m.Path) is not { } p) return OpsResult.Outside();
        Directory.CreateDirectory(p);
        return new CommandResult { Ok = true };
    }

    public CommandResult Move(FsMove m)
    {
        if (PathGuard.Resolve(roots, m.From) is not { } from || PathGuard.Resolve(roots, m.To) is not { } to) return OpsResult.Outside();
        if (PathGuard.IsRoot(roots, from)) return OpsResult.Outside();
        if (File.Exists(to) || Directory.Exists(to)) return OpsResult.Fail("FLEET_PATH_EXISTS", "Target already exists");
        if (Directory.Exists(from)) Directory.Move(from, to);
        else if (File.Exists(from)) File.Move(from, to);
        else return OpsResult.NotFound();
        return new CommandResult { Ok = true };
    }

    public CommandResult Delete(FsDelete d)
    {
        if (PathGuard.Resolve(roots, d.Path) is not { } p || PathGuard.IsRoot(roots, p)) return OpsResult.Outside();
        if (Directory.Exists(p)) Directory.Delete(p, d.Recursive);
        else if (File.Exists(p)) File.Delete(p);
        else return OpsResult.NotFound();
        return new CommandResult { Ok = true };
    }

    private static FsStat Stat(string p, long size) =>
        new() { Path = p, Size = (ulong)size, ModifiedAt = Timestamp.FromDateTime(File.GetLastWriteTimeUtc(p)) };

    public static async Task<FsStat> HashedStatAsync(string p, CancellationToken ct)
    {
        await using var fs = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var stat = Stat(p, fs.Length);
        stat.Sha256 = Convert.ToHexStringLower(await System.Security.Cryptography.SHA256.HashDataAsync(fs, ct));
        return stat;
    }
}
