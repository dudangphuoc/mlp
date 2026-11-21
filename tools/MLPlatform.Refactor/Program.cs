using MLPlatform.Refactor;

return args switch
{
    ["split", var mapFile] => TypeSplitter.Run(Path.GetFullPath(mapFile)),
    ["inventory", var file] => TypeInventory.Print(Path.GetFullPath(file)),
    ["clean-comments", .. var roots] => CommentCleaner.Run(roots.SelectMany(r => Directory.EnumerateFiles(r, "*.cs", SearchOption.AllDirectories)).Where(f => !f.Contains("/obj/") && !f.Contains("/bin/"))),
    _ => Usage(),
};

static int Usage()
{
    Console.WriteLine("mlp-refactor split <map.txt>   lines: <source.cs> <TypeName> <target.cs> (paths relative to the map file's directory or absolute)");
    Console.WriteLine("mlp-refactor inventory <file.cs>   prints top-level types with line counts");
    return 2;
}
