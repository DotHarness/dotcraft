namespace DotCraft.Oratorio.Integrations;

internal static class OratorioPrompts
{
    /// <summary>Reads a prompt file embedded as <c>DotCraft.Oratorio.Prompts.{name}.md</c>, without the file's final line break.</summary>
    internal static string Read(string name)
    {
        using var stream = typeof(OratorioPrompts).Assembly.GetManifestResourceStream($"DotCraft.Oratorio.Prompts.{name}.md")
            ?? throw new InvalidOperationException($"Embedded prompt '{name}' was not found.");
        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd();
        return text.EndsWith("\r\n", StringComparison.Ordinal) ? text[..^2]
            : text.EndsWith('\n') ? text[..^1]
            : text;
    }
}
