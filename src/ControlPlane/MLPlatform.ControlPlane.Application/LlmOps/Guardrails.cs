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

/// <summary>Gateway guardrails (M15 §4): PII (email, VN phone number, CCCD/CMND national ID, card) → off / redact /
/// block; prompt injection (English + Vietnamese patterns) → block; Admin-defined banned terms → block.</summary>
public static partial class Guardrails
{
    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")] private static partial Regex Email();
    [GeneratedRegex(@"(?<!\d)(?:\+?84|0)(?:[\s.-]?\d){9,10}(?!\d)")] private static partial Regex Phone();
    [GeneratedRegex(@"(?<!\d)\d{12}(?!\d)|(?<!\d)\d{9}(?!\d)")] private static partial Regex NationalId();
    [GeneratedRegex(@"(?<!\d)(?:\d[ -]?){13,16}(?!\d)")] private static partial Regex Card();
    [GeneratedRegex(@"ignore (all |any )?(the )?(previous|above|prior) (instructions|prompts?)|disregard (the |all )?(previous|above) |reveal (your |the )?(system prompt|instructions)|you are now (?:in )?(developer|dan) mode|bỏ qua (mọi |tất cả |các )?(hướng dẫn|chỉ dẫn|yêu cầu) (trước|ở trên)|tiết lộ (lời nhắn|prompt) hệ thống",
        RegexOptions.IgnoreCase)]
    private static partial Regex Injection();

    public sealed record Result(string Text, bool Blocked, string? Reason, List<string> Flags);

    public static Result Apply(string text, LlmSettingsRow? s, bool output)
    {
        var flags = new List<string>();
        if (s is null) return new Result(text, false, null, flags);
        var t = text;
        if (!output && s.BlockInjection && Injection().IsMatch(t)) { flags.Add("injection"); return new Result(t, true, "Prompt injection detected", flags); }
        foreach (var term in JsonNode.Parse(s.BlockedTerms)?.AsArray().Select(x => x?.ToString() ?? "").Where(x => x.Length > 0) ?? [])
            if (t.Contains(term, StringComparison.OrdinalIgnoreCase)) { flags.Add("term"); return new Result(t, true, $"Contains a banned term (\"{term}\")", flags); }
        if (s.PiiMode is "redact" or "block")
        {
            foreach (var (name, rx, mask) in new[] { ("email", Email(), "[EMAIL]"), ("card", Card(), "[CARD]"), ("phone", Phone(), "[PHONE]"), ("id", NationalId(), "[ID NUMBER]") })
            {
                if (!rx.IsMatch(t)) continue;
                flags.Add("pii:" + name);
                if (s.PiiMode == "block") return new Result(t, true, $"Contains personal data ({name})", flags);
                t = rx.Replace(t, mask);
            }
        }
        return new Result(t, false, null, flags);
    }
}
