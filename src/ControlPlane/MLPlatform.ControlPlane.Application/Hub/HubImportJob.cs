using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Registry;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Hub;

/// <summary>HF import job (read by the worker).</summary>
public sealed record HubImportJob(Guid Id, Guid WorkspaceId, string Kind, string Repo, string Revision, string[] Include, Guid? TargetId, string TargetName,
    string Task, string State, Guid CreatedBy, string Source, string Mode);
