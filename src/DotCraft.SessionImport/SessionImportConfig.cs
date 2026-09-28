using DotCraft.Configuration;

namespace DotCraft.SessionImport;

/// <summary>Synchronization choices are owned by the user configuration file, not by this merged section.</summary>
[ConfigSection("AgentImport", DisplayName = "Agent import", Order = 46)]
public sealed class SessionImportConfig
{
    public bool Enabled { get; set; } = true;

    public bool SyncEnabled { get; set; }

    public List<string> Sources { get; set; } = [.. SessionImportSources.All];

    public DotCraft.Protocol.AppServer.ImportSelection Selection { get; set; } = new();

    public TimeSpan SyncInterval { get; set; } = TimeSpan.FromHours(12);

    public int MaxSessionAgeDays { get; set; } = 30;

    public int MaxSessionsPerSource { get; set; } = 50;
}
