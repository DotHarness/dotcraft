using DotCraft.Agents;
using DotCraft.Configuration;
using Microsoft.Extensions.AI;

namespace DotCraft.Runtime;

internal sealed class PluginProviderCompactorFactory(IProviderNativeCompactorFactory target, PluginInvocation invocation)
    : IProviderNativeCompactorFactory
{
    private readonly PluginTarget<IProviderNativeCompactorFactory> _target = invocation.Capture(target);
    public IProviderNativeCompactor CreateCompactor(EffectiveModelRuntime runtime, IChatClient? rawRepresentationClient = null) =>
        invocation.Invoke(() => new Compactor(_target.Value.CreateCompactor(runtime, rawRepresentationClient), invocation));

    internal static ProviderNativeCompactionInput Copy(ProviderNativeCompactionInput input) =>
        input with { Items = CopyItems(input.Items) };
    internal static ProviderNativeCompactionReplacement Copy(ProviderNativeCompactionReplacement replacement) =>
        replacement with { Items = CopyItems(replacement.Items) };
    private static ProviderHistoryItem[] CopyItems(IReadOnlyList<ProviderHistoryItem> items) =>
        items.Select(item => new ProviderHistoryItem(item.EntryId, item.Payload.Clone())).ToArray();

    private sealed class Compactor(IProviderNativeCompactor target, PluginInvocation invocation) : IProviderNativeCompactor
    {
        private readonly PluginTarget<IProviderNativeCompactor> _target = invocation.Capture(target, ownsTarget: true);
        public Task<ProviderNativeCompactionReplacement> CompactAsync(ProviderNativeCompactionInput input,
            IReadOnlyList<ChatMessage> neutralHistory, ChatOptions? options, CancellationToken ct) =>
            invocation.InvokeAsync(new Func<Task<ProviderNativeCompactionReplacement>>(async () =>
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, invocation.Stopping);
                return Copy(await _target.Value.CompactAsync(input, neutralHistory, options, linked.Token).ConfigureAwait(false));
            }));
    }
}
