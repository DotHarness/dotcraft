using System.Text.Json;
using DotCraft.Plugins;

namespace Acme.ReviewCore;

/// <summary>This plugin's own settings bag from Host config under
/// <c>Plugins.Settings["acme.review-core"]</c>.</summary>
/// <remarks>The context is an activation snapshot. Each property owns its fallback because the Host does not validate the bag.</remarks>
internal sealed class ReviewSettings(IPluginActivationContext context)
{
    public int ChecklistLimit =>
        context.Settings.TryGetProperty("checklistLimit", out var value)
        && value.TryGetInt32(out var limit)
        && limit is >= 1 and <= 10
            ? limit
            : 3;

    public string Tone =>
        context.Settings.TryGetProperty("tone", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() switch
            {
                "coaching" => "coaching",
                _ => "direct"
            }
            : "direct";

    public int MaxInputLength =>
        context.Settings.TryGetProperty("maxInputLength", out var value)
        && value.TryGetInt32(out var limit)
        && limit > 0
            ? limit
            : 2000;
}
