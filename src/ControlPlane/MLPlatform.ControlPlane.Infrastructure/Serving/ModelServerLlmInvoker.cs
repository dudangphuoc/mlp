using System.Text.Json.Nodes;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using MLPlatform.ControlPlane.Application.LlmOps;
using MLPlatform.Contracts.V1;
using MLPlatform.Cqrs;

namespace MLPlatform.ControlPlane.Infrastructure;

/// <summary>ILlmInvoker over the ModelServer gRPC API (chat input, generate output).</summary>
public sealed class ModelServerLlmInvoker(ModelServerClients clients) : ILlmInvoker
{
    public async Task<(string Text, int PromptTokens, int CompletionTokens)> ChatAsync(string endpoint, IReadOnlyList<(string Role, string Content)> messages,
        int maxTokens, double temperature, CancellationToken ct)
    {
        var chat = new ChatInput { MaxTokens = maxTokens, Temperature = (float)temperature };
        chat.Messages.AddRange(messages.Select(m => new ChatMessage { Role = m.Role, Content = m.Content }));
        var req = new PredictRequest();
        req.Inputs.Add(new TaskInput { Chat = chat });
        PredictResponse res;
        try { res = await clients.For(endpoint).PredictAsync(req, cancellationToken: ct); }
        catch (RpcException e) { throw new UnavailableException("SERVE_REPLICA_ERROR", "Replica error: " + e.Status.Detail); }
        var o = res.Outputs.FirstOrDefault();
        if (o?.KindCase != TaskOutput.KindOneofCase.Generate) throw new InvalidArgumentException("LLM_NOT_GENERATIVE", "Deployment does not return text generation output (task is not an LLM?)");
        return (o.Generate.Text, o.Generate.PromptTokens, o.Generate.CompletionTokens);
    }
}
