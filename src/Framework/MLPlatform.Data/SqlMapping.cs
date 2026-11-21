using System.Text;

namespace MLPlatform.Data;

/// <summary>Helpers for generated code: enum ↔ snake_case text (the DB stores `in_progress`, C# `InProgress`).</summary>
public static class SqlMapping
{
    public static string EnumToDb<TEnum>(TEnum value) where TEnum : struct, Enum
    {
        var s = value.ToString();
        var sb = new StringBuilder(s.Length + 4);
        for (var i = 0; i < s.Length; i++)
        {
            var ch = s[i];
            if (char.IsUpper(ch))
            {
                if (i > 0) sb.Append('_');
                sb.Append(char.ToLowerInvariant(ch));
            }
            else sb.Append(ch);
        }
        return sb.ToString();
    }

    public static TEnum EnumFromDb<TEnum>(string value) where TEnum : struct, Enum =>
        Enum.Parse<TEnum>(value.Replace("_", ""), ignoreCase: true);

    public static object DbValue(object? value) => value ?? DBNull.Value;
}
