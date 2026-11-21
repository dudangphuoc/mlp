using MLPlatform.ControlPlane.Infrastructure;
using Grpc.Core;
using Grpc.Core.Interceptors;
using MLPlatform.Cqrs;

namespace MLPlatform.Api.Middleware;

/// <summary>AppException → gRPC status + trailers with the reason code and field errors (20/03 §4, 20/07 §3).</summary>
public sealed class ErrorInterceptor(ILogger<ErrorInterceptor> log) : Interceptor
{
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(TRequest request,
        ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            return await continuation(request, context);
        }
        catch (AppException e)
        {
            throw Map(e);
        }
        catch (Exception e) when (e is not RpcException)
        {
            log.LogError(e, "RPC {Method} failed", context.Method);
            throw new RpcException(new Status(StatusCode.Internal, "Internal error"));
        }
    }

    private static RpcException Map(AppException e)
    {
        var code = e switch
        {
            NotFoundException => StatusCode.NotFound,
            ConflictException => StatusCode.AlreadyExists,
            ForbiddenException => StatusCode.PermissionDenied,
            UnauthenticatedException => StatusCode.Unauthenticated,
            PreconditionException => StatusCode.FailedPrecondition,
            ResourceExhaustedException => StatusCode.ResourceExhausted,
            UnavailableException => StatusCode.Unavailable,
            ConcurrencyConflictException => StatusCode.Aborted,
            ValidationException => StatusCode.InvalidArgument,
            InvalidArgumentException => StatusCode.InvalidArgument,
            AbortedException => StatusCode.Aborted,
            _ => StatusCode.Unknown,
        };
        var trailers = new Metadata { { "mlp-error-reason", e.Reason } };
        foreach (var (k, v) in e.Metadata)
            trailers.Add("mlp-field-" + System.Text.RegularExpressions.Regex.Replace(k.ToLowerInvariant(), "[^a-z0-9_.-]", ".").Trim('.'), Uri.EscapeDataString(v));
        return new RpcException(new Status(code, e.Message), trailers);
    }
}
