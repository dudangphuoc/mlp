using System.Text.RegularExpressions;

namespace MLPlatform.CodeGate.Rules;

/// <summary>MLPAPI001: endpoints are mapped only under Api/Grpc and Api/Http; gRPC service classes live only under
/// Api/Grpc.</summary>
public static partial class ApiPlacementRules
{
    private const string ApiRoot = "src/Hosts/MLPlatform.Api/";

    public static IEnumerable<Finding> Check(RepoFiles repo)
    {
        foreach (var file in repo.Under("src", "*.cs"))
        {
            var path = repo.Relative(file);
            var text = File.ReadAllText(file);
            var inGrpc = path.StartsWith(ApiRoot + "Grpc/", StringComparison.Ordinal);
            var inHttp = path.StartsWith(ApiRoot + "Http/", StringComparison.Ordinal);
            if (!inGrpc && !inHttp && EndpointMapping().IsMatch(text))
                yield return new("MLPAPI001", path, path, "maps endpoints outside Api/Grpc and Api/Http");
            if (!inGrpc && GrpcServiceClass().IsMatch(text))
                yield return new("MLPAPI001", path, path, "gRPC service class outside Api/Grpc");
        }
    }

    [GeneratedRegex(@"\.(MapGrpcService<|MapGet\(|MapPost\(|MapPut\(|MapDelete\(|MapPatch\(|MapMethods\(|Map\(""/)")]
    private static partial Regex EndpointMapping();

    [GeneratedRegex(@"class \w+\([^)]*\)\s*:\s*\w+\.\w+ServiceBase|class \w+\s*:\s*\w+\.\w+ServiceBase")]
    private static partial Regex GrpcServiceClass();
}
