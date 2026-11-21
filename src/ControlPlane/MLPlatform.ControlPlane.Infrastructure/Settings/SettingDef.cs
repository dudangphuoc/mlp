using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Platform;
using MLPlatform.Cqrs;

namespace MLPlatform.ControlPlane.Infrastructure;

/// <summary>Definition of one dynamic settings group.</summary>
public sealed record SettingDef(string Key, string Title, string Description, string TestHint, SettingFieldDef[] Fields);
