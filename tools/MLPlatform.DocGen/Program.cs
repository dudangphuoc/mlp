using MLPlatform.DocGen;

var root = Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal)) ?? ".");
var check = args.Contains("--check");
var model = ApiModel.Load(root);
var outputs = new Dictionary<string, string>(StringComparer.Ordinal)
{
    ["src/Hosts/MLPlatform.Api/OpenApi/openapi.json"] = OpenApiWriter.Write(model),
};
foreach (var (path, text) in MarkdownWriter.Write(model)) outputs["docs/generated/" + path] = text;
return GeneratedFiles.Sync(root, outputs, check);
