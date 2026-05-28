using System.Text.Json;

namespace DotCraft.Hub;

internal static class HubJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
}
