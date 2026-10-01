using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotCraft.Tools;

internal static class ImageGenerationToolText
{
    public static string Description(int maxReferenceImages) =>
        $"""
        The `image_gen.imagegen` tool enables image generation from descriptions and editing of existing images based on specific instructions. Use it when:

        - The user requests an image based on a scene description, such as a diagram, portrait, comic, meme, or any other visual.
        - The user wants to modify an attached or previously generated image with specific changes, including adding or removing elements, altering colors, improving quality/resolution, or transforming the style (e.g., cartoon, oil painting).

        Guidelines:
        - Set `transparent_background` to true when the request calls for a transparent background, including background removal or a cutout; set it to false otherwise. For edits, preserve existing transparency unless the user asks to change it.
        - Omit both `referenced_image_paths` and `num_last_images_to_include` when generating a brand new image.
        - For edits, use `referenced_image_paths` when every target image has a local file path.
        - If you have not seen a local image yet, use `ReadFile` to inspect it before editing.
        - Use `num_last_images_to_include` only when at least one target image has no local file path.
        - Set `num_last_images_to_include` to the smallest number of recent conversation images that includes every target image, up to {maxReferenceImages}.
        - Never provide both `referenced_image_paths` and `num_last_images_to_include`.
        - If neither mechanism can include every target image, ask the user to attach the missing images again.
        - Directly generate the image without reconfirmation or clarification unless required images must be attached again.
        - Always use this tool for image editing unless the user explicitly requests otherwise. Do not edit images with shell commands or scripts unless specifically instructed.
        """;

    public static JsonElement InputSchema(int maxReferenceImages) =>
        JsonSerializer.SerializeToElement(new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["prompt"] = new JsonObject { ["type"] = "string" },
                ["transparent_background"] = new JsonObject
                {
                    ["type"] = "boolean",
                    ["description"] = "Whether the output should have a transparent background. Defaults to false."
                },
                ["referenced_image_paths"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject { ["type"] = "string" },
                    ["maxItems"] = maxReferenceImages
                },
                ["num_last_images_to_include"] = new JsonObject
                {
                    ["type"] = "integer",
                    ["minimum"] = 1,
                    ["maximum"] = maxReferenceImages
                }
            },
            ["required"] = new JsonArray("prompt"),
            ["additionalProperties"] = false
        });
}
