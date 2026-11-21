namespace MLPlatform.Cqrs;

/// <summary>Internal command sent by background work (e.g. the sweep orchestrator); not audited even if the scope has a user; child commands are still audited normally.</summary>
public interface IInternalCommand;
