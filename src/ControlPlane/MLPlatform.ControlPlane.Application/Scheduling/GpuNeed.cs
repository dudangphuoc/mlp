using System.Text.Json;
using System.Text.Json.Serialization;

namespace MLPlatform.ControlPlane.Application.Scheduling;

/// <summary>D-50: one GPU runs several jobs.</summary>
public static class GpuNeed
{
    public const int ServeMb = 2048, NotebookMb = 4096;

    public static int Mb(JobSpecDto spec)
    {
        if (spec.Gpus != 1) return 0;
        if (spec.GpuMemoryMb > 0) return spec.GpuMemoryMb;
        if (spec.Env.ContainsKey("MLP_DEPLOYMENT")) return ServeMb;
        if (spec.Env.ContainsKey("MLP_NOTEBOOK")) return NotebookMb;
        if (!spec.Env.TryGetValue("MLP_JOB_SPEC", out var json)) return 0;
        try
        {
            var o = System.Text.Json.Nodes.JsonNode.Parse(json);
            var gb = Number(o?["variant"]?["minVramGb"]) ?? Number(o?["manifest"]?["environment"]?["minVramGb"]);
            if (gb is not > 0) return 0;
            var mb = (int)(gb.Value * 1024);
            return o?["entrypoint"]?.ToString() == "train" ? mb : Math.Max(ServeMb, mb / 2);
        }
        catch (JsonException) { return 0; }
    }

    /// <summary>Share of a GPU with gpuTotalMb memory; 0 = the whole GPU (budget unknown, or it does not leave room for
    /// anything else).</summary>
    public static double Fraction(int needMb, long gpuTotalMb)
    {
        if (needMb <= 0 || gpuTotalMb <= 0) return 0;
        var f = Math.Floor(needMb * 10000.0 / gpuTotalMb) / 10000.0;
        return f >= 0.9999 ? 0 : Math.Max(f, 0.0001);
    }

    private static double? Number(System.Text.Json.Nodes.JsonNode? n) =>
        n is System.Text.Json.Nodes.JsonValue v && v.TryGetValue<double>(out var d) ? d : null;
}
