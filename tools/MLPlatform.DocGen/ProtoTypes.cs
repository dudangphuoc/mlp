using Google.Protobuf.Reflection;

namespace MLPlatform.DocGen;

/// <summary>Readable proto field types for tables (repeated and map shapes spelled out).</summary>
public static class ProtoTypes
{
    public static string Describe(FieldDescriptor field)
    {
        if (field.IsMap)
        {
            var entry = field.MessageType;
            return $"map<{Scalar(entry.FindFieldByNumber(1))}, {Scalar(entry.FindFieldByNumber(2))}>";
        }
        var type = Scalar(field);
        return field.IsRepeated ? $"repeated {type}" : type;
    }

    public static string Scalar(FieldDescriptor field) => field.FieldType switch
    {
        FieldType.Message => field.MessageType.Name,
        FieldType.Enum => field.EnumType.Name,
        var t => t.ToString().ToLowerInvariant(),
    };
}
