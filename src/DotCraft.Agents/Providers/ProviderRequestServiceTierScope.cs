namespace DotCraft.Agents;

public static class ProviderRequestServiceTierScope
{
    private static readonly AsyncLocal<string?> Tier = new();

    public static string? Current => Tier.Value;

    public static IDisposable Push(string? serviceTier)
    {
        var previous = Tier.Value;
        Tier.Value = serviceTier;
        return new Scope(previous);
    }

    private sealed class Scope(string? previous) : IDisposable
    {
        public void Dispose() => Tier.Value = previous;
    }
}
