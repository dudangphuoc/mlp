using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Google.Protobuf.Reflection;

namespace MLPlatform.DocGen;

/// <summary>OpenAPI 3.1 document: REST routes as real paths, gRPC methods described with x-grpc (called over gRPC-Web,
/// not JSON).</summary>
public static class OpenApiWriter
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static string Write(ApiModel model)
    {
        var schemas = new JsonObject();
        foreach (var file in model.ProtoFiles)
        {
            foreach (var message in file.MessageTypes) AddMessage(schemas, message);
            foreach (var e in file.EnumTypes) schemas[e.Name] = EnumSchema(e);
        }
        var paths = new JsonObject();
        foreach (var route in model.RestRoutes) AddRest(paths, route);
        foreach (var service in model.Services)
            foreach (var method in service.Methods) paths[$"/{service.FullName}/{method.Name}"] = new JsonObject { ["post"] = GrpcOperation(model, method) };
        var doc = new JsonObject
        {
            ["openapi"] = "3.1.0",
            ["info"] = new JsonObject { ["title"] = "MLPlatform API", ["version"] = "v1", ["x-proto-hash"] = ProtoHash(model) },
            ["paths"] = paths,
            ["components"] = new JsonObject { ["schemas"] = schemas, ["securitySchemes"] = SecuritySchemes() },
        };
        return doc.ToJsonString(Indented) + "\n";
    }

    private static JsonObject GrpcOperation(ApiModel model, MethodDescriptor method)
    {
        var requests = model.RequestsFor(method).ToList();
        var primary = OperationTraits.Primary(requests);
        var op = new JsonObject
        {
            ["operationId"] = $"{method.Service.Name}.{method.Name}",
            ["tags"] = new JsonArray(method.Service.Name),
            ["requestBody"] = Body(method.InputType),
            ["responses"] = new JsonObject { ["200"] = Body(method.OutputType) },
            ["x-grpc"] = new JsonObject { ["service"] = method.Service.FullName, ["method"] = method.Name, ["transport"] = "grpc-web" },
            ["x-kind"] = requests.Any(r => r.IsCommand) ? "command" : "query",
            ["x-effect"] = OperationTraits.Effect(method, requests),
            ["x-audience"] = OperationTraits.Audience(method, requests),
            ["x-access"] = primary?.Access ?? "",
            ["x-streaming"] = method.IsServerStreaming,
            ["x-module"] = primary?.Module ?? "",
        };
        if (primary?.Permission is { } permission)
        {
            op["x-permission"] = permission;
            op["x-roles"] = new JsonArray([.. primary.Roles.Select(r => (JsonNode)r)]);
        }
        if (method.InputType.FindFieldByName("workspace_id") is not null) op["x-scope-field"] = "workspace_id";
        var refs = IdReferences(method);
        if (refs.Count > 0) op["x-ref"] = refs;
        return op;
    }

    private static JsonObject IdReferences(MethodDescriptor method)
    {
        var refs = new JsonObject();
        foreach (var field in method.InputType.Fields.InFieldNumberOrder())
        {
            if (field.FieldType != FieldType.String || field.IsRepeated || !field.Name.EndsWith("_id", StringComparison.Ordinal) || field.Name == "workspace_id") continue;
            var entity = Pascal(field.Name[..^3]);
            var list = method.Service.Methods.FirstOrDefault(m => m.Name == $"List{entity}s" || m.Name == $"List{entity}es");
            refs[field.Name] = new JsonObject { ["entity"] = entity, ["list"] = list is null ? null : $"{method.Service.Name}.{list.Name}" };
        }
        return refs;
    }

    private static void AddRest(JsonObject paths, RestRoute route)
    {
        var path = route.Path.Replace(":guid", "").Replace("**", "");
        var item = paths[path] as JsonObject ?? new JsonObject();
        var verb = route.Verb == "ANY" ? "get" : route.Verb.ToLowerInvariant();
        item[verb] = new JsonObject
        {
            ["operationId"] = $"{route.Area}.{route.Verb}{path.Replace('/', '_').Replace("{", "").Replace("}", "")}",
            ["tags"] = new JsonArray("rest:" + route.Area),
            ["responses"] = new JsonObject { ["200"] = new JsonObject { ["description"] = "OK" } },
            ["security"] = route.IsPublic ? new JsonArray() : new JsonArray(new JsonObject { ["session"] = new JsonArray() }),
            ["x-public"] = route.IsPublic,
        };
        if (route.Area == "Inference") item[verb]!["security"] = new JsonArray(new JsonObject { ["deploymentKey"] = new JsonArray() });
        paths[path] = item;
    }

    private static JsonObject Body(MessageDescriptor type) => new()
    {
        ["description"] = type.Name,
        ["content"] = new JsonObject { ["application/json"] = new JsonObject { ["schema"] = new JsonObject { ["$ref"] = $"#/components/schemas/{type.Name}" } } },
    };

    private static void AddMessage(JsonObject schemas, MessageDescriptor message)
    {
        if (schemas.ContainsKey(message.Name)) return;
        var properties = new JsonObject();
        foreach (var field in message.Fields.InFieldNumberOrder()) properties[field.JsonName] = FieldSchema(field);
        schemas[message.Name] = new JsonObject { ["type"] = "object", ["properties"] = properties, ["x-proto"] = message.FullName };
        foreach (var nested in message.NestedTypes.Where(n => !n.GetOptions()?.MapEntry ?? true)) AddMessage(schemas, nested);
        foreach (var e in message.EnumTypes) schemas[e.Name] = EnumSchema(e);
    }

    private static JsonNode FieldSchema(FieldDescriptor field)
    {
        if (field.IsMap) return new JsonObject { ["type"] = "object", ["additionalProperties"] = ScalarSchema(field.MessageType.FindFieldByNumber(2)) };
        var item = ScalarSchema(field);
        return field.IsRepeated ? new JsonObject { ["type"] = "array", ["items"] = item } : item;
    }

    private static JsonNode ScalarSchema(FieldDescriptor field) => field.FieldType switch
    {
        FieldType.Message when field.MessageType.FullName == "google.protobuf.Timestamp" => new JsonObject { ["type"] = "string", ["format"] = "date-time" },
        FieldType.Message when field.MessageType.FullName.StartsWith("google.protobuf.", StringComparison.Ordinal) => new JsonObject(),
        FieldType.Message => new JsonObject { ["$ref"] = $"#/components/schemas/{field.MessageType.Name}" },
        FieldType.Enum => new JsonObject { ["$ref"] = $"#/components/schemas/{field.EnumType.Name}" },
        FieldType.String => new JsonObject { ["type"] = "string" },
        FieldType.Bytes => new JsonObject { ["type"] = "string", ["contentEncoding"] = "base64" },
        FieldType.Bool => new JsonObject { ["type"] = "boolean" },
        FieldType.Double or FieldType.Float => new JsonObject { ["type"] = "number" },
        FieldType.Int64 or FieldType.UInt64 or FieldType.SInt64 or FieldType.Fixed64 or FieldType.SFixed64 => new JsonObject { ["type"] = "string", ["format"] = "int64" },
        _ => new JsonObject { ["type"] = "integer" },
    };

    private static JsonObject EnumSchema(EnumDescriptor e) =>
        new() { ["type"] = "string", ["enum"] = new JsonArray([.. e.Values.Select(v => (JsonNode)v.Name)]) };

    private static JsonObject SecuritySchemes() => new()
    {
        ["session"] = new JsonObject { ["type"] = "apiKey", ["in"] = "cookie", ["name"] = "mlp_session" },
        ["userKey"] = new JsonObject { ["type"] = "http", ["scheme"] = "bearer", ["bearerFormat"] = "mlp_" },
        ["deploymentKey"] = new JsonObject { ["type"] = "http", ["scheme"] = "bearer", ["bearerFormat"] = "mlpd_" },
    };

    private static string ProtoHash(ApiModel model)
    {
        var bytes = model.ProtoFiles.SelectMany(f => f.SerializedData.ToByteArray()).ToArray();
        return Convert.ToHexStringLower(SHA256.HashData(bytes))[..16];
    }

    private static string Pascal(string snake) =>
        string.Concat(snake.Split('_').Where(p => p.Length > 0).Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
}
