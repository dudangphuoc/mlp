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

/// <summary>What an inspection is cached by: the image, the variant, the parameters that change the result, and the
/// class count.</summary>
public sealed record InspectionKey(Guid WorkspaceId, string EnvTag, string Variant, string ParamsSha256, int NumClasses);
