using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Registry;

/// <summary>Rules of an optimization run: valid candidates per device, image picking, storage layout, the job's
/// script.</summary>
public static class OptimizationRules
{
    /// <summary>Only pairs that make the model faster: FP16 pays off on a GPU, INT8 (without TensorRT) only on a
    /// CPU.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Allowed = new Dictionary<string, string[]>
    {
        ["cpu"] = ["fp32", "int8"],
        ["cuda"] = ["fp32", "fp16"],
    };
    public static readonly string[] Tasks = ["detect", "classify"];
    public static string Prefix(Guid ws, Guid id) => $"{ws}/optimizations/{id}/";
    public const int WorstImages = 6;

    /// <summary>The job's script (python/mlp_optimize/pipeline.py, embedded); also stored with every run so it can be
    /// run again byte for byte.</summary>
    public static string Script()
    {
        using var s = typeof(OptimizationRules).Assembly.GetManifestResourceStream("optimize/pipeline.py") ?? throw new InvalidOperationException("missing optimize/pipeline.py");
        using var r = new StreamReader(s);
        return r.ReadToEnd().Replace("\r\n", "\n");
    }

    /// <summary>Deterministic pick of n items (same seed → same images, so runs can be compared).</summary>
    public static List<T> Pick<T>(IEnumerable<T> items, int n, string seed, Func<T, string> key) =>
        [.. items.OrderBy(i => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed + ":" + key(i)))), StringComparer.Ordinal).Take(n)];
}
