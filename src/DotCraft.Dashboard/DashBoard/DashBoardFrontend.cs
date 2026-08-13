namespace DotCraft.DashBoard;

public static class DashBoardFrontend
{
    private static string? _cachedHtml;
    private static string? _cachedLoginHtml;

    public static string GetHtml()
    {
        if (_cachedHtml != null) return _cachedHtml;

        _cachedHtml = LoadEmbeddedResource("DotCraft.Resources.DashBoard.html");
        return _cachedHtml;
    }

    public static string GetLoginHtml()
    {
        if (_cachedLoginHtml != null) return _cachedLoginHtml;

        _cachedLoginHtml = LoadEmbeddedResource("DotCraft.Resources.DashBoardLogin.html");
        return _cachedLoginHtml;
    }

    private static string LoadEmbeddedResource(string name)
    {
        var assembly = typeof(DashBoardFrontend).Assembly;
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded resource '{name}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

