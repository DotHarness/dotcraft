namespace DotCraft.Oratorio.Integrations;

internal sealed class BackgroundWorkWait(TimeSpan pollInterval)
{
    private static readonly TimeSpan SettlePeriod = TimeSpan.FromSeconds(2);

    public Task? Timer { get; private set; }
    public bool Settling { get; private set; }

    public static TimeSpan PollInterval(DotCraftOptions options) =>
        TimeSpan.FromTicks(Math.Min(TimeSpan.FromSeconds(30).Ticks, options.StallTimeout.Ticks / 3));

    public void Reset()
    {
        Timer = null;
        Settling = false;
    }

    public async Task<bool> ScheduleAsync(IDotCraftAppServerClient client, string threadId, CancellationToken ct)
    {
        if (!await client.HasBackgroundWorkAsync(threadId, ct))
        {
            Settling = true;
            Timer = Task.Delay(SettlePeriod, ct);
            return false;
        }

        Settling = false;
        Timer = Task.Delay(pollInterval, ct);
        return true;
    }
}
