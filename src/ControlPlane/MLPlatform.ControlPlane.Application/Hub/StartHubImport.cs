using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Registry;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Hub;

/// <summary>Starts an import from HF Hub.</summary>
public sealed record StartHubImportCommand(Guid WorkspaceId, string Kind, string Repo, string Revision, string[] Include, Guid? DatasetId,
    string TargetName, string Task, string Source = "hf", string Mode = "copy") : ICommand<Guid>;

/// <summary>Handles <see cref="StartHubImportCommand"/>.</summary>
public sealed class StartHubImportCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth, IMediator mediator) : ICommandHandler<StartHubImportCommand, Guid>
{
    public async ValueTask<Guid> Handle(StartHubImportCommand c, CancellationToken ct)
    {
        if (c.Source is not ("hf" or "url" or "s3")) throw new ValidationException(new Dictionary<string, string> { ["source"] = "hf | url | s3" });
        if (c.Mode is not ("copy" or "reference")) throw new ValidationException(new Dictionary<string, string> { ["mode"] = "copy | reference" });
        var repo = c.Repo.Trim();
        var include = c.Include.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToArray();
        switch (c.Source)
        {
            case "hf":
                repo = repo.Trim('/');
                if (!System.Text.RegularExpressions.Regex.IsMatch(repo, @"^[A-Za-z0-9][\w.-]*(/[\w.-]+)?$"))
                    throw new ValidationException(new Dictionary<string, string> { ["repo"] = "Use the form org/name, e.g. facebook/detr-resnet-50" });
                break;
            case "url":
                if (include.Length == 0 || include.Any(u => !Uri.TryCreate(u, UriKind.Absolute, out var x) || x.Scheme is not ("https" or "http")))
                    throw new ValidationException(new Dictionary<string, string> { ["urls"] = "Enter one or more http(s) file URLs" });
                repo = include.Length == 1 ? include[0] : $"{include.Length} URLs";
                break;
            case "s3":
                if (!System.Text.RegularExpressions.Regex.IsMatch(repo, @"^s3://[a-z0-9][a-z0-9.-]{1,62}(/.*)?$"))
                    throw new ValidationException(new Dictionary<string, string> { ["repo"] = "Use the form s3://bucket/prefix" });
                break;
        }
        if (c.Kind is not ("dataset" or "model")) throw new ValidationException(new Dictionary<string, string> { ["kind"] = "dataset | model" });
        if (string.IsNullOrWhiteSpace(c.Task)) throw new ValidationException(new Dictionary<string, string> { ["task"] = "Select a task" });
        var uid = await auth.RequireAsync(c.WorkspaceId, c.Kind == "dataset" ? "dataset.write" : "model.write", ct);
        var name = string.IsNullOrWhiteSpace(c.TargetName) ? repo.TrimEnd('/').Split('/')[^1].Split('?')[0] : c.TargetName.Trim();
        Guid? target = c.DatasetId;
        if (c.Kind == "dataset" && target is null)
        {
            var slug = new string(name.ToLowerInvariant().Select(ch => char.IsAsciiLetterOrDigit(ch) ? ch : '-').ToArray()).Trim('-');
            var ds = await mediator.Send(new CreateDatasetCommand(c.WorkspaceId, slug.Length >= 2 ? slug : "import-" + slug, name, c.Task), ct);
            target = ds.Id;
        }
        var id = Ids.New();
        await db.InsertImportAsync(id, c.WorkspaceId, c.Kind, repo, c.Source != "hf" ? "" : string.IsNullOrWhiteSpace(c.Revision) ? "main" : c.Revision.Trim(),
            include, target, name, c.Task, uid, clock.UtcNow, c.Source, c.Mode, ct);
        return id;
    }
}
