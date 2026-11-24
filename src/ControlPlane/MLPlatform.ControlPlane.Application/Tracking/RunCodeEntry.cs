using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>Start a repository script from the web: the ref becomes a commit, the server fetches that commit and runs
/// the script with the given parameters (as --name value) on the chosen machine.</summary>
[Access(AccessKind.ScopedPermission, "job.submit")]
public sealed record RunCodeEntryCommand(Guid WorkspaceId, Guid EntryId, string? Ref, string? Machine, IReadOnlyDictionary<string, string> Params)
    : ICommand<CodeRunOpened>;

public sealed class RunCodeEntryCommandHandler(IReadSession db, WorkspaceAuthorizer auth, ICodeSnapshotComposer composer, IMediator mediator)
    : ICommandHandler<RunCodeEntryCommand, CodeRunOpened>
{
    public async ValueTask<CodeRunOpened> Handle(RunCodeEntryCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "job.submit", ct);
        var entry = await db.GetCodeEntryAsync(c.EntryId, c.WorkspaceId, ct) ?? throw new NotFoundException("TRACK_ENTRY_NOT_FOUND", "Entry not found");
        var project = await db.GetProjectAsync(entry.ProjectId, c.WorkspaceId, ct) ?? throw new NotFoundException("TRACK_PROJECT_NOT_FOUND", "Project not found");
        var source = CodeSourceUrls.Match(await db.ListCodeSourcesAsync(c.WorkspaceId, ct), entry.GitRemote)
            ?? throw new PreconditionException("TRACK_SOURCE_NOT_FOUND", $"No code source covers {entry.GitRemote}");
        var reference = string.IsNullOrWhiteSpace(c.Ref) ? entry.Ref : c.Ref.Trim();
        var commit = await composer.ResolveCommitAsync(source, entry.GitRemote, reference, ct);
        var snapshot = await composer.ComposeAsync(c.WorkspaceId, source, entry.GitRemote, commit, null, [], ct);
        var requirements = await composer.ReadFileAsync(c.WorkspaceId, snapshot, "requirements.txt", ct) ?? "";
        var args = c.Params.Where(p => p.Key.Length > 0).SelectMany(p => new[] { "--" + p.Key, p.Value }).ToList();
        var remote = new RemoteCodeRun(string.IsNullOrWhiteSpace(c.Machine) ? entry.Machine : c.Machine.Trim(), entry.Gpus, entry.Image, snapshot, "",
            ["python", entry.Script, .. args], new Dictionary<string, string>(), requirements);
        return await mediator.Send(new OpenCodeRunCommand(c.WorkspaceId.ToString(), project.Slug, entry.Name, ParamsJson(c.Params), "",
            CodeJson(entry, commit, reference, args), remote), ct);
    }

    private static string ParamsJson(IReadOnlyDictionary<string, string> p) =>
        new JsonObject(p.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)JsonValue.Create(kv.Value)))).ToJsonString();

    private static string CodeJson(CodeEntryRow e, string commit, string reference, IReadOnlyList<string> args) => new JsonObject
    {
        ["gitRemote"] = e.GitRemote, ["commit"] = commit, ["dirty"] = false, ["script"] = e.Script,
        ["argv"] = new JsonArray(args.Select(a => (JsonNode?)JsonValue.Create(a)).ToArray()), ["python"] = "", ["branch"] = reference,
    }.ToJsonString();
}
