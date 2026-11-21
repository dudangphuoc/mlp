using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Serving;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.LlmOps;

public sealed record LlmCallResult(string Text, int PromptTokens, int CompletionTokens, double LatencyMs, double Cost, bool Blocked, string? Reason,
    IReadOnlyList<string> Flags, IReadOnlyList<(string Role, string Content)> SentMessages);
