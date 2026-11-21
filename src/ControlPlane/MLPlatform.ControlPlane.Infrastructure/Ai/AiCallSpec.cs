using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Ai;
using MLPlatform.Cqrs;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Infrastructure;

/// <summary>What a call is for. The purpose/subject/trigger are shown to the user verbatim.</summary>
public sealed record AiCallSpec(string Kind, string Role, string Purpose, string? Subject, string Trigger, Guid? WorkspaceId, Guid? ActorId,
    string[]? NeedInputs = null, Guid? PinnedModel = null, int? DeadlineMs = null, int? MaxTokens = null, bool UpdateHealth = true);
