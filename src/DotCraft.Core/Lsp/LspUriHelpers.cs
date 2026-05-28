namespace DotCraft.Lsp;

internal static class LspUriHelpers
{
    public static string ToFileUri(string filePath)
    {
        return new Uri(Path.GetFullPath(filePath)).AbsoluteUri;
    }
}
