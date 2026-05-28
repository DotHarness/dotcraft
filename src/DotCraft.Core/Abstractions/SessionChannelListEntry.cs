namespace DotCraft.Abstractions;

/// <summary>
/// One discoverable session origin channel for AppServer <c>channel/list</c>.
/// <see cref="Name"/> must match <see cref="SessionIdentity.ChannelName"/> / thread <c>OriginChannel</c>.
/// </summary>
public readonly record struct SessionChannelListEntry(string Name, string Category);
