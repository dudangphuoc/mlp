using Microsoft.Extensions.Logging;
using MLPlatform.AgentProtocol;
using MLPlatform.Contracts.V1;

namespace MLPlatform.Agent;

/// <summary>Agent configuration + state on disk.</summary>
public sealed class AgentState(string path)
{
    public AgentOptions Options { get; } = AgentOptions.Load(path);
    public bool Disconnected { get; set; }
    public void Save() => Options.Save(path);
    public void Forget() { if (File.Exists(path)) File.Delete(path); }
    public string Dir => Path.GetDirectoryName(Path.GetFullPath(path))!;
}
