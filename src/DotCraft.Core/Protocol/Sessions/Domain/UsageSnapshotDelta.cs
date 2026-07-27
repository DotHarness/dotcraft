namespace DotCraft.Protocol;

/// <summary>
/// Converts streaming cumulative <see cref="Microsoft.Extensions.AI.UsageContent"/> snapshots
/// into per-notification deltas for <c>item/usage/delta</c>. Providers often emit monotonically
/// increasing totals within one LLM request; subtracting the previous snapshot yields true increments.
/// </summary>
internal static class UsageSnapshotDelta
{
    /// <summary>
    /// Computes deltas from cumulative snapshots and advances <paramref name="lastInput"/> /
    /// <paramref name="lastOutput"/>. When a snapshot decreases on an axis (new LLM sub-round),
    /// that axis is treated as a fresh cumulative starting from zero: delta = current.
    /// </summary>
    public static void Compute(
        long curInput,
        long curOutput,
        ref long lastInput,
        ref long lastOutput,
        out long deltaInput,
        out long deltaOutput)
    {
        deltaInput = curInput < lastInput ? curInput : curInput - lastInput;
        deltaOutput = curOutput < lastOutput ? curOutput : curOutput - lastOutput;
        if (deltaInput < 0)
            deltaInput = 0;
        if (deltaOutput < 0)
            deltaOutput = 0;
        lastInput = curInput;
        lastOutput = curOutput;
    }
}
