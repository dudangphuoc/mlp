using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Recipes;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Catalog;

/// <summary>A recipe image on one machine: <c>building</c>, <c>ready</c> or <c>failed</c> for one image tag.</summary>
public sealed record EnvironmentRow(Guid Id, Guid WorkspaceId, string RecipeName, string EnvTag, Guid HostId, string State, string Error,
    DateTimeOffset StartedAt, DateTimeOffset? BuiltAt);
