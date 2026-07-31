using System.Text.Json.Serialization;

namespace DotCraft.AppBinding;

public static class SocialBindingTargetSelections
{
    public const string ConfirmInChannel = "confirmInChannel";
    public const string DesktopPicker = "desktopPicker";
    public const string DeepLink = "deepLink";

    public static bool IsKnown(string value) =>
        string.Equals(value, ConfirmInChannel, StringComparison.Ordinal)
        || string.Equals(value, DesktopPicker, StringComparison.Ordinal)
        || string.Equals(value, DeepLink, StringComparison.Ordinal);
}

public sealed class SocialBindingIntentWire
{
    public string ChannelName { get; set; } = string.Empty;
    public string TargetSelection { get; set; } = SocialBindingTargetSelections.ConfirmInChannel;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayHint { get; set; }
}

public sealed class SocialChannelBoundByWire
{
    public string PlatformUserId { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayName { get; set; }
}

public sealed class SocialChannelTargetWire
{
    public string ChannelName { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AccountId { get; set; }

    public string ConversationKind { get; set; } = string.Empty;
    public string ConversationId { get; set; } = string.Empty;
    public string DeliveryTarget { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayName { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SocialChannelBoundByWire? BoundBy { get; set; }
}

public sealed class AppSocialBindingResolveParams
{
    [JsonIgnore]
    public string AppId { get; set; } = string.Empty;

    public string ChannelName { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AccountId { get; set; }

    public string ConversationKind { get; set; } = string.Empty;
    public string ConversationId { get; set; } = string.Empty;
}

public sealed class AppSocialBindingResolveResult
{
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public AppBindingWire? Binding { get; set; }
}
