using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Serving;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.LlmOps;

public sealed record LlmSettingsRow(Guid DeploymentId, string PiiMode, bool BlockInjection, string BlockedTerms, bool CheckOutput, double PricePer1k, DateTimeOffset UpdatedAt);
