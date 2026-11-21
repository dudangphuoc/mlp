using System.Text;
using System.Text.RegularExpressions;
using MLPlatform.CodeGate.Comments;

namespace MLPlatform.CodeGate.Reports;

/// <summary>Size and comment metrics per project, written as markdown tables for the refactor plan.</summary>
public static partial class MetricsReport
{
    public static void Write(RepoFiles repo, string path)
    {
        var files = repo.HandWrittenCSharp().Select(f => (Path: repo.Relative(f), Lines: File.ReadAllLines(f))).ToList();
        var comments = CommentScanner.Scan(repo).ToList();
        var md = new StringBuilder();
        md.AppendLine("| Project | .cs files | Lines | Types | Body comments | Declaration comments |");
        md.AppendLine("|---|---:|---:|---:|---:|---:|");
        foreach (var project in files.GroupBy(f => ProjectOf(f.Path)).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var inProject = comments.Where(c => ProjectOf(c.File) == project.Key).ToList();
            md.AppendLine($"| {project.Key} | {project.Count()} | {project.Sum(f => f.Lines.Length)} | {project.Sum(f => TypeCount(f.Lines))} | "
                + $"{inProject.Count(c => c.Placement == CommentPlacement.InsideBody)} | {inProject.Count(c => c.Placement != CommentPlacement.InsideBody)} |");
        }
        md.AppendLine($"| **Total** | {files.Count} | {files.Sum(f => f.Lines.Length)} | {files.Sum(f => TypeCount(f.Lines))} | "
            + $"{comments.Count(c => c.Placement == CommentPlacement.InsideBody)} | {comments.Count(c => c.Placement != CommentPlacement.InsideBody)} |");
        md.AppendLine();
        md.AppendLine("| Largest files | Lines | Types |");
        md.AppendLine("|---|---:|---:|");
        foreach (var f in files.OrderByDescending(f => f.Lines.Length).Take(20))
            md.AppendLine($"| {f.Path} | {f.Lines.Length} | {TypeCount(f.Lines)} |");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, md.ToString());
    }

    private static string ProjectOf(string path) => string.Join('/', path.Split('/').Take(path.StartsWith("tools/", StringComparison.Ordinal) ? 2 : 3));

    private static int TypeCount(string[] lines) => lines.Count(l => TypeDeclaration().IsMatch(l));

    [GeneratedRegex(@"^\s*(public |internal |private |protected |file )?(sealed |static |abstract |partial |readonly |closed |ref )*(class|record|interface|enum|struct|union)( struct| class)? [A-Z]")]
    private static partial Regex TypeDeclaration();
}
