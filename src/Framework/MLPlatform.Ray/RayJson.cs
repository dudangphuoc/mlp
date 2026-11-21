using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace MLPlatform.Ray;

[JsonSerializable(typeof(SubmitJobRequest))]
[JsonSerializable(typeof(JobInfo))]
internal sealed partial class RayJson : JsonSerializerContext;
