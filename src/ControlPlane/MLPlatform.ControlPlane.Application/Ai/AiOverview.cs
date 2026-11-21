using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Platform;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Ai;

/// <summary>Everything the models page needs in one read (Instance Admin).</summary>
public sealed record AiOverview(List<AiConnectionRow> Connections, List<AiModelRow> Models, List<AiRoleDefaultRow> Defaults, List<AiModelStatsRow> Stats);
