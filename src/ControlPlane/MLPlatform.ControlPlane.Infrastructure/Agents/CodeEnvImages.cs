using System.Text;
using Google.Protobuf;
using MLPlatform.Contracts.V1;

namespace MLPlatform.ControlPlane.Infrastructure;

/// <summary>Environment image for code runs: base image + requirements.txt + the Python SDK; the code itself is
/// fetched when the container starts.</summary>
public sealed class CodeEnvImages : MLPlatform.ControlPlane.Application.Tracking.ICodeEnvironment
{
    private const string Prefix = "mlp/code-env:";

    private const string Dockerfile = """
        FROM __BASE__
        ENV PIP_BREAK_SYSTEM_PACKAGES=1 PYTHONUNBUFFERED=1 PYTHONPATH=/opt/mlp-sdk
        RUN command -v python >/dev/null || ln -s "$(command -v python3)" /usr/local/bin/python
        COPY requirements.txt /opt/mlp-env/requirements.txt
        RUN if [ -s /opt/mlp-env/requirements.txt ]; then python -m pip install --no-cache-dir -r /opt/mlp-env/requirements.txt; fi
        RUN python -m pip install --no-cache-dir debugpy==1.8.20
        COPY mlplatform /opt/mlp-sdk/mlplatform
        """;

    private static readonly Lazy<Dictionary<string, byte[]>> SdkFiles = new(LoadSdk);

    public string Fingerprint { get; } = ComputeFingerprint();

    public static ImageBuild? Build(string image, IReadOnlyDictionary<string, string> env)
    {
        if (!image.StartsWith(Prefix, StringComparison.Ordinal) || !env.TryGetValue("MLP_CODE_ENV_BASE", out var baseImage)) return null;
        var build = new ImageBuild { Tag = image, TimeoutSeconds = 45 * 60 };
        build.Files["Dockerfile"] = ByteString.CopyFromUtf8(Dockerfile.Replace("__BASE__", baseImage) + "\n");
        build.Files["requirements.txt"] = ByteString.CopyFromUtf8(env.GetValueOrDefault("MLP_CODE_ENV_REQUIREMENTS", ""));
        foreach (var (name, data) in SdkFiles.Value) build.Files[name] = ByteString.CopyFrom(data);
        return build;
    }

    private static Dictionary<string, byte[]> LoadSdk()
    {
        var assembly = typeof(CodeEnvImages).Assembly;
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith("sdk/mlplatform/", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            files[name["sdk/".Length..]] = Encoding.UTF8.GetBytes(reader.ReadToEnd().Replace("\r\n", "\n"));
        }
        return files;
    }

    private static string ComputeFingerprint()
    {
        using var sha = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        sha.AppendData(Encoding.UTF8.GetBytes(Dockerfile));
        foreach (var (name, data) in SdkFiles.Value.OrderBy(kv => kv.Key, StringComparer.Ordinal)) { sha.AppendData(Encoding.UTF8.GetBytes(name)); sha.AppendData(data); }
        return Convert.ToHexStringLower(sha.GetHashAndReset())[..16];
    }
}
