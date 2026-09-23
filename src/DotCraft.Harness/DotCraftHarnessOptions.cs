using DotCraft.Agents.Remote;

namespace DotCraft.Harness;

public sealed class DotCraftHarnessOptions
{
    public ModelServiceConnection? ModelService { get; set; }
    public ModelServiceCatalog? InitialModelServiceCatalog { get; set; }
    public bool RefreshModelServiceCatalog { get; set; } = true;
    public string WorkspacePath { get; set; } = string.Empty;

    /// <summary>Relative to WorkspacePath and must identify one of its direct children.</summary>
    public string DataPath { get; set; } = ".craft";

    /// <summary>Null disables implicit user-level discovery and persistence.</summary>
    public string? UserDataPath { get; set; }
}
