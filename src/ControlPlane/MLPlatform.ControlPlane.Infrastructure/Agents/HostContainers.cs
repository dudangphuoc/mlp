using System.Globalization;
using Google.Protobuf;
using MLPlatform.ControlPlane.Application.Fleet;
using MLPlatform.Contracts.V1;
using MLPlatform.Cqrs;

namespace MLPlatform.ControlPlane.Infrastructure;

/// <summary>D-38: the agent is only a container bridge; the control plane assembles the spec for each role (label
/// mlp.role ): net (WireGuard spoke mlp-net), ray-node (thin Ray worker), workload (attempt container.</summary>
public static class HostContainers
{
    public const string NetName = "mlp-net";
    private const string NetVolume = "mlp-net-state";
    private static readonly string NetDockerfile = Resource("mlp-net/Dockerfile");
    private static readonly string NetScript = Resource("mlp-net/mlp-net.sh");
    public static string NetImage { get; } =
        "mlp/net:" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(NetDockerfile + NetScript)))[..12];

    private static string Resource(string name)
    {
        using var s = typeof(HostContainers).Assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException("missing " + name);
        using var r = new StreamReader(s);
        return r.ReadToEnd().Replace("\r\n", "\n");
    }

    private static readonly string NotebookDockerfile = Resource("notebook/Dockerfile");

    /// <summary>Pip packages per notebook profile (M08): CPU uses python slim, GPU uses the PyTorch CUDA runtime.</summary>
    private static readonly Dictionary<string, string> NotebookPip = new()
    {
        ["base"] = "jupyterlab numpy pandas matplotlib scikit-learn",
        ["pytorch"] = "jupyterlab numpy pandas matplotlib torch torchvision",
        ["cv"] = "jupyterlab numpy pandas matplotlib pillow opencv-python-headless scikit-image",
        ["nlp-llm"] = "jupyterlab numpy pandas transformers datasets accelerate sentencepiece",
        ["tabular"] = "jupyterlab numpy pandas matplotlib scikit-learn xgboost lightgbm",
        ["audio"] = "jupyterlab numpy pandas librosa soundfile",
        ["tensorflow"] = "jupyterlab numpy pandas matplotlib tensorflow",
    };

    /// <summary>Notebook profile image of the form mlp/nb-{profile}:{ver}-{cpu|gpu} → build command on the host; other
    /// images (dev override) → null.</summary>
    public static ImageBuild? NotebookImage(string image)
    {
        var m = System.Text.RegularExpressions.Regex.Match(image, @"^mlp/nb-([a-z0-9-]+):[\w.]+-(cpu|gpu)$");
        if (!m.Success) return null;
        var gpu = m.Groups[2].Value == "gpu";
        var pip = NotebookPip.GetValueOrDefault(m.Groups[1].Value, NotebookPip["base"]);
        if (gpu) pip = pip.Replace(" torch torchvision", "");
        var df = NotebookDockerfile
            .Replace("ARG BASE=python:3.12-slim", "ARG BASE=" + (gpu ? "pytorch/pytorch:2.5.1-cuda12.4-cudnn9-runtime" : "python:3.12-slim"))
            .Replace("ARG PIP=\"jupyterlab\"", "ARG PIP=\"" + pip + "\"");
        var b = new ImageBuild { Tag = image };
        b.Files["Dockerfile"] = ByteString.CopyFromUtf8(df);
        return b;
    }

    /// <summary>The platform layer added to every recipe image (20/08 §6): mlp-serve with gRPC stubs generated for the
    /// image's own Python.</summary>
    private const string ServeLayer = """

        # ── platform layer (added by the control plane) ──
        COPY _serve/mlp_serve /opt/mlp-py/mlp_serve
        COPY _serve/proto /opt/mlp-py/proto
        # Debian/Ubuntu system Python (PEP 668) refuses pip without this; images may also ship only python3
        ENV PIP_BREAK_SYSTEM_PACKAGES=1
        RUN (command -v python >/dev/null || ln -s "$(command -v python3)" /usr/local/bin/python) \
         && python -m pip install --no-cache-dir grpcio grpcio-tools protobuf \
         && mkdir -p /opt/mlp-py/mlp_serve/gen /mnt/model /mnt/output && chmod 777 /mnt/model /mnt/output && cd /opt/mlp-py \
         && python -m grpc_tools.protoc -Iproto --python_out=mlp_serve/gen --grpc_python_out=mlp_serve/gen proto/mlplatform/v1/model_server.proto \
         && sed -i 's/^from mlplatform.v1 import/from mlp_serve.gen.mlplatform.v1 import/' mlp_serve/gen/mlplatform/v1/model_server_pb2_grpc.py \
         && touch mlp_serve/gen/__init__.py mlp_serve/gen/mlplatform/__init__.py mlp_serve/gen/mlplatform/v1/__init__.py \
         && printf '#!/bin/sh\nexec python -m mlp_serve.server "$@"\n' > /usr/local/bin/mlp-serve && chmod +x /usr/local/bin/mlp-serve
        ENV PYTHONPATH=/opt/mlp-py:/opt/recipe
        WORKDIR /opt/recipe
        """;

    /// <summary>Platform training layer (20/08 §6): mlp-shim (built by agent-builder for the host's
    /// architecture.</summary>
    private const string TrainLayer = """

        # ── platform training layer (added by the control plane) ──
        COPY _shared/converters /opt/mlp-convert
        ADD __MLP_SHIM_URL__ /usr/local/bin/mlp-shim
        RUN chmod 755 /usr/local/bin/mlp-shim && mkdir -p /mnt/data /mnt/weights && chmod 777 /mnt/data /mnt/weights
        """;

    /// <summary>recipes/{name}:current → build command for this host: the recipe's Dockerfile and files (context =
    /// recipes dir, as in CI) plus the platform serve layer and, when shim is given, the training layer.</summary>
    public static ImageBuild? RecipeImage(string image, string? recipesDir, (string Url, string Sha256)? shim = null)
    {
        var m = System.Text.RegularExpressions.Regex.Match(image, @"^recipes/([a-z0-9-]+):current$");
        if (!m.Success || string.IsNullOrEmpty(recipesDir)) return null;
        var name = m.Groups[1].Value;
        var dir = Path.Combine(recipesDir, name);
        if (!File.Exists(Path.Combine(dir, "Dockerfile"))) return null;
        var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var root in new[] { dir, Path.Combine(recipesDir, "_shared") })
        {
            if (!Directory.Exists(root)) continue;
            foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                files[Path.GetRelativePath(recipesDir, f).Replace('\\', '/')] = File.ReadAllBytes(f);
        }
        foreach (var r in new[] { "mlp_serve/__init__.py", "mlp_serve/server.py", "mlp_serve/types.py", "mlp_serve/selftest.py", "proto/mlplatform/v1/model_server.proto" })
            files["_serve/" + r] = System.Text.Encoding.UTF8.GetBytes(Resource("serve/" + r));
        var dockerfile = File.ReadAllText(Path.Combine(dir, "Dockerfile")).Replace("\r\n", "\n") + ServeLayer + (shim is null ? "" : TrainLayer);
        files["Dockerfile"] = System.Text.Encoding.UTF8.GetBytes(dockerfile);
        using var sha = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        foreach (var (k, v) in files) { sha.AppendData(System.Text.Encoding.UTF8.GetBytes(k)); sha.AppendData(v); }
        if (shim is { } sh) sha.AppendData(System.Text.Encoding.UTF8.GetBytes("mlp-shim:" + sh.Sha256));
        var b = new ImageBuild { Tag = $"mlp/recipe-{name}:{Convert.ToHexStringLower(sha.GetHashAndReset())[..12]}", TimeoutSeconds = 3600 };
        if (shim is { } s2) files["Dockerfile"] = System.Text.Encoding.UTF8.GetBytes(dockerfile.Replace("__MLP_SHIM_URL__", s2.Url));
        foreach (var (k, v) in files) b.Files[k] = ByteString.CopyFrom(v);
        return b;
    }

    public static string RayNodeName(Guid hostId) => "mlp-ray-node-" + hostId.ToString()[^12..];
    public static string WorkloadName(string attemptId, int rank) => $"mlp-w-{attemptId}-{rank}";

    /// <summary>Build the mlp-net spoke on the host (P0-16): build the image from the control plane's Dockerfile, get
    /// the public key (the private key never leaves the volume), store the key for the hub, run the spoke.</summary>
    public static async Task<bool> EnsureNetworkAsync(AgentSession s, string overlayIp, IConfiguration cfg, IServiceScopeFactory scopes, ILogger log, CancellationToken ct)
    {
        if (Overlay.HubPublicKey(cfg) is not { } hubKey || string.IsNullOrEmpty(overlayIp)) return false;
        var build = new ImageBuild { Tag = NetImage };
        build.Files["Dockerfile"] = ByteString.CopyFromUtf8(NetDockerfile);
        build.Files["mlp-net.sh"] = ByteString.CopyFromUtf8(NetScript);
        var b = await s.SendCommandAsync(new AgentCommand { ImageBuild = build }, TimeSpan.FromMinutes(15), ct);
        if (!b.Ok) { log.LogWarning("Building mlp-net on {Host} failed: {Msg}", s.HostId, b.Message); return false; }
        var keygen = new ContainerSpec { Image = NetImage };
        keygen.Mounts.Add(new Mount { Source = NetVolume, Target = "/state" });
        keygen.Command.Add("keygen");
        var k = await s.SendCommandAsync(new AgentCommand { ContainerRunOnce = new ContainerRunOnce { Spec = keygen, TimeoutSeconds = 120 } }, TimeSpan.FromMinutes(3), ct);
        var key = k.Output.Trim();
        if (!k.Ok || key.Length != 44) { log.LogWarning("Could not get the mlp-net key on {Host}: {Msg}", s.HostId, k.Message); return false; }
        await using (var scope = scopes.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new SetHostWgKeyCommand(s.HostId, key), ct);
        var spoke = new ContainerSpec { Name = NetName, Image = NetImage, Restart = "unless-stopped" };
        spoke.Labels["mlp.role"] = "net";
        spoke.CapAdd.Add("NET_ADMIN");
        spoke.Mounts.Add(new Mount { Source = NetVolume, Target = "/state" });
        spoke.Env["WG_ADDR"] = overlayIp.Contains('/') ? overlayIp : overlayIp + "/16";
        spoke.Env["PEER_PUB"] = hubKey;
        spoke.Env["PEER_ENDPOINT"] = cfg["WireGuard:Endpoint"] ?? "";
        spoke.Env["PEER_ALLOWED"] = "10.88.0.0/16";
        if (cfg["WireGuard:Mtu"] is { Length: > 0 } mtu) spoke.Env["MTU"] = mtu;
        spoke.Command.Add("spoke");
        var r = await s.SendCommandAsync(new AgentCommand { ContainerApply = new ContainerApply { Spec = spoke, KeepIfSame = true } }, TimeSpan.FromMinutes(3), ct);
        if (!r.Ok) { log.LogWarning("mlp-net on {Host} failed: {Msg}", s.HostId, r.Message); return false; }
        if (!r.Kept)
        {
            await s.SendCommandAsync(new AgentCommand { ContainerRemove = new ContainerRemove { Name = RayNodeName(s.HostId), Force = true } }, TimeSpan.FromMinutes(2), ct);
            log.LogInformation("mlp-net on {Host}: {Ip} → hub {Endpoint}", s.HostId, overlayIp, cfg["WireGuard:Endpoint"]);
        }
        var check = new ContainerSpec { Image = NetImage, Network = "container:" + NetName };
        check.CapAdd.Add("NET_ADMIN");
        check.Command.Add("check");
        var c = await s.SendCommandAsync(new AgentCommand { ContainerRunOnce = new ContainerRunOnce { Spec = check, TimeoutSeconds = 60 } }, TimeSpan.FromMinutes(2), ct);
        var ok = c.Ok && c.Output.Trim().EndsWith("ok", StringComparison.Ordinal);
        await using (var scope = scopes.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new SetHostVpnCommand(s.HostId, ok ? "connected" : "failed",
                ok ? $"{overlayIp} ↔ hub {cfg["WireGuard:Endpoint"]}" : (string.IsNullOrWhiteSpace(c.Output) ? c.Message : c.Output.Trim())), ct);
        if (!ok) log.LogWarning("mlp-net on {Host}: has not joined the VPN: {Msg}", s.HostId, c.Output.Trim());
        return ok;
    }

    /// <summary>Thin Ray worker (20/04 §5, D-12, D-32): CPU = min(logical - 1, limit), resource host:<id> , labels
    /// mlp.io/*, GPUs by UUID.</summary>
    public static ContainerSpec RayNode(AgentSession s, HostInfo? info, IConfiguration cfg, bool overlay, string overlayIp)
    {
        var cap = int.TryParse(cfg["Ray:MaxCpusPerNode"], out var c) ? c : int.MaxValue;
        var cpus = Math.Max(1, Math.Min(cap, (info?.LogicalCpus ?? 2) - 1));
        var spec = new ContainerSpec
        {
            Name = RayNodeName(s.HostId), Image = cfg["Images:Node"] ?? "rayproject/ray:2.60.0-py312-cpu", Restart = "unless-stopped",
            Network = overlay ? "container:" + NetName : cfg["Ray:NodeNetwork"] ?? "", ShmBytes = 2L * 1024 * 1024 * 1024,
        };
        spec.Labels["mlp.role"] = "ray-node";
        spec.Labels["mlp.host"] = s.HostId.ToString();
        var gpus = info?.Gpus.Select(g => g.Uuid).ToList() ?? [];
        if (gpus.Count > 0) { spec.Gpus.Add("all"); spec.Env["CUDA_VISIBLE_DEVICES"] = string.Join(',', gpus); }
        var resources = new Dictionary<string, double> { [$"host:{s.HostId}"] = 1, [$"ws:{s.WorkspaceId}"] = 1 };
        var labels = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["mlp.io/host-id"] = s.HostId.ToString() };
        if (info is not null)
        {
            labels["mlp.io/os"] = info.OsFamily.ToString().ToLowerInvariant();
            if (info.Gpus.Count > 0) labels["mlp.io/gpu"] = info.Gpus[0].Name.Replace(' ', '-');
        }
        spec.Command.AddRange(["ray", "start", "--block", "--address=" + (cfg["Ray:HeadAddress"] ?? "10.88.0.1:6379"),
            "--num-cpus=" + cpus.ToString(CultureInfo.InvariantCulture), "--num-gpus=" + gpus.Count.ToString(CultureInfo.InvariantCulture),
            "--resources={" + string.Join(',', resources.OrderBy(x => x.Key, StringComparer.Ordinal).Select(kv => $"\"{kv.Key}\":{kv.Value.ToString(CultureInfo.InvariantCulture)}")) + "}",
            "--labels=" + string.Join(',', labels.Select(kv => $"{kv.Key}={kv.Value}"))]);
        if (overlay) spec.Command.Add("--node-ip-address=" + overlayIp.Split('/')[0]);
        return spec;
    }
}
