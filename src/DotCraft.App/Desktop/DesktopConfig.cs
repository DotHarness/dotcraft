using DotCraft.Configuration;

namespace DotCraft.Desktop;

[ConfigSection("Desktop", DisplayName = "Desktop", Order = 175)]
public sealed class DesktopConfig
{
    [ConfigField(Hint = "Extra apps Desktop can open files and folders in, keyed by id.", Reload = ReloadBehavior.Hot, HasReload = true)]
    public Dictionary<string, DesktopCustomFileHandler> CustomFileHandlers { get; set; } = new(StringComparer.Ordinal);
}

public sealed class DesktopCustomFileHandler
{
    public string Label { get; set; } = string.Empty;

    public string? Icon { get; set; }

    public string Command { get; set; } = string.Empty;

    public List<string> Args { get; set; } = [];

    public DesktopFileHandlerInput Input { get; set; } = DesktopFileHandlerInput.Path;
}

public enum DesktopFileHandlerInput
{
    Path,
    JsonArgument,
    JsonStdin
}
