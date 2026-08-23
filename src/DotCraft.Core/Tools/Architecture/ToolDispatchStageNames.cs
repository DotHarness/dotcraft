namespace DotCraft.Tools;

/// <summary>Tier-B replacement target names of the built-in tool dispatch stages. Names are scoped to one contribution point, so the same string may be reused across stages.</summary>
public static class ToolDispatchStageNames
{
    /// <summary>The built-in policy evaluator (thread and mode authority).</summary>
    public const string PolicyDefault = "policy:default";

    /// <summary>The built-in approval evaluator.</summary>
    public const string ApprovalDefault = "approval:default";

    /// <summary>The built-in late-bound Session lifecycle recorder router.</summary>
    public const string RecorderRouter = "recorder:router";

    /// <summary>The built-in audience normalizer and result limiter.</summary>
    public const string NormalizerDefault = "normalizer:default";
}
