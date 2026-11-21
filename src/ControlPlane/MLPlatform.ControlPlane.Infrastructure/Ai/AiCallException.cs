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

/// <summary>A failed provider call, classified by status (never by message text).</summary>
public sealed class AiCallException(string outcome, string message, int status = 0, int tokensIn = 0, int tokensOut = 0) : Exception(message)
{
    public string Outcome { get; } = outcome;
    public int Status { get; } = status;
    /// <summary>Tokens the provider billed even though the call failed (e.g. an empty answer) — still shown and costed.</summary>
    public int TokensIn { get; } = tokensIn;
    public int TokensOut { get; } = tokensOut;
}
