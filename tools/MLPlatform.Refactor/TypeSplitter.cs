using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MLPlatform.Refactor;

/// <summary>Moves top-level types verbatim into new files; each new file gets the source file's usings and namespace.</summary>
public static class TypeSplitter
{
    public static int Run(string mapFile)
    {
        var baseDir = Path.GetDirectoryName(mapFile)!;
        var moves = File.ReadAllLines(mapFile)
            .Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#'))
            .Select(l => l.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
            .Select(p => (Source: Resolve(baseDir, p[0]), Type: p[1], Target: Resolve(baseDir, p[2])))
            .ToList();
        foreach (var group in moves.GroupBy(m => m.Source))
            SplitFile(group.Key, group.Select(m => (m.Type, m.Target)).ToList());
        return 0;
    }

    private static string Resolve(string baseDir, string path) => Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(baseDir, path));

    private static void SplitFile(string source, List<(string Type, string Target)> moves)
    {
        var root = CSharpSyntaxTree.ParseText(File.ReadAllText(source), SourceFiles.ParseOptions).GetCompilationUnitRoot();
        var header = SourceFiles.Header(root);
        var ns = SourceFiles.NamespaceOf(root);
        var types = SourceFiles.TopLevelTypes(root).ToList();
        var outputs = new Dictionary<string, StringBuilder>(StringComparer.Ordinal);
        var removed = new List<MemberDeclarationSyntax>();
        foreach (var (typeName, target) in moves)
        {
            var matches = types.Where(t => SourceFiles.NameOf(t) == typeName).ToList();
            if (matches.Count == 0) throw new InvalidOperationException($"{typeName} not found in {source}");
            if (!outputs.TryGetValue(target, out var sb)) outputs[target] = sb = new StringBuilder();
            foreach (var type in matches)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(type.ToFullString().Trim('\n', '\r')).Append('\n');
                removed.Add(type);
            }
        }
        foreach (var (target, body) in outputs) Write(target, header, ns, body.ToString());
        var remaining = root.RemoveNodes(removed, SyntaxRemoveOptions.KeepNoTrivia)!;
        if (SourceFiles.TopLevelTypes(remaining).Any() || remaining.Members.Any(m => m is GlobalStatementSyntax))
            File.WriteAllText(source, Tidy(remaining.ToFullString()));
        else
            File.Delete(source);
    }

    private static void Write(string target, string header, string? ns, string body)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var text = new StringBuilder(header.TrimEnd()).Append("\n\n");
        if (ns is not null) text.Append($"namespace {ns};\n\n");
        text.Append(body);
        if (File.Exists(target))
        {
            var existing = File.ReadAllText(target).TrimEnd();
            File.WriteAllText(target, existing + "\n\n" + body);
            return;
        }
        File.WriteAllText(target, text.ToString().TrimStart('\n'));
    }

    private static string Tidy(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text, @"\n{3,}", "\n\n").TrimEnd() + "\n";
}
