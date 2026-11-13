using DotCraft.Tools;

[assembly: GeneratedToolCatalog(typeof(DotCraft.CSharp.CSharpToolCatalog))]

namespace DotCraft.CSharp;

internal static class CSharpToolCatalog
{
    public static IReadOnlyList<GeneratedToolDescriptor> Descriptors { get; } =
    [
        NativeToolCatalog.Entry(
            new ToolName(CSharpToolSource.Namespace, "Inspect"),
            CSharpToolMethods.InspectDescription),
        NativeToolCatalog.Entry(
            new ToolName(CSharpToolSource.Namespace, "Build"),
            CSharpToolMethods.BuildDescription)
    ];
}
