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

public sealed record AiResult(Guid RunId, bool Ok, string? Text, JsonObject? Answers, string DecidedBy, double? Confidence, string? Fallback,
    string? ErrorCode, string? Error, int LatencyMs, Guid? ModelId, string? ModelLabel);
