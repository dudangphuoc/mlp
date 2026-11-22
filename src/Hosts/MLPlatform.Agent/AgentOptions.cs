using System.Text.Json;
using System.Text.Json.Serialization;

namespace MLPlatform.Agent;

/// <summary>Agent configuration + credentials (a file in the agent data folder, mode 600; 20/04 §2).</summary>
public sealed class AgentOptions
{
    public string ControlPlane { get; set; } = "";
    public bool UseTls { get; set; } = true;
    /// <summary>SHA-256 (hex) of the control plane TLS certificate; pinned when using a self-signed certificate.</summary>
    public string? CertPin { get; set; }
    /// <summary>Service manager running the agent (systemd | launchd | schtasks | none); written by the install script,
    /// used by OTA to restart.</summary>
    public string ServiceManager { get; set; } = "none";
    public string DataPath { get; set; } = "";
    public string ModelsPath { get; set; } = "";
    public string? EnrollToken { get; set; }
    public string? Credential { get; set; }
    public string? HostId { get; set; }
    public string? OverlayIp { get; set; }
    /// <summary>SHA-256 of the enrollment token already used; a service restart with the same --token does not enroll
    /// again.</summary>
    public string? UsedEnrollTokenHash { get; set; }
    /// <summary>Install instance suffix ("" = default install, "dev" = second agent on the same machine).</summary>
    public string Instance { get; set; } = "";
    /// <summary>normal | yield; yield = accept GPU work only while no other agent's GPU container runs here.</summary>
    public string GpuPriority { get; set; } = "normal";
    /// <summary>Extra folders the file browser may open besides the data, models and state folders.</summary>
    public List<string> FsRoots { get; set; } = [];

    [JsonIgnore]
    public bool YieldsGpu => GpuPriority == "yield";

    public static string DefaultStateDir(string instance = "")
    {
        var suffix = instance.Length > 0 ? "-" + instance : "";
        return OperatingSystem.IsWindows() ? @"C:\ProgramData\MLPlatform" + suffix
            : OperatingSystem.IsMacOS() ? "/Library/Application Support/MLPlatform" + suffix
            : "/var/lib/mlplatform-agent" + suffix;
    }

    public static AgentOptions Load(string path) =>
        File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllText(path), AgentJson.Default.AgentOptions) ?? new() : new();

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, AgentJson.Default.AgentOptions));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(tmp, path, overwrite: true);
    }
}
