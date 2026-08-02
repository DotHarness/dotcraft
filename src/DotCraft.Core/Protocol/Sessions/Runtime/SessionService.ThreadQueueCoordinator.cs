using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace DotCraft.Protocol;

public sealed partial class SessionService
{
    private sealed class ThreadQueueCoordinator(SessionService owner)
    {
        public async Task<QueuedTurnInput> EnqueueAsync(
            string threadId,
            IList<AIContent> content,
            SenderContext? sender,
            CancellationToken ct,
            SessionInputSnapshot? inputSnapshot)
        {
            if (content.Count == 0 && inputSnapshot?.MaterializedInputParts is not { Count: > 0 })
                throw new InvalidOperationException("Queued input must not be empty.");

            var thread = await owner.GetOrLoadThreadAsync(threadId, ct);
            if (thread.Status != ThreadStatus.Active)
                throw new InvalidOperationException($"Thread '{threadId}' is not Active (current status: {thread.Status}). Cannot enqueue input.");

            var activeTurnId = thread.Turns
                .LastOrDefault(t => t.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput)
                ?.Id;

            var nativeParts = inputSnapshot?.NativeInputParts?.ToList()
                ?? content.Select(c => c.ToWireInputPart()).ToList();
            var materializedParts = inputSnapshot?.MaterializedInputParts?.ToList()
                ?? nativeParts;
            var displayText = inputSnapshot?.DisplayText
                ?? SessionWireMapper.BuildDisplayText(nativeParts);
            var triggerInfo = TurnTriggerScope.Current;

            var queued = new QueuedTurnInput
            {
                Id = SessionIdGenerator.NewQueuedInputId(),
                ThreadId = threadId,
                NativeInputParts = nativeParts,
                MaterializedInputParts = materializedParts,
                DisplayText = displayText,
                Sender = sender,
                Status = "queued",
                CreatedAt = DateTimeOffset.UtcNow,
                ReadyAfterTurnId = activeTurnId,
                TriggerKind = triggerInfo?.Kind,
                TriggerLabel = triggerInfo?.Label,
                TriggerRefId = triggerInfo?.RefId,
                DeliveryBindingId = inputSnapshot?.DeliveryBindingId,
                SentAsGoal = inputSnapshot?.SentAsGoal
            };

            IReadOnlyList<QueuedTurnInput> queueSnapshot;
            using (await owner.AcquireThreadQueueLockAsync(threadId, ct))
            {
                if (owner._runtimeRegistry.TryGetThread(threadId, out var cachedThread))
                    thread = cachedThread;

                queued = queued with
                {
                    ReadyAfterTurnId = thread.Turns
                        .LastOrDefault(t => t.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput)
                        ?.Id
                };

                var queue = thread.QueuedInputs.ToList();
                queue.Add(queued);
                thread.QueuedInputs = queue;
                thread.LastActiveAt = DateTimeOffset.UtcNow;
                await owner.PersistThreadWithMaterializationAsync(thread, ct);
                queueSnapshot = queue.ToList();
            }

            owner.PublishQueueUpdated(thread.Id, queueSnapshot);
            return queued;
        }

        public async Task<IReadOnlyList<QueuedTurnInput>> RemoveAsync(
            string threadId,
            string queuedInputId,
            CancellationToken ct)
        {
            var thread = await owner.GetOrLoadThreadAsync(threadId, ct);
            IReadOnlyList<QueuedTurnInput> queueSnapshot;
            using (await owner.AcquireThreadQueueLockAsync(threadId, ct))
            {
                if (owner._runtimeRegistry.TryGetThread(threadId, out var cachedThread))
                    thread = cachedThread;

                var queue = thread.QueuedInputs.ToList();
                var removed = queue.RemoveAll(q => string.Equals(q.Id, queuedInputId, StringComparison.Ordinal));
                if (removed == 0)
                    throw new KeyNotFoundException($"Queued input '{queuedInputId}' not found.");

                thread.QueuedInputs = queue;
                thread.LastActiveAt = DateTimeOffset.UtcNow;
                await owner.PersistThreadWithMaterializationAsync(thread, ct);
                queueSnapshot = queue.ToList();
            }

            owner.PublishQueueUpdated(thread.Id, queueSnapshot);
            return queueSnapshot;
        }

        public async Task<IReadOnlyList<QueuedTurnInput>> ReorderAsync(
            string threadId,
            IReadOnlyList<string> orderedQueuedInputIds,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(orderedQueuedInputIds);

            var thread = await owner.GetOrLoadThreadAsync(threadId, ct);
            IReadOnlyList<QueuedTurnInput> queueSnapshot;
            var changed = false;
            using (await owner.AcquireThreadQueueLockAsync(threadId, ct))
            {
                if (owner._runtimeRegistry.TryGetThread(threadId, out var cachedThread))
                    thread = cachedThread;

                var queue = thread.QueuedInputs.ToList();
                var reordered = BuildReorderedQueuedInputs(queue, orderedQueuedInputIds);
                changed = !queue.Select(q => q.Id).SequenceEqual(reordered.Select(q => q.Id), StringComparer.Ordinal);
                if (changed)
                {
                    thread.QueuedInputs = reordered;
                    thread.LastActiveAt = DateTimeOffset.UtcNow;
                    await owner.PersistThreadWithMaterializationAsync(thread, ct);
                }

                queueSnapshot = thread.QueuedInputs.ToList();
            }

            if (changed)
                owner.PublishQueueUpdated(thread.Id, queueSnapshot);
            return queueSnapshot;
        }

        public async Task<IReadOnlyList<QueuedTurnInput>> UpdateAsync(
            string threadId,
            string queuedInputId,
            string expectedTurnId,
            string status,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(expectedTurnId))
                throw new InvalidOperationException("expectedTurnId must not be empty.");
            if (string.IsNullOrWhiteSpace(queuedInputId))
                throw new InvalidOperationException("queuedInputId must not be empty.");
            if (!string.Equals(status, "queued", StringComparison.Ordinal)
                && !string.Equals(status, "guidancePending", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("status must be 'queued' or 'guidancePending'.");
            }

            var thread = await owner.GetOrLoadThreadAsync(threadId, ct);
            IReadOnlyList<QueuedTurnInput> queueSnapshot;
            var changed = false;
            using (await owner.AcquireThreadQueueLockAsync(threadId, ct))
            {
                if (owner._runtimeRegistry.TryGetThread(threadId, out var cachedThread))
                    thread = cachedThread;

                var queue = thread.QueuedInputs.ToList();
                var queueIndex = queue.FindIndex(q => string.Equals(q.Id, queuedInputId, StringComparison.Ordinal));
                if (queueIndex < 0)
                    throw new KeyNotFoundException($"Queued input '{queuedInputId}' not found.");

                var queued = queue[queueIndex];
                if (string.Equals(queued.Status, status, StringComparison.Ordinal))
                {
                    if (string.Equals(status, "guidancePending", StringComparison.Ordinal)
                        && !string.Equals(queued.ReadyAfterTurnId, expectedTurnId, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException($"Queued input '{queuedInputId}' is pending guidance for a different turn.");
                    }
                    queueSnapshot = queue;
                }
                else if (string.Equals(status, "guidancePending", StringComparison.Ordinal))
                {
                    if (!string.Equals(queued.Status, "queued", StringComparison.Ordinal))
                        throw new InvalidOperationException($"Queued input '{queuedInputId}' cannot transition from '{queued.Status}' to guidancePending.");
                    if (thread.Status != ThreadStatus.Active)
                        throw new InvalidOperationException($"Thread '{threadId}' is not Active (current status: {thread.Status}). Cannot promote queued guidance.");
                    var turn = thread.Turns.LastOrDefault(t => t.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput)
                        ?? throw new InvalidOperationException($"Thread '{threadId}' has no active turn to guide.");
                    if (!string.Equals(turn.Id, expectedTurnId, StringComparison.Ordinal))
                        throw new InvalidOperationException($"Expected active turn id '{expectedTurnId}' but found '{turn.Id}'.");

                    queue[queueIndex] = queued with { Status = "guidancePending", ReadyAfterTurnId = turn.Id };
                    thread.QueuedInputs = queue;
                    thread.LastActiveAt = DateTimeOffset.UtcNow;
                    await owner.PersistThreadWithMaterializationAsync(thread, ct);
                    queueSnapshot = queue.ToList();
                    changed = true;
                }
                else
                {
                    if (!string.Equals(queued.Status, "guidancePending", StringComparison.Ordinal))
                        throw new InvalidOperationException($"Queued input '{queuedInputId}' cannot transition from '{queued.Status}' to queued.");
                    if (!string.Equals(queued.ReadyAfterTurnId, expectedTurnId, StringComparison.Ordinal))
                        throw new InvalidOperationException($"Queued input '{queuedInputId}' is not pending guidance for turn '{expectedTurnId}'.");

                    queue[queueIndex] = queued with { Status = "queued" };
                    thread.QueuedInputs = queue;
                    thread.LastActiveAt = DateTimeOffset.UtcNow;
                    await owner.PersistThreadWithMaterializationAsync(thread, ct);
                    queueSnapshot = queue.ToList();
                    changed = true;
                }
            }

            if (changed)
                owner.PublishQueueUpdated(thread.Id, queueSnapshot);
            return queueSnapshot;
        }

        public async Task TryStartNextAsync(string threadId, CancellationToken ct)
        {
            QueuedTurnInput? queued = null;
            SessionThread? thread = null;
            try
            {
                thread = await owner.GetOrLoadThreadAsync(threadId, ct);
                IReadOnlyList<QueuedTurnInput> queueSnapshot;
                var shouldStartQueuedTurn = false;
                using (await owner.AcquireThreadQueueLockAsync(threadId, ct))
                {
                    if (owner._runtimeRegistry.TryGetThread(threadId, out var cachedThread))
                        thread = cachedThread;

                    var queue = thread.QueuedInputs.ToList();
                    var removedLegacyBudgetGuidance = queue.RemoveAll(IsLegacyGoalBudgetGuidanceInput) > 0;
                    var queueIndex = queue.FindIndex(q => string.Equals(q.Status, "queued", StringComparison.Ordinal));
                    if (queueIndex < 0
                        || thread.Turns.Any(t => t.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput)
                        || (owner._runtimeRegistry.TryGetRuntime(threadId, out var runtime) && runtime.Maintenance != null))
                    {
                        if (!removedLegacyBudgetGuidance)
                            return;

                        thread.QueuedInputs = queue;
                        thread.LastActiveAt = DateTimeOffset.UtcNow;
                        await owner.PersistThreadWithMaterializationAsync(thread, ct);
                        queueSnapshot = queue.ToList();
                    }
                    else
                    {
                        queued = queue[queueIndex];
                        queue.RemoveAt(queueIndex);
                        thread.QueuedInputs = queue;
                        thread.LastActiveAt = DateTimeOffset.UtcNow;
                        await owner.PersistThreadWithMaterializationAsync(thread, ct);
                        queueSnapshot = queue.ToList();
                        shouldStartQueuedTurn = true;
                    }
                }

                owner.PublishQueueUpdated(thread.Id, queueSnapshot);
                if (!shouldStartQueuedTurn || queued == null)
                    return;

                var content = await ResolveInputPartsAsync(queued.MaterializedInputParts.ToList(), ct);
                if (content.Count == 0)
                    return;

                using var triggerScope = CreateQueuedInputTriggerScope(queued);
                var events = owner.SubmitInputAsync(
                    threadId,
                    content,
                    queued.Sender,
                    messages: null,
                    ct,
                    new SessionInputSnapshot
                    {
                        NativeInputParts = queued.NativeInputParts,
                        MaterializedInputParts = queued.MaterializedInputParts,
                        DisplayText = queued.DisplayText,
                        DeliveryMode = "queued",
                        QueuedInputId = queued.Id,
                        DeliveryBindingId = queued.DeliveryBindingId,
                        SentAsGoal = queued.SentAsGoal
                    });

                _ = Task.Run(async () =>
                {
                    await foreach (var _ in events.WithCancellation(CancellationToken.None)) { }
                }, CancellationToken.None);
            }
            catch (Exception ex)
            {
                owner.Logger?.LogError(ex, "Failed to start queued input {QueuedInputId} for thread {ThreadId}", queued?.Id, threadId);
                if (thread != null && queued != null)
                {
                    IReadOnlyList<QueuedTurnInput>? queueSnapshot = null;
                    using (await owner.AcquireThreadQueueLockAsync(threadId, CancellationToken.None))
                    {
                        if (owner._runtimeRegistry.TryGetThread(threadId, out var cachedThread))
                            thread = cachedThread;

                        var queue = thread.QueuedInputs.ToList();
                        if (queue.All(q => !string.Equals(q.Id, queued.Id, StringComparison.Ordinal)))
                        {
                            queue.Insert(0, queued);
                            thread.QueuedInputs = queue;
                            await owner.PersistThreadWithMaterializationAsync(thread, CancellationToken.None);
                            queueSnapshot = queue.ToList();
                        }
                    }

                    if (queueSnapshot != null)
                        owner.PublishQueueUpdated(thread.Id, queueSnapshot);
                }
            }
        }

        public async Task<List<AIContent>> ResolveInputPartsAsync(
            List<SessionWireInputPart> parts,
            CancellationToken ct)
        {
            var result = new List<AIContent>(parts.Count);
            foreach (var part in parts)
            {
                result.Add(part.Type switch
                {
                    "localImage" when part.Path is { } path => await ResolveQueuedLocalImageAsync(path, part.MimeType, part.FileName, ct),
                    "image" when part.Url is { } url => await ResolveQueuedRemoteImageAsync(url, ct),
                    _ => part.ToAIContent()
                });
            }

            return result;
        }

        private static List<QueuedTurnInput> BuildReorderedQueuedInputs(
            IReadOnlyList<QueuedTurnInput> queue,
            IReadOnlyList<string> orderedQueuedInputIds)
        {
            if (orderedQueuedInputIds.Count != queue.Count)
                throw new ArgumentException("orderedQueuedInputIds must contain every current queued input ID exactly once.", nameof(orderedQueuedInputIds));

            var byId = queue.ToDictionary(q => q.Id, StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var reordered = new List<QueuedTurnInput>(queue.Count);
            foreach (var queuedInputId in orderedQueuedInputIds)
            {
                if (string.IsNullOrWhiteSpace(queuedInputId))
                    throw new ArgumentException("orderedQueuedInputIds must not contain empty IDs.", nameof(orderedQueuedInputIds));
                if (!seen.Add(queuedInputId))
                    throw new ArgumentException("orderedQueuedInputIds must not contain duplicate IDs.", nameof(orderedQueuedInputIds));
                if (!byId.TryGetValue(queuedInputId, out var queuedInput))
                    throw new ArgumentException($"Queued input '{queuedInputId}' is not in the current queue.", nameof(orderedQueuedInputIds));
                reordered.Add(queuedInput);
            }

            return reordered;
        }

        private static IDisposable? CreateQueuedInputTriggerScope(QueuedTurnInput queued)
        {
            if (string.IsNullOrWhiteSpace(queued.TriggerKind))
                return null;

            return TurnTriggerScope.Set(new TurnTriggerInfo
            {
                Kind = queued.TriggerKind!,
                Label = queued.TriggerLabel,
                RefId = queued.TriggerRefId
            });
        }

        private static async Task<AIContent> ResolveQueuedLocalImageAsync(
            string path,
            string? mimeTypeHint,
            string? fileNameHint,
            CancellationToken ct)
        {
            try
            {
                var bytes = await File.ReadAllBytesAsync(path, ct);
                var data = new DataContent(bytes, InferMediaType(path));
                data.AdditionalProperties ??= new AdditionalPropertiesDictionary();
                data.AdditionalProperties["localImage.path"] = path;
                if (!string.IsNullOrWhiteSpace(mimeTypeHint))
                    data.AdditionalProperties["localImage.mimeType"] = mimeTypeHint.Trim();
                if (!string.IsNullOrWhiteSpace(fileNameHint))
                    data.AdditionalProperties["localImage.fileName"] = fileNameHint.Trim();
                return data;
            }
            catch
            {
                return new TextContent($"[localImage:{path}]");
            }
        }

        private static async Task<AIContent> ResolveQueuedRemoteImageAsync(string url, CancellationToken ct)
        {
            try
            {
                using var response = await QueuedInputHttpClient.GetAsync(url, ct);
                response.EnsureSuccessStatusCode();
                var mediaType = response.Content.Headers.ContentType?.MediaType ?? "image/png";
                var bytes = await response.Content.ReadAsByteArrayAsync(ct);
                return new DataContent(bytes, mediaType);
            }
            catch
            {
                return new TextContent($"[image:{url}]");
            }
        }

        private static string InferMediaType(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                ".bmp" => "image/bmp",
                _ => "image/png"
            };
        }
    }
}
