using DotCraft.GeneratedTools.Core;
using DotCraft.Tools;
using Microsoft.Extensions.AI;

namespace DotCraft.Cron;

/// <summary>
/// Provides Cron (scheduled task) tools.
/// Only available when CronTools is configured.
/// </summary>
public sealed class CronToolSource(CronTools cronTools) : AIFunctionToolSource
{
    /// <inheritdoc />
    public override string SourceId => "cron";

    /// <inheritdoc />
    public override int Priority => 70;

    /// <inheritdoc />
    protected override IEnumerable<AIFunction> CreateFunctions(ToolPlanningContext context) =>
        [GeneratedToolFunctions.CronTools_Cron(cronTools)];
}
