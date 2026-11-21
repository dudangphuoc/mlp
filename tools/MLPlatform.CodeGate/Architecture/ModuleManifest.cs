using System.Text.Json;

namespace MLPlatform.CodeGate.Architecture;

/// <summary>Application module layers and allowed same-layer edges, read from Application/modules.json.</summary>
public sealed class ModuleManifest
{
    public const string ApplicationRoot = "src/ControlPlane/MLPlatform.ControlPlane.Application";

    private readonly Dictionary<string, int> _layerOf = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _uses = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> Modules => _layerOf.Keys;

    public static ModuleManifest Load(RepoFiles repo)
    {
        var manifest = new ModuleManifest();
        var path = Path.Combine(repo.Root, ApplicationRoot, "modules.json");
        if (!File.Exists(path)) return manifest;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var layers = doc.RootElement.GetProperty("layers").EnumerateArray().Select(l => l.GetString()!).ToList();
        foreach (var module in doc.RootElement.GetProperty("modules").EnumerateObject())
        {
            manifest._layerOf[module.Name] = layers.IndexOf(module.Value.GetProperty("layer").GetString()!);
            manifest._uses[module.Name] = module.Value.TryGetProperty("uses", out var uses)
                ? [.. uses.EnumerateArray().Select(u => u.GetString()!)]
                : [];
        }
        return manifest;
    }

    public bool Knows(string module) => _layerOf.ContainsKey(module);

    public bool Allows(string from, string to) =>
        !Knows(from) || !Knows(to) || _layerOf[to] < _layerOf[from] || _uses[from].Contains(to);
}
