using DotCraft.Contributions;

namespace DotCraft.Tools;

/// <summary>Registers the composition root's policy, approval, recorder, and normalizer stages as ordinary built-in contributions.</summary>
internal static class ToolDispatchStageCatalog
{
    /// <summary>The order every built-in stage is registered at.</summary>
    public const int BuiltInOrder = 100;

    /// <summary>Registers the supplied dispatch stages. A <see langword="null"/> stage leaves its contribution point empty.</summary>
    /// <param name="registrar">Optional origin-scoped owner for the handles; when omitted the stages are attributed to <see cref="ContributionOrigin.Builtin"/> and live for the registry's lifetime.</param>
    /// <returns>The handles. The instances belong to the container, so disposing a handle only removes the stage from its contribution point.</returns>
    internal static IReadOnlyList<IContributionHandle> RegisterBuiltIns(
        IContributionRegistry registry,
        IToolPolicyEvaluator? policyEvaluator = null,
        IToolApprovalEvaluator? approvalEvaluator = null,
        IToolInvocationRecorder? recorder = null,
        IToolResultNormalizer? resultNormalizer = null,
        IContributionRegistrar? registrar = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        using var batch = registry.BeginBatch();
        var handles = new List<IContributionHandle>(4);
        if (policyEvaluator is not null)
            handles.Add(Add(registry, registrar, policyEvaluator, ToolDispatchStageNames.PolicyDefault));
        if (approvalEvaluator is not null)
            handles.Add(Add(registry, registrar, approvalEvaluator, ToolDispatchStageNames.ApprovalDefault));
        if (recorder is not null)
            handles.Add(Add(registry, registrar, recorder, ToolDispatchStageNames.RecorderRouter));
        if (resultNormalizer is not null)
            handles.Add(Add(registry, registrar, resultNormalizer, ToolDispatchStageNames.NormalizerDefault));

        return handles;
    }

    private static IContributionHandle Add<TContract>(
        IContributionRegistry registry,
        IContributionRegistrar? registrar,
        TContract stage,
        string targetName)
        where TContract : class, IContributionContract
    {
        var options = new ContributionOptions(Order: BuiltInOrder)
        {
            TargetName = targetName,
            OwnsContribution = false
        };
        return registrar is null ? registry.Add(stage, options) : registrar.Add(stage, options);
    }
}
