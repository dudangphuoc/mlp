using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MLPlatform.Refactor;

public static class TypeInventory
{
    public static int Print(string file)
    {
        var root = CSharpSyntaxTree.ParseText(File.ReadAllText(file), SourceFiles.ParseOptions).GetCompilationUnitRoot();
        foreach (var type in SourceFiles.TopLevelTypes(root))
        {
            var span = type.GetLocation().GetLineSpan();
            Console.WriteLine($"{SourceFiles.NameOf(type)}\t{span.EndLinePosition.Line - span.StartLinePosition.Line + 1}");
        }
        return 0;
    }
}
