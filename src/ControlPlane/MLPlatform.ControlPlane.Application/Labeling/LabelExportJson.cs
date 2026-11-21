using System.Globalization;
using System.Text;
using System.Text.Json;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Labeling;

/// <summary>JSON for the class-name list in data.yaml (source generated, AOT).</summary>
[System.Text.Json.Serialization.JsonSerializable(typeof(List<string>))]
internal sealed partial class LabelExportJson : System.Text.Json.Serialization.JsonSerializerContext;
