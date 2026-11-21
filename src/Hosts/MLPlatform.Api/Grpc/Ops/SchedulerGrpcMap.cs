using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Timeline;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using MLPlatform.Api.Middleware;
using MLPlatform.Api.Http.Inference;
using MLPlatform.Api.Http.Install;
using MLPlatform.Api.Http.Mcp;
using MLPlatform.Api.Http.Notebooks;
using MLPlatform.ControlPlane.Infrastructure;
using MLPlatform.ControlPlane.Application.Ops;
using MLPlatform.Contracts.V1;
using MLPlatform.Cqrs;

namespace MLPlatform.Api.Grpc;

internal static class SchedulerGrpcMap
{
    public static OpsEvent Event(EventRow e) => new()
    {
        Id = e.Id.ToString(), At = Timestamp.FromDateTimeOffset(e.At), Source = e.Source, ResourceType = e.ResourceType, ResourceId = e.ResourceId?.ToString() ?? "",
        Kind = e.Kind, Severity = e.Severity, Message = e.Message, DataJson = e.Data,
    };
}
