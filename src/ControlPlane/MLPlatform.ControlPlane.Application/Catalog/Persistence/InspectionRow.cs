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

/// <summary>
/// A cached CPU-only inspection of a recipe; <c>Blocks</c> is a JSON array of <c>{key, params, trainable}</c>, <c>Sources</c> a JSON object
/// pane → <c>[{name, file, line, code}]</c> (the upstream code the merged config selects).
/// </summary>
public sealed record InspectionRow(Guid Id, Guid WorkspaceId, string RecipeName, string EnvTag, string Variant, string ParamsSha256,
    int NumClasses, string State, string Error, string OverlayYaml, string ConfigJson, string Blocks, DateTimeOffset CreatedAt, string Sources);
