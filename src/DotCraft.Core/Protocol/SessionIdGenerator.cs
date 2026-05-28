namespace DotCraft.Protocol;

/// <summary>
/// Generates stable, human-readable IDs for Session Protocol entities.
/// </summary>
public static class SessionIdGenerator
{
    private static readonly Random Random = Random.Shared;
    private const string Chars = "abcdefghijklmnopqrstuvwxyz0123456789";

    /// <summary>
    /// Generates a new Thread ID.
    /// Format: thread_{yyyyMMdd}_{6-char-random}, e.g. "thread_20260315_a3f2k9".
    /// </summary>
    public static string NewThreadId()
    {
        var date = DateTimeOffset.UtcNow.ToString("yyyyMMdd");
        var random = GenerateRandom(6);
        return $"thread_{date}_{random}";
    }

    /// <summary>
    /// Generates a Turn ID for the given 1-based sequence number within a Thread.
    /// Format: turn_{3-digit-sequence}, e.g. "turn_001".
    /// </summary>
    public static string NewTurnId(int sequence) => $"turn_{sequence:D3}";

    /// <summary>
    /// Generates an Item ID for the given 1-based sequence number within a Turn.
    /// Format: item_{3-digit-sequence}, e.g. "item_001".
    /// </summary>
    public static string NewItemId(int sequence) => $"item_{sequence:D3}";

    /// <summary>
    /// Generates a queued turn input ID.
    /// </summary>
    public static string NewQueuedInputId() => $"queued_{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}_{GenerateRandom(6)}";

    /// <summary>
    /// Generates a new Thread Goal ID.
    /// </summary>
    public static string NewGoalId() => $"goal_{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}_{GenerateRandom(6)}";

    private static string GenerateRandom(int length)
    {
        var chars = new char[length];
        for (var i = 0; i < length; i++)
            chars[i] = Chars[Random.Next(Chars.Length)];
        return new string(chars);
    }
}
