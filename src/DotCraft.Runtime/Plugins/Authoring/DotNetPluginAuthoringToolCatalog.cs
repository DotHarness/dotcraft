using DotCraft.Tools;

[assembly: GeneratedToolCatalog(typeof(DotCraft.Runtime.DotNetPluginAuthoringToolCatalog))]

namespace DotCraft.Runtime;

internal static class DotNetPluginAuthoringToolCatalog
{
    public static IReadOnlyList<GeneratedToolDescriptor> Descriptors { get; } =
    [
        NativeToolCatalog.Entry(
            new ToolName(DotNetPluginAuthoringToolSource.Namespace, "Inspect"),
            DotNetPluginAuthoringToolMethods.InspectDescription),
        NativeToolCatalog.Entry(
            new ToolName(DotNetPluginAuthoringToolSource.Namespace, "Build"),
            DotNetPluginAuthoringToolMethods.BuildDescription)
    ];
}
