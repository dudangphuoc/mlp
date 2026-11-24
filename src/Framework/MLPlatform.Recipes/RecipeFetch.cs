namespace MLPlatform.Recipes;

/// <summary>A file of the build context that is downloaded instead of sent: a source archive (git: placeholder) or an
/// upstream file checked by sha256.</summary>
public sealed record RecipeFetch(string Path, string Url, string Sha256);
