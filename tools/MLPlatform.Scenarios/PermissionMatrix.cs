using Google.Protobuf;
using Google.Protobuf.Reflection;
using Grpc.Core;
using Grpc.Net.Client;
using MLPlatform.Contracts.V1;

/// <summary>Calls every unary RPC that takes a workspace_id as an outsider and anonymously; any OK answer is a leak.</summary>
public static class PermissionMatrix
{
    public sealed record Outcome(string Method, StatusCode Outsider, StatusCode Anonymous);

    public static async Task<List<Outcome>> RunAsync(GrpcChannel channel, string workspaceId, Metadata outsider)
    {
        var invoker = channel.CreateCallInvoker();
        var outcomes = new List<Outcome>();
        foreach (var (service, method) in WorkspaceMethods())
        {
            var request = Request(method.InputType, workspaceId);
            outcomes.Add(new Outcome($"{service.Name}/{method.Name}",
                await CallAsync(invoker, service, method, request, outsider),
                await CallAsync(invoker, service, method, request, [])));
        }
        return outcomes;
    }

    private static readonly FileDescriptor[] Files =
    [
        AiReflection.Descriptor, AuditReflection.Descriptor, CatalogReflection.Descriptor, DataReflection.Descriptor, FeatureReflection.Descriptor,
        FleetReflection.Descriptor, HubReflection.Descriptor, IamReflection.Descriptor, LabelingReflection.Descriptor, LlmopsReflection.Descriptor,
        MonitoringReflection.Descriptor, NotebookReflection.Descriptor, NotifyReflection.Descriptor, OpsReflection.Descriptor, PipelineReflection.Descriptor,
        RegistryReflection.Descriptor, SchedulerReflection.Descriptor, ServingReflection.Descriptor, SettingsReflection.Descriptor,
        TrackingReflection.Descriptor, TuningReflection.Descriptor,
    ];

    private static IEnumerable<(ServiceDescriptor Service, MethodDescriptor Method)> WorkspaceMethods() =>
        Files.SelectMany(f => f.Services)
            .SelectMany(s => s.Methods.Select(m => (s, m)))
            .Where(x => !x.m.IsClientStreaming && !x.m.IsServerStreaming && x.m.InputType.FindFieldByName("workspace_id") is not null);

    private static IMessage Request(MessageDescriptor type, string workspaceId)
    {
        var message = type.Parser.ParseFrom(ByteString.Empty);
        foreach (var field in type.Fields.InFieldNumberOrder())
        {
            if (field.FieldType != FieldType.String || field.IsRepeated || field.IsMap) continue;
            if (field.Name == "workspace_id") field.Accessor.SetValue(message, workspaceId);
            else if (field.Name.EndsWith("_id", StringComparison.Ordinal)) field.Accessor.SetValue(message, Guid.NewGuid().ToString());
        }
        return message;
    }

    private static async Task<StatusCode> CallAsync(CallInvoker invoker, ServiceDescriptor service, MethodDescriptor method, IMessage request, Metadata headers)
    {
        var call = new Method<IMessage, IMessage>(MethodType.Unary, service.FullName, method.Name,
            Marshallers.Create((IMessage m) => m.ToByteArray(), b => method.InputType.Parser.ParseFrom(b)),
            Marshallers.Create((IMessage m) => m.ToByteArray(), b => method.OutputType.Parser.ParseFrom(b)));
        try
        {
            await invoker.AsyncUnaryCall(call, null, new CallOptions(headers), request);
            return StatusCode.OK;
        }
        catch (RpcException e) { return e.StatusCode; }
    }
}
