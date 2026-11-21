namespace MLPlatform.DocGen;

public sealed record ModuleInfo(string Name, string Layer, IReadOnlyList<string> Uses, IReadOnlyList<string> SqlFolders, string Spec, IReadOnlyList<string> Api, bool PublicSql);
