using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCraft.TraceViewer;

internal enum AppearancePreference
{
    System,
    Light,
    Dark
}

internal sealed record TraceViewerSettings(
    string? RecentWorkspacePath = null,
    AppearancePreference Appearance = AppearancePreference.System);

internal sealed class TraceViewerSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter<AppearancePreference>() }
    };
    private readonly string _settingsPath;

    public TraceViewerSettingsStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DotCraft",
            "TraceViewer",
            "settings.json"))
    {
    }

    internal TraceViewerSettingsStore(string settingsPath)
    {
        _settingsPath = settingsPath;
    }

    public TraceViewerSettings Load()
    {
        try
        {
            return File.Exists(_settingsPath)
                ? JsonSerializer.Deserialize<TraceViewerSettings>(File.ReadAllText(_settingsPath), JsonOptions)
                    ?? new TraceViewerSettings()
                : new TraceViewerSettings();
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return new TraceViewerSettings();
        }
    }

    public void SaveRecentWorkspace(string workspacePath) =>
        Save(Load() with { RecentWorkspacePath = workspacePath });

    public void SaveAppearance(AppearancePreference appearance) =>
        Save(Load() with { Appearance = appearance });

    private void Save(TraceViewerSettings settings)
    {
        var directory = Path.GetDirectoryName(_settingsPath)
            ?? throw new InvalidOperationException("DotCraft Trace Viewer settings path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = _settingsPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporaryPath, _settingsPath, overwrite: true);
    }
}
