using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Registry;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Hub;

/// <summary>Import history row (UI).</summary>
public sealed record HubImportRow(Guid Id, string Kind, string Repo, string Revision, Guid? TargetId, string TargetName, string Task, string State,
    int FilesTotal, int FilesDone, long BytesTotal, long BytesDone, string Message, DateTimeOffset CreatedAt, DateTimeOffset? FinishedAt, string Source, string Mode);
