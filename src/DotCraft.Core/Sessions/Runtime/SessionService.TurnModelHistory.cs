using System.Text.Json;
using DotCraft.Agents;
using Microsoft.Extensions.AI;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private sealed class TurnModelHistory(
        List<ChatMessage> session,
        string turnId,
        TurnCommitter committer,
        Func<IReadOnlyList<ChatMessage>, CancellationToken, Task> persist) : IAgentHistoryObserver
    {
        private const string InputsKey = "dotcraft.history.inputs";
        private readonly List<Admission> _admissions = [];
        public bool RequiresReconciliation { get; private set; }

        public async ValueTask OnHistoryChangedAsync(AgentHistoryUpdate update, CancellationToken cancellationToken)
        {
            if (update.IsReplacement)
            {
                // Session Core's compaction installer has already persisted and installed this baseline.
                committer.PersistedModelHistoryCount = session.Count;
                return;
            }
            try
            {
                var delta = session.Skip(committer.PersistedModelHistoryCount).Concat(update.Messages).ToArray();
                await persist(delta, cancellationToken);
                session.AddRange(update.Messages);
                committer.PersistedModelHistoryCount = session.Count;
                var incorporated = update.Messages.SelectMany(Inputs).Select(input => input.InputId)
                    .ToHashSet(StringComparer.Ordinal);
                foreach (var admission in _admissions.ToArray())
                {
                    if (!admission.Ids.All(incorporated.Contains)) continue;
                    await admission.Commit();
                    admission.Lease.Dispose();
                    _admissions.Remove(admission);
                }
            }
            catch
            {
                RequiresReconciliation = true;
                AbortPending();
                throw;
            }
        }

        public void Stage(ChatMessage message, string itemId, IReadOnlyList<string> ids, Func<Task> commit, IDisposable lease)
        {
            message.AdditionalProperties ??= new();
            message.AdditionalProperties[InputsKey] = JsonSerializer.SerializeToElement(
                ids.Select(id => new InputIdentity(id, itemId, turnId)).ToArray());
            _admissions.Add(new Admission(ids, commit, lease));
        }

        public void AbortPending()
        {
            foreach (var admission in _admissions) admission.Lease.Dispose();
            _admissions.Clear();
        }

        public static ChatMessage Combine(ChatMessage first, ChatMessage second) => new(
            ChatRole.User, first.Contents.Concat(second.Contents).ToList())
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [InputsKey] = JsonSerializer.SerializeToElement(Inputs(first).Concat(Inputs(second)).ToArray())
            }
        };

        public static IReadOnlyList<InputIdentity> Inputs(ChatMessage message)
        {
            if (message.AdditionalProperties?.TryGetValue(InputsKey, out var value) != true) return [];
            return ((JsonElement)value!).Deserialize<InputIdentity[]>()!;
        }

        internal sealed record InputIdentity(string InputId, string ItemId, string TurnId);
        private sealed record Admission(IReadOnlyList<string> Ids, Func<Task> Commit, IDisposable Lease);
    }
}
