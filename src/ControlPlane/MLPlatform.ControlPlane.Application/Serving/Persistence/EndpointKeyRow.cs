using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Serving;

/// <summary>An endpoint key as listed: prefix only, never the key.</summary>
public sealed record EndpointKeyRow(Guid Id, string Name, string Prefix, DateTimeOffset CreatedAt, DateTimeOffset? RevokedAt);
