[assembly: DotCraft.Tools.GeneratedToolCatalog(typeof(DotCraft.Tools.NativeToolCatalog))]

namespace DotCraft.Tools;

internal static class NativeToolCatalog
{
    public static IReadOnlyList<GeneratedToolDescriptor> Descriptors { get; } =
    [
        Entry(
            new ToolName(ImageGenerationToolSource.ToolNamespace, ImageGenerationToolSource.ToolName),
            "Generate images from descriptions and edit existing images. Available when image generation is enabled and the image provider supports image generation."),
        Entry(
            new ToolName(WebSearchToolSource.ToolNamespace, WebSearchToolSource.ToolName),
            "Search the web and open pages through ChatGPT. Available when the conversation provider signs in with ChatGPT.")
    ];

    internal static GeneratedToolDescriptor Entry(ToolName name, string description) =>
        new(
            ProviderToolProjector.Project([name])[name],
            description,
            MaxResultChars: null,
            StreamArgumentsEnabled: false,
            CatalogVisible: true);
}
