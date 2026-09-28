using System.Text.Json.Nodes;
using DotCraft.Protocol.AppServer;

namespace DotCraft.SessionImport;

public sealed record SetupImportPaths(string Workspace, string Data, string UserData, string Home,
    IReadOnlyDictionary<string, string> SourceRoots)
{
    public string Root(string scope) => scope == "user" ? UserData : Data;
}

internal sealed record SetupImportItem(ImportCandidate Candidate, string? Text = null,
    JsonObject? Config = null, string? Directory = null, string? Scripts = null,
    ExternalPluginSource? Plugin = null, string? DirectoryHash = null, string? ScriptsHash = null);

public static class ImportCategories
{
    public static readonly string[] Setup = ["skills", "instructions", "commands", "hooks", "mcp", "plugins"];
    public static bool Includes(ImportSelection selection, string category, string scope) =>
        selection.All || (category == "sessions" ? selection.Sessions
            : (scope == "user" ? selection.User : selection.Workspace).Contains(category, StringComparer.Ordinal));

    public static void Validate(ImportSelection selection)
    {
        if (selection.User.Concat(selection.Workspace).Any(c => !Setup.Contains(c, StringComparer.Ordinal))
            || selection.Workspace.Contains("plugins"))
            throw new ArgumentException("Unknown or unsupported import category selection.");
    }
}
