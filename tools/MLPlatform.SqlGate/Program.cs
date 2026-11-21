// SQL + comment gate (P0-11). Usage: mlp-sqlgate <repo-root> [--update-baseline]
//   MLPSQL001  Sql/**.sql files must start each query with `-- mlp:query Name :one|:many|:exec|:scalar` (PascalCase name, unique within the module)
//   MLPSQL010  a query touching a table with a workspace_id column must mention workspace_id (filter by workspace);
//              exemption with a reason: a `-- mlp:no-workspace <reason>` line in the query (e.g. token lookup before the workspace is known)
//   MLPSQL020  Sql/<Module>/ only touches that module's schema (module boundary, 20/02 §3)
//   MLPMIG001  migrations are db/<schema>/<yyyyMMddHHmm>_<name>.sql, unique timestamps, no DROP TABLE/COLUMN (additive only — 20/05)
//   MLPDOC001  public types in Application/Api/Agent/Shim must have /// <summary> (CommentGate)
// Violations that existed when the gate was enabled are in gates/baseline.txt — only NEW violations turn CI red.
using System.Text.RegularExpressions;

var root = Path.GetFullPath(args.ElementAtOrDefault(0) ?? ".");
var update = args.Contains("--update-baseline");
var found = new SortedSet<string>(StringComparer.Ordinal);
string Rel(string p) => Path.GetRelativePath(root, p).Replace('\\', '/');
bool Skip(string p) => p.Contains("/bin/") || p.Contains("/obj/") || p.Contains("/node_modules/");

// --- tables with workspace_id (from migrations) ---
var wsTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
var createTable = new Regex(@"create\s+table\s+(?:if\s+not\s+exists\s+)?([a-z_]+\.[a-z_0-9]+)\s*\((.*?)\n\);", RegexOptions.IgnoreCase | RegexOptions.Singleline);
var alterAdd = new Regex(@"alter\s+table\s+(?:if\s+exists\s+)?([a-z_]+\.[a-z_0-9]+)\s+add\s+column\s+(?:if\s+not\s+exists\s+)?workspace_id\b", RegexOptions.IgnoreCase);
var migName = new Regex(@"^\d{12}_[a-z0-9_]+\.sql$");
var stamps = new Dictionary<string, string>();
foreach (var f in Directory.EnumerateFiles(Path.Combine(root, "db"), "*.sql", SearchOption.AllDirectories).Order())
{
    var text = File.ReadAllText(f);
    foreach (Match m in createTable.Matches(text))
        if (Regex.IsMatch(m.Groups[2].Value, @"^\s*workspace_id\s", RegexOptions.Multiline | RegexOptions.IgnoreCase)) wsTables.Add(m.Groups[1].Value);
    foreach (Match m in alterAdd.Matches(text)) wsTables.Add(m.Groups[1].Value);
    var name = Path.GetFileName(f);
    if (!migName.IsMatch(name)) found.Add($"MLPMIG001 {Rel(f)}: migration name must be yyyyMMddHHmm_name.sql");
    else if (!stamps.TryAdd(name[..12], Rel(f))) found.Add($"MLPMIG001 {Rel(f)}: timestamp clashes with {stamps[name[..12]]}");
    if (Regex.IsMatch(text, @"\bdrop\s+(table|column)\b", RegexOptions.IgnoreCase)) found.Add($"MLPMIG001 {Rel(f)}: migrations must not DROP TABLE/COLUMN");
}

// --- queries ---
var header = new Regex(@"^--\s*mlp:query\s+(\S+)\s+:(\S+)\s*$", RegexOptions.Multiline);
var tableRef = new Regex(@"\b(?:from|join|into|update|exists\s*\(\s*select\s+1\s+from)\s+([a-z_]+)\.([a-z_0-9]+)", RegexOptions.IgnoreCase);
static string SchemaOf(string module) => module.ToLowerInvariant() switch { "llm" => "llmops", var x => x };
var shared = new HashSet<string> { "platform" };   // shared schema (system tables)
foreach (var f in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.sql", SearchOption.AllDirectories).Where(p => !Skip(p) && p.Contains("/Sql/")).Order())
{
    var rel = Rel(f);
    var module = rel[(rel.IndexOf("/Sql/", StringComparison.Ordinal) + 5)..].Split('/')[0];
    var text = File.ReadAllText(f);
    var heads = header.Matches(text);
    if (heads.Count == 0 || !text.TrimStart().StartsWith("--", StringComparison.Ordinal)) { found.Add($"MLPSQL001 {rel}: missing -- mlp:query header"); continue; }
    for (var i = 0; i < heads.Count; i++)
    {
        var (name, kind) = (heads[i].Groups[1].Value, heads[i].Groups[2].Value);
        if (kind is not ("one" or "many" or "exec" or "scalar" or "copy")) found.Add($"MLPSQL001 {rel}:{name}: invalid kind '{kind}'");
        if (!Regex.IsMatch(name, "^[A-Z][A-Za-z0-9]*$")) found.Add($"MLPSQL001 {rel}:{name}: query name must be PascalCase");
        var end = i + 1 < heads.Count ? heads[i + 1].Index : text.Length;
        var raw = text[(heads[i].Index + heads[i].Length)..end];
        var exempt = Regex.IsMatch(raw, @"--\s*mlp:no-workspace\s+\S");
        var body = Regex.Replace(raw, "--[^\n]*", "");
        var refs = tableRef.Matches(body).Select(m => (Schema: m.Groups[1].Value.ToLowerInvariant(), Table: $"{m.Groups[1].Value}.{m.Groups[2].Value}".ToLowerInvariant())).ToList();
        foreach (var r in refs.Where(r => r.Schema != SchemaOf(module) && !shared.Contains(r.Schema)).Select(r => r.Schema).Distinct())
            found.Add($"MLPSQL020 {rel}:{name}: module {module} touches schema {r}");
        if (!exempt && refs.Any(r => wsTables.Contains(r.Table)) && !body.Contains("workspace_id", StringComparison.OrdinalIgnoreCase))
            found.Add($"MLPSQL010 {rel}:{name}: table has workspace_id but the query does not filter on workspace_id");
    }
}

// --- CommentGate ---
var publicType = new Regex(@"^(\s*)public\s+(?:sealed\s+|static\s+|abstract\s+|partial\s+|readonly\s+)*(class|record|interface|struct|enum)\s+(\w+)", RegexOptions.Multiline);
foreach (var dir in new[] { "src/ControlPlane", "src/Hosts" })
foreach (var f in Directory.EnumerateFiles(Path.Combine(root, dir), "*.cs", SearchOption.AllDirectories).Where(p => !Skip(p)).Order())
{
    var lines = File.ReadAllLines(f);
    for (var i = 0; i < lines.Length; i++)
    {
        var m = publicType.Match(lines[i]);
        if (!m.Success) continue;
        var j = i - 1;
        while (j >= 0 && lines[j].TrimStart().StartsWith('[')) j--;   // skip attributes
        if (j < 0 || !lines[j].TrimStart().StartsWith("///", StringComparison.Ordinal))
            found.Add($"MLPDOC001 {Rel(f)}: {m.Groups[2].Value} {m.Groups[3].Value} missing /// <summary>");
    }
}

var baselinePath = Path.Combine(root, "gates", "baseline.txt");
if (update)
{
    Directory.CreateDirectory(Path.GetDirectoryName(baselinePath)!);
    File.WriteAllLines(baselinePath, ["# Gate violations that existed when the gate was enabled (P0-11) — shrink over time, never add. Generated by: mlp-sqlgate . --update-baseline", .. found]);
    Console.WriteLine($"Wrote baseline with {found.Count} lines");
    return 0;
}
var baseline = File.Exists(baselinePath) ? File.ReadAllLines(baselinePath).Where(l => !l.StartsWith('#')).ToHashSet() : [];
var fresh = found.Where(x => !baseline.Contains(x)).ToList();
var fixedCount = baseline.Count(b => !found.Contains(b));
foreach (var x in fresh) Console.WriteLine("FAIL " + x);
Console.WriteLine($"gate: {found.Count} violations ({fresh.Count} new, {found.Count - fresh.Count} in baseline, {fixedCount} fixed — run --update-baseline to shrink it)");
return fresh.Count == 0 ? 0 : 1;
