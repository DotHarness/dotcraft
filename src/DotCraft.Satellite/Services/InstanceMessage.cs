using System.Text.Json;

namespace DotCraft.Satellite.Services;

/// <summary>
/// One line of the single-instance pipe, in the shape the CLI's <c>tool-host join</c> also writes.
/// </summary>
internal sealed record InstanceMessage(string Kind, string? Url)
{
    public const string JoinKind = "join";
    public const string ShowKind = "show";

    public static InstanceMessage Join(string url) => new(JoinKind, url);

    public static InstanceMessage Show() => new(ShowKind, null);

    public string Encode() => JsonSerializer.Serialize(
        Url is null ? new { kind = Kind } : (object)new { kind = Kind, url = Url },
        JsonSerializerOptions.Web);

    public static InstanceMessage? Decode(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;
        try
        {
            var message = JsonSerializer.Deserialize<InstanceMessage>(line, JsonSerializerOptions.Web);
            return string.IsNullOrEmpty(message?.Kind) ? null : message;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
