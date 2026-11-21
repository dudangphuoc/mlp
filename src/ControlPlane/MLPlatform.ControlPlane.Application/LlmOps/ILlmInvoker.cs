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

/// <summary>Calls the text generation model on the deployment's replica (implemented by the host with the ModelServer
/// gRPC client).</summary>
public interface ILlmInvoker
{
    Task<(string Text, int PromptTokens, int CompletionTokens)> ChatAsync(string endpoint, IReadOnlyList<(string Role, string Content)> messages,
        int maxTokens, double temperature, CancellationToken ct);
}
