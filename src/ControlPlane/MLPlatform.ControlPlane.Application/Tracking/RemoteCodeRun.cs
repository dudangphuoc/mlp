namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>Where and how to run uploaded code: machine name (or "auto"), GPUs, base image, snapshot (or, with a base
/// commit, only the changed files), working folder, command, forwarded environment, requirements.txt content.</summary>
public sealed record RemoteCodeRun(string Machine, int Gpus, string Image, string SnapshotSha256, string Workdir,
    IReadOnlyList<string> Command, IReadOnlyDictionary<string, string> Env, string Requirements,
    string GitRemote = "", string BaseCommit = "", IReadOnlyList<string>? Deleted = null, IReadOnlyDictionary<string, string>? Data = null, RemoteDebug? Debug = null);
