using System.Diagnostics.CodeAnalysis;

namespace DotCraft.Contributions;

/// <summary>
/// The four ways the host combines a contribution point's already-resolved contributions: fan-out, first opinion,
/// single authority, and ordered fold. A reader resolves its own list — with its own thread id,
/// entry projection and default view — and hands it here instead of hand-writing the walk.
/// </summary>
/// <remarks>
/// Every combinator walks the list in resolved order and treats a <see langword="null"/> or empty list
/// as "nothing contributed". Where a combinator takes <c>onFailure</c>, a contribution that throws is
/// reported through it and skipped; where <c>onFailure</c> is optional and omitted, the exception
/// propagates to the caller.
/// </remarks>
public static class ContributionRead
{
    /// <summary>Invokes every contribution for its effect; a thrower is reported and skipped so the ones behind it still run.</summary>
    public static void Fanout<TContribution>(
        IReadOnlyList<TContribution>? contributions,
        Action<TContribution> invoke,
        Action<TContribution, Exception> onFailure)
    {
        if (contributions is not { Count: > 0 })
            return;

        for (var index = 0; index < contributions.Count; index++)
        {
            var contribution = contributions[index];
            try
            {
                invoke(contribution);
            }
            catch (Exception exception)
            {
                onFailure(contribution, exception);
            }
        }
    }

    /// <summary>Awaits every contribution in order for its effect; a thrower is reported and skipped so the ones behind it still run.</summary>
    public static async ValueTask FanoutAsync<TContribution>(
        IReadOnlyList<TContribution>? contributions,
        Func<TContribution, CancellationToken, ValueTask> invoke,
        Action<TContribution, Exception> onFailure,
        CancellationToken cancellationToken = default)
    {
        if (contributions is not { Count: > 0 })
            return;

        for (var index = 0; index < contributions.Count; index++)
        {
            var contribution = contributions[index];
            try
            {
                await invoke(contribution, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                onFailure(contribution, exception);
            }
        }
    }

    /// <summary>Returns the first contribution's answer that is not a decline, or <see langword="null"/> when every contribution declined.</summary>
    public static TOpinion? FirstOpinion<TContribution, TOpinion>(
        IReadOnlyList<TContribution>? contributions,
        Func<TContribution, TOpinion?> ask,
        Action<TContribution, Exception>? onFailure = null)
        where TOpinion : class
    {
        if (contributions is not { Count: > 0 })
            return null;

        for (var index = 0; index < contributions.Count; index++)
        {
            var contribution = contributions[index];
            TOpinion? opinion;
            try
            {
                opinion = ask(contribution);
            }
            catch (Exception exception) when (onFailure is not null)
            {
                onFailure(contribution, exception);
                continue;
            }

            if (opinion is not null)
                return opinion;
        }

        return null;
    }

    /// <summary>Returns the first contribution's answer that is not a decline, for contribution points whose opinion is a value type.</summary>
    public static TOpinion? FirstOpinion<TContribution, TOpinion>(
        IReadOnlyList<TContribution>? contributions,
        Func<TContribution, TOpinion?> ask,
        Action<TContribution, Exception>? onFailure = null)
        where TOpinion : struct
    {
        if (contributions is not { Count: > 0 })
            return null;

        for (var index = 0; index < contributions.Count; index++)
        {
            var contribution = contributions[index];
            TOpinion? opinion;
            try
            {
                opinion = ask(contribution);
            }
            catch (Exception exception) when (onFailure is not null)
            {
                onFailure(contribution, exception);
                continue;
            }

            if (opinion is { } decided)
                return decided;
        }

        return null;
    }

    /// <summary>Awaits each contribution in order and returns the first answer that is not a decline.</summary>
    public static async ValueTask<TOpinion?> FirstOpinionAsync<TContribution, TOpinion>(
        IReadOnlyList<TContribution>? contributions,
        Func<TContribution, CancellationToken, ValueTask<TOpinion?>> ask,
        Action<TContribution, Exception>? onFailure = null,
        CancellationToken cancellationToken = default)
        where TOpinion : class
    {
        if (contributions is not { Count: > 0 })
            return null;

        for (var index = 0; index < contributions.Count; index++)
        {
            var contribution = contributions[index];
            TOpinion? opinion;
            try
            {
                opinion = await ask(contribution, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (onFailure is not null)
            {
                onFailure(contribution, exception);
                continue;
            }

            if (opinion is not null)
                return opinion;
        }

        return null;
    }

    /// <summary>Returns the one contribution that holds a contribution point's authority — the last of the resolved list — or the built-in when nothing is contributed.</summary>
    /// <remarks>Last wins so a contribution that neither replaces the target nor is ordered ahead of it is still reachable: highest <c>Order</c>, later registration breaking ties.</remarks>
    [return: NotNullIfNotNull(nameof(builtIn))]
    public static TContribution? Authority<TContribution>(
        IReadOnlyList<TContribution>? contributions,
        TContribution? builtIn = null)
        where TContribution : class =>
        contributions is { Count: > 0 } resolved ? resolved[^1] : builtIn;

    /// <summary>Folds the contributions over a seed in resolved order, each one transforming the result of the one before it.</summary>
    /// <param name="reverse">Walks the list from the last contribution to the first, so a wrapping fold leaves the lowest-order contribution outermost.</param>
    public static TState Fold<TContribution, TState>(
        IReadOnlyList<TContribution>? contributions,
        TState seed,
        Func<TState, TContribution, TState> step,
        Action<TContribution, Exception>? onFailure = null,
        bool reverse = false)
    {
        if (contributions is not { Count: > 0 })
            return seed;

        var state = seed;
        for (var offset = 0; offset < contributions.Count; offset++)
        {
            var contribution = contributions[reverse ? contributions.Count - 1 - offset : offset];
            try
            {
                state = step(state, contribution);
            }
            catch (Exception exception) when (onFailure is not null)
            {
                onFailure(contribution, exception);
            }
        }

        return state;
    }

    /// <summary>Folds the contributions over a seed in resolved order, awaiting each step.</summary>
    public static async ValueTask<TState> FoldAsync<TContribution, TState>(
        IReadOnlyList<TContribution>? contributions,
        TState seed,
        Func<TState, TContribution, CancellationToken, ValueTask<TState>> step,
        Action<TContribution, Exception>? onFailure = null,
        CancellationToken cancellationToken = default)
    {
        if (contributions is not { Count: > 0 })
            return seed;

        var state = seed;
        for (var index = 0; index < contributions.Count; index++)
        {
            var contribution = contributions[index];
            try
            {
                state = await step(state, contribution, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (onFailure is not null)
            {
                onFailure(contribution, exception);
            }
        }

        return state;
    }
}
