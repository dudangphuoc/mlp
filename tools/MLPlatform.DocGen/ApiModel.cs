using Google.Protobuf.Reflection;

namespace MLPlatform.DocGen;

/// <summary>Everything the docs and the OpenAPI document are built from, read from code and migrations.</summary>
public sealed record ApiModel(
    IReadOnlyList<FileDescriptor> ProtoFiles,
    IReadOnlyList<RequestInfo> Requests,
    IReadOnlyDictionary<string, IReadOnlyList<string>> RpcRequests,
    IReadOnlyList<RestRoute> RestRoutes,
    IReadOnlyList<ModuleInfo> Modules,
    IReadOnlyList<DbTable> Tables)
{
    public static ApiModel Load(string root) => new(
        ProtoCatalog.Files,
        RequestCatalog.Load(),
        RpcBindings.Scan(root),
        MLPlatform.DocGen.RestRoutes.Scan(root),
        ModuleCatalog.Load(root),
        DbSchema.Load(root));

    public IEnumerable<ServiceDescriptor> Services => ProtoFiles.SelectMany(f => f.Services).OrderBy(s => s.Name, StringComparer.Ordinal);

    public IEnumerable<RequestInfo> RequestsFor(MethodDescriptor method) =>
        RpcRequests.TryGetValue($"{method.Service.Name}/{method.Name}", out var names)
            ? Requests.Where(r => names.Contains(r.ShortName))
            : [];
}
