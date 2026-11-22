using System.Text.Json;

namespace MLPlatform.Agent.Gpu;

/// <summary>Lists running containers on the shared Docker daemon that hold GPUs, whichever agent created
/// them.</summary>
public static class GpuContainers
{
    private const string Format = "{{.Id}}|{{.Name}}|{{index .Config.Labels \"mlp.agent\"}}|{{index .Config.Labels \"mlp.attempt\"}}|" +
        "{{index .Config.Labels \"mlp.gpus\"}}|{{json .HostConfig.DeviceRequests}}";

    public static async Task<List<GpuContainer>> ListAsync(CancellationToken ct)
    {
        var (code, ids, _) = await Docker.RunAsync(["ps", "-q"], ct, TimeSpan.FromSeconds(20));
        var list = ids.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (code != 0 || list.Length == 0) return [];
        var (ic, outp, _) = await Docker.RunAsync(["inspect", "--format", Format, .. list], ct, TimeSpan.FromSeconds(20));
        return ic == 0 ? Parse(outp) : [];
    }

    public static List<GpuContainer> Parse(string inspectOutput)
    {
        var res = new List<GpuContainer>();
        foreach (var line in inspectOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var f = line.Split('|', 6);
            if (f.Length < 6) continue;
            var gpus = Devices(f[5]);
            if (gpus.Count == 0 && NvidiaSmi.FakeGpus && Clean(f[4]) is { Length: > 0 } fake) gpus = [.. fake.Split(',')];
            if (gpus.Count > 0) res.Add(new GpuContainer(f[0], f[1].TrimStart('/'), Clean(f[2]), Clean(f[3]), gpus));
        }
        return res;
    }

    private static string Clean(string v) => v == "<no value>" ? "" : v.Trim();

    /// <summary>GPU ids from HostConfig.DeviceRequests JSON; ["all"] for --gpus all.</summary>
    public static List<string> Devices(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "null") return [];
        var res = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];
            foreach (var req in doc.RootElement.EnumerateArray())
            {
                if (!IsGpuRequest(req)) continue;
                if (req.TryGetProperty("DeviceIDs", out var ids) && ids.ValueKind == JsonValueKind.Array && ids.GetArrayLength() > 0)
                    res.AddRange(ids.EnumerateArray().Select(i => i.GetString() ?? "").Where(i => i.Length > 0));
                else res.Add("all");
            }
        }
        catch (JsonException) { return []; }
        return res;
    }

    private static bool IsGpuRequest(JsonElement req) =>
        req.TryGetProperty("Capabilities", out var caps) && caps.ValueKind == JsonValueKind.Array
        && caps.EnumerateArray().Any(set => set.ValueKind == JsonValueKind.Array && set.EnumerateArray().Any(c => c.GetString() == "gpu"));
}
