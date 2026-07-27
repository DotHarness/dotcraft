namespace DotCraft.AppBinding;

public sealed class ThreadSocialBindingRequestCreateParams
{
    public string ThreadId { get; set; } = string.Empty;
    public string ChannelName { get; set; } = string.Empty;
}

public sealed class SocialBindingRequestGetParams
{
    public string Code { get; set; } = string.Empty;
}

public sealed class SocialBindingAcceptParams
{
    public string Code { get; set; } = string.Empty;
    public SocialChannelTargetWire Target { get; set; } = new();
}

public sealed class SocialBindingRebindParams
{
    public string BindingId { get; set; } = string.Empty;
    public long AuthorityRevision { get; set; }
    public SocialChannelTargetWire Target { get; set; } = new();
}
