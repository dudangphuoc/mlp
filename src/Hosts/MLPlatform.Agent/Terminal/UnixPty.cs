using System.Collections;
using System.Runtime.InteropServices;

namespace MLPlatform.Agent.Terminal;

/// <summary>openpty + posix_spawnp; on Linux `setsid -c` makes the slave the controlling terminal (posix_spawn cannot);
/// macOS (development only) starts the shell in a new session without a controlling terminal.</summary>
public sealed class UnixPty : IPty
{
    private const int SigHup = 1, SigKill = 9, SpawnStructBytes = 1024;
    private const short MacSetSid = 0x0400;
    private readonly int _master;
    private readonly int _pid;
    private int _exited;

    public Stream Output { get; }
    public Stream Input { get; }

    private UnixPty(int master, int pid)
    {
        _master = master;
        _pid = pid;
        Output = new FdStream(UnixNative.Dup(master));
        Input = new FdStream(UnixNative.Dup(master));
    }

    public static UnixPty Start(string shell, string cwd, int cols, int rows)
    {
        var size = new UnixNative.WinSize { Cols = (ushort)cols, Rows = (ushort)rows };
        if (UnixNative.OpenPty(out var master, out var slave, ref size) != 0)
            throw new IOException("openpty failed: " + Marshal.GetLastPInvokeError());
        try { return new UnixPty(master, Spawn(slave, Argv(shell, cwd), Env(cwd))); }
        catch { UnixNative.Close(master); throw; }
        finally { UnixNative.Close(slave); }
    }

    private const string EnterCwd = "cd \"$0\" 2>/dev/null; exec \"$1\" -l";

    private static string[] Argv(string shell, string cwd)
    {
        string[] inner = ["/bin/sh", "-c", EnterCwd, cwd, shell];
        if (OperatingSystem.IsMacOS()) return inner;
        if (File.Exists("/usr/bin/setsid") || File.Exists("/bin/setsid")) return ["setsid", "-c", .. inner];
        return ["script", "-qfc", $"cd '{cwd.Replace("'", "'\\''")}' 2>/dev/null; exec '{shell}' -l", "/dev/null"];
    }

    private static string[] Env(string cwd)
    {
        var env = System.Environment.GetEnvironmentVariables().Cast<DictionaryEntry>()
            .ToDictionary(e => (string)e.Key, e => (string?)e.Value ?? "", StringComparer.Ordinal);
        env["TERM"] = "xterm-256color";
        env["PWD"] = cwd;
        env.TryAdd("HOME", "/root");
        env.TryAdd("LANG", "C.UTF-8");
        return [.. env.Select(kv => kv.Key + "=" + kv.Value)];
    }

    private static int Spawn(int slave, string[] argv, string[] env)
    {
        var actions = Marshal.AllocHGlobal(SpawnStructBytes);
        var attr = Marshal.AllocHGlobal(SpawnStructBytes);
        using var args = new NativeStrings(argv);
        using var envp = new NativeStrings(env);
        var file = Marshal.StringToCoTaskMemUTF8(argv[0]);
        try
        {
            UnixNative.FileActionsInit(actions);
            UnixNative.AttrInit(attr);
            if (OperatingSystem.IsMacOS()) UnixNative.AttrSetFlags(attr, MacSetSid);
            for (var fd = 0; fd <= 2; fd++) UnixNative.FileActionsAddDup2(actions, slave, fd);
            var rc = UnixNative.SpawnP(out var pid, file, actions, attr, args.Pointer, envp.Pointer);
            return rc == 0 ? pid : throw new IOException($"posix_spawnp {argv[0]} failed: {rc}");
        }
        finally
        {
            UnixNative.FileActionsDestroy(actions);
            UnixNative.AttrDestroy(attr);
            Marshal.FreeHGlobal(actions);
            Marshal.FreeHGlobal(attr);
            Marshal.FreeCoTaskMem(file);
        }
    }

    public void Resize(int cols, int rows)
    {
        if (!OperatingSystem.IsLinux()) return;
        var size = new UnixNative.WinSize { Cols = (ushort)cols, Rows = (ushort)rows };
        UnixNative.Ioctl(_master, LinuxTiocswinsz, ref size);
    }

    private const nuint LinuxTiocswinsz = 0x5414;

    public Task<int> WaitForExitAsync(CancellationToken ct) => Task.Factory.StartNew(() =>
    {
        UnixNative.WaitPid(_pid, out var status, 0);
        Interlocked.Exchange(ref _exited, 1);
        return (status & 0x7f) == 0 ? (status >> 8) & 0xff : 128 + (status & 0x7f);
    }, ct, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    public void Kill()
    {
        if (Volatile.Read(ref _exited) == 1) return;
        UnixNative.Kill(-_pid, SigHup);
        UnixNative.Kill(_pid, SigHup);
        _ = Task.Delay(TimeSpan.FromSeconds(3)).ContinueWith(_ =>
        {
            if (Volatile.Read(ref _exited) == 1) return;
            UnixNative.Kill(-_pid, SigKill);
            UnixNative.Kill(_pid, SigKill);
        }, TaskScheduler.Default);
    }

    public void Dispose()
    {
        Kill();
        Output.Dispose();
        Input.Dispose();
        UnixNative.Close(_master);
    }
}
