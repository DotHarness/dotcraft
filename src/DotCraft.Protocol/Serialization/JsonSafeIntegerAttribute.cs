namespace DotCraft.Protocol;

/// <summary>
/// Marks a 64-bit integer contract property whose protocol domain is restricted to the
/// exact integer range shared by JSON and JavaScript runtimes.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class JsonSafeIntegerAttribute : Attribute
{
    /// <summary>Smallest exactly representable integer accepted by the wire contract.</summary>
    public const long Minimum = -9_007_199_254_740_991L;

    /// <summary>Largest exactly representable integer accepted by the wire contract.</summary>
    public const long Maximum = 9_007_199_254_740_991L;
}
