using System.Text.Json;

namespace MLPlatform.DocGen;

/// <summary>Modules, layers and declared cross-module APIs from Application/modules.json.</summary>
public static class ModuleCatalog
{
    public static IReadOnlyList<ModuleInfo> Load(string root)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "src/ControlPlane/MLPlatform.ControlPlane.Application/modules.json")));
        return [.. doc.RootElement.GetProperty("modules").EnumerateObject().Select(m => new ModuleInfo(
            m.Name,
            m.Value.GetProperty("layer").GetString()!,
            Strings(m.Value, "uses"),
            Strings(m.Value, "sql"),
            m.Value.GetProperty("spec").GetString()!,
            Strings(m.Value, "api"),
            m.Value.TryGetProperty("publicSql", out var shared) && shared.GetBoolean()))];
    }

    private static IReadOnlyList<string> Strings(JsonElement module, string property) =>
        module.TryGetProperty(property, out var values) ? [.. values.EnumerateArray().Select(v => v.GetString()!)] : [];
}
