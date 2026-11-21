using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Recipes;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Catalog;

/// <summary>What the inspection container reported: the overlay, the merged config and the model blocks, or why there is none.</summary>
public sealed record InspectionOutput(bool Ok, string Error, string OverlayYaml, string ConfigJson, string BlocksJson, string SourcesJson = "{}");
