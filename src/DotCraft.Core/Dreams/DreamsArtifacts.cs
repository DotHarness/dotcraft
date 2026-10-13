namespace DotCraft.Dreams;

/// <summary>The Dreams directory of a workspace: run records with their input, baseline, and candidate store.</summary>
public sealed class DreamsArtifacts(string dataPath)
{
    public string DreamsDirectoryPath { get; } = Path.Combine(dataPath, "dreams");

    public string RunsDirectoryPath => Path.Combine(DreamsDirectoryPath, "runs");

    public string RunDirectory(string runId) => Path.Combine(RunsDirectoryPath, NormalizeRunId(runId));

    public string InputDirectory(string runId) => Path.Combine(RunDirectory(runId), "input");

    public string ManifestPath(string runId) => Path.Combine(InputDirectory(runId), "MANIFEST.md");

    public string BaselineDirectory(string runId) => Path.Combine(RunDirectory(runId), "before");

    public string CandidateDirectory(string runId) => Path.Combine(RunDirectory(runId), "after");

    public void ClearAll()
    {
        if (Directory.Exists(DreamsDirectoryPath)
            && (File.GetAttributes(DreamsDirectoryPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException($"Refusing to clear reparse-point Dreams directory: {DreamsDirectoryPath}");
        }

        Directory.CreateDirectory(DreamsDirectoryPath);
        foreach (var entry in Directory.EnumerateFileSystemEntries(DreamsDirectoryPath).ToArray())
            DeleteEntry(entry);
    }

    internal static void DeleteEntry(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.Directory) != 0)
            Directory.Delete(path, recursive: (attributes & FileAttributes.ReparsePoint) == 0);
        else
            File.Delete(path);
    }

    internal static string NormalizeRunId(string runId)
    {
        if (string.IsNullOrWhiteSpace(runId))
            throw new ArgumentException("Dream run id is required.", nameof(runId));
        var normalized = runId.Trim();
        if (normalized.Any(static ch => !char.IsAsciiLetterOrDigit(ch) && ch is not ('-' or '_')))
            throw new ArgumentException($"Dream run id contains invalid characters: {runId}", nameof(runId));
        return normalized;
    }
}
