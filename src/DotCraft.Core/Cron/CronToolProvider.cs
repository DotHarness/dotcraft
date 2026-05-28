using DotCraft.Abstractions;
using Microsoft.Extensions.AI;

namespace DotCraft.Cron;

/// <summary>
/// Provides Cron (scheduled task) tools.
/// Only available when CronTools is configured.
/// </summary>
public sealed class CronToolProvider : IAgentToolProvider
{
    /// <inheritdoc />
    public int Priority => 70; // System tools have lower priority

    /// <inheritdoc />
    public IEnumerable<AITool> CreateTools(ToolProviderContext context)
    {
        if (context.CronTools == null)
            return [];

        return [AIFunctionFactory.Create(context.CronTools.Cron)];
    }
}
