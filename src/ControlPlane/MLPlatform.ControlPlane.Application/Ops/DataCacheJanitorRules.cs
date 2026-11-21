using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Timeline;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Ops;

/// <summary>Automatic clean-up rules of host dataset caches.</summary>
public static class DataCacheJanitorRules
{
    public static readonly TimeSpan MaxIdle = TimeSpan.FromDays(14);
    public const long MinFreeBytes = 50L << 30;
}
