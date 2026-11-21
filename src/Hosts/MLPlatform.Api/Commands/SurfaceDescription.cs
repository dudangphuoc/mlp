using System.Text.Json;
using MLPlatform.ControlPlane.Application.Generated;

namespace MLPlatform.Api.Commands;

/// <summary>`mlp-api describe [file]`: writes the API surface and DI graph as JSON so refactors can be
/// diffed.</summary>
public static class SurfaceDescription
{
    public static bool IsRequested(string[] args) => args is ["describe", ..];

    public static void PrepareConfiguration()
    {
        Environment.SetEnvironmentVariable("MLP__Db__ConnectionString", Environment.GetEnvironmentVariable("MLP__Db__ConnectionString") ?? "Host=127.0.0.1;Database=describe");
        Environment.SetEnvironmentVariable("MLP__Secrets__TokenPepper", Environment.GetEnvironmentVariable("MLP__Secrets__TokenPepper") ?? "describe");
    }

    public static void Write(string[] args, IServiceCollection services, IEndpointRouteBuilder endpoints)
    {
        var path = args.Length > 1 ? args[1] : "surface.json";
        using var stream = File.Create(path);
        using var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        json.WriteStartObject();
        WriteEndpoints(json, endpoints);
        WriteServices(json, services);
        WriteRequests(json);
        json.WriteEndObject();
        json.Flush();
        Console.WriteLine($"surface written to {path}");
    }

    private static void WriteEndpoints(Utf8JsonWriter json, IEndpointRouteBuilder endpoints)
    {
        var routes = endpoints.DataSources.SelectMany(d => d.Endpoints).OfType<RouteEndpoint>()
            .Select(e => (Pattern: e.RoutePattern.RawText ?? "", Methods: string.Join(',', e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["*"])))
            .OrderBy(r => r.Pattern, StringComparer.Ordinal).ThenBy(r => r.Methods, StringComparer.Ordinal);
        json.WriteStartArray("endpoints");
        foreach (var (pattern, methods) in routes) json.WriteStringValue($"{methods} {pattern}");
        json.WriteEndArray();
    }

    private static void WriteServices(Utf8JsonWriter json, IServiceCollection services)
    {
        json.WriteStartArray("services");
        foreach (var d in services)
            json.WriteStringValue($"{d.Lifetime} {d.ServiceType.FullName} => {Implementation(d)}");
        json.WriteEndArray();
    }

    private static string Implementation(ServiceDescriptor d) =>
        d.ImplementationType?.FullName ?? (d.ImplementationInstance is { } i ? $"instance {i.GetType().FullName}" : "factory");

    private static void WriteRequests(Utf8JsonWriter json)
    {
        json.WriteStartArray("requests");
        foreach (var (name, isCommand, access, permission) in CqrsRegistration.Requests.OrderBy(r => r.Name, StringComparer.Ordinal))
            json.WriteStringValue($"{(isCommand ? "command" : "query")} {name} {access} {permission ?? "-"}");
        json.WriteEndArray();
    }
}
