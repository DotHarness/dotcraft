// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses the referenced Microsoft.Extensions.AI source to you under the MIT license.
// DotCraft adaptation: owns a compact streaming tool loop so same-turn guidance can be inserted at safe boundaries.

using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using DotCraft.Context;
using DotCraft.Context.Compaction;
using DotCraft.Hooks;
using DotCraft.Protocol;
using DotCraft.Tools;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;
using AnthropicBetaRawContentBlockDeltaEvent = Anthropic.Models.Beta.Messages.BetaRawContentBlockDeltaEvent;
using AnthropicBetaRawContentBlockStartEvent = Anthropic.Models.Beta.Messages.BetaRawContentBlockStartEvent;
using AnthropicBetaRawMessageStreamEvent = Anthropic.Models.Beta.Messages.BetaRawMessageStreamEvent;
using OpenAiStreamingUpdate = OpenAI.Chat.StreamingChatCompletionUpdate;
using OpenAI.Responses;

#pragma warning disable OPENAI001, MEAI001 // Mirrors upstream FunctionInvokingChatClient handling for provider-managed continuations.

namespace DotCraft.Agents;

/// <summary>
/// Raised when an initial provider streaming request completes without effective assistant output.
/// </summary>
public sealed class EmptyProviderResponseException(string message) : InvalidOperationException(message);

/// <summary>
/// DotCraft-owned streaming function invocation loop with safe-boundary hooks
/// for same-turn guidance injection and tool-call argument previews.
/// </summary>
public sealed class StreamingFunctionInvokingChatClient(IChatClient innerClient, IServiceProvider? services = null)
    : DelegatingChatClient(innerClient)
{
    private static readonly AsyncLocal<FunctionInvocationContext?> CurrentInvocationContext = new();
    private static readonly Regex AnsiEscapeRegex = new(@"\x1B\[[0-?]*[ -/]*[@-~]", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SecretAssignmentRegex = new(@"\b(token|access[_-]?token|refresh[_-]?token|api[_-]?key|password|secret)\s*[:=]\s*([^\s,;]+)", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex BearerSecretRegex = new(@"\bBearer\s+[A-Za-z0-9._~+/=-]+", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private const string InvalidToolArgumentsMetadataKey = "dotcraft.toolResult.invalidArguments";
    private const string ToolResultErrorCodeMetadataKey = "dotcraft.toolResult.errorCode";
    private const int ToolFailureMessageMaxChars = 1000;

    /// <summary>
    /// Gets the function invocation context currently flowing through this client.
    /// </summary>
    public static FunctionInvocationContext? CurrentContext => CurrentInvocationContext.Value;

    /// <summary>
    /// Extra tools that may be invoked even when they are not sent in the current
    /// request's <see cref="ChatOptions.Tools"/> list.
    /// </summary>
    public IList<AITool>? AdditionalTools { get; set; }

    /// <summary>
    /// Allows multiple tool calls from one model response to run concurrently.
    /// </summary>
    public bool AllowConcurrentInvocation { get; set; }

    /// <summary>
    /// Includes additional exception details in generated function result content.
    /// </summary>
    public bool IncludeDetailedErrors { get; set; }

    /// <summary>
    /// Maximum number of extra model calls that guidance may add after the
    /// function loop reaches a termination condition.
    /// </summary>
    public int MaximumGuidanceContinuationsPerRequest
    {
        get;
        set
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), "Maximum guidance continuations cannot be negative.");

            field = value;
        }
    } = 8;

    /// <summary>
    /// Maximum consecutive function-call iterations allowed to fail before the
    /// original exception is rethrown.
    /// </summary>
    public int MaximumConsecutiveErrorsPerRequest
    {
        get;
        set
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), "Maximum consecutive errors cannot be negative.");

            field = value;
        }
    } = 3;

    /// <summary>
    /// Terminates the loop when a requested function is not available locally.
    /// </summary>
    public bool TerminateOnUnknownCalls { get; set; }

    /// <summary>
    /// Emits preview-only tool-call argument deltas while provider streaming
    /// payloads are still being assembled into <see cref="FunctionCallContent"/>.
    /// </summary>
    public bool EnableToolCallArgumentPreviews { get; set; }

    /// <summary>
    /// Optional predicate that decides whether argument deltas should be emitted for a tool.
    /// When <see langword="null"/> (default) all tools are eligible.
    /// </summary>
    public Func<string, bool>? IsStreamableTool { get; set; }

    /// <summary>
    /// Tool names that should emit argument delta previews. Used as a fallback when
    /// <see cref="IsStreamableTool"/> is not set. When both are <see langword="null"/>,
    /// all tools are eligible.
    /// </summary>
    public IReadOnlySet<string>? StreamableToolNames { get; set; }

    /// <summary>
    /// Custom invocation hook matching Microsoft.Extensions.AI's public surface.
    /// </summary>
    public Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>>? FunctionInvoker { get; set; }

    /// <summary>
    /// Optional runtime policy hook that may deny a tool call without changing the visible tool schema.
    /// </summary>
    public Func<FunctionInvocationContext, ModeToolPolicyDecision>? ModeToolPolicy { get; set; }

    /// <summary>
    /// Optional runtime policy hook invoked before a tool name is resolved.
    /// Used to reject stale calls to tools hidden by thread capability policy.
    /// </summary>
    public Func<FunctionCallContent, ModeToolPolicyDecision>? ToolCallPolicy { get; set; }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        await GetStreamingResponseAsync(messages, options, cancellationToken).ToChatResponseAsync(cancellationToken);

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var originalMessages = messages.ToList();
        var currentMessages = (IEnumerable<ChatMessage>)originalMessages;
        List<ChatMessage>? augmentedHistory = null;
        List<ChatMessage>? responseMessages = null;
        var consecutiveErrorCount = 0;
        var lastIterationHadConversationId = false;
        var guidanceContinuationCount = 0;
        var toolMessageId = Guid.NewGuid().ToString("N");
        var hasAnyEffectiveProviderOutput = false;
        var awaitingPostToolContinuation = false;

        for (var iteration = 0; ; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var preparedMessages = await PrepareMessagesForSamplingAsync(
                currentMessages,
                options,
                cancellationToken);
            if (!ReferenceEquals(preparedMessages, currentMessages))
            {
                currentMessages = preparedMessages;
                originalMessages = preparedMessages.ToList();
                augmentedHistory = originalMessages.ToList();
                responseMessages = [];
                lastIterationHadConversationId = false;
                ResetProviderContinuationAfterHistoryReplacement(ref options);
            }
            var samplingMessages = preparedMessages;

            var updates = new List<ChatResponseUpdate>();
            var functionCalls = new List<FunctionCallContent>();
            var lastYieldedUpdateIndex = 0;
            var toolCallPreviewTrackers = new Dictionary<int, ToolCallTracker>();
            Dictionary<ChatResponseUpdate, IReadOnlyList<ToolCallArgumentsDeltaContent>>? previewContentsByUpdate = null;
            var requestMarked = false;

            using var promptCacheRequestIndexScope = PromptCacheRequestShapeTraceScope.UseRequestIndex(iteration + 1);
            await foreach (var update in base.GetStreamingResponseAsync(samplingMessages, options, cancellationToken))
            {
                if (!requestMarked)
                {
                    TokenUsageRequestMetadata.MarkRequestStart(update, iteration + 1);
                    requestMarked = true;
                }

                var addedPreviewContents = AddToolCallArgumentPreviews(update, toolCallPreviewTrackers);
                if (addedPreviewContents is { Count: > 0 })
                    (previewContentsByUpdate ??= [])[update] = addedPreviewContents;
                NormalizeFunctionCallArguments(update.Contents);
                updates.Add(update);
                CopyFunctionCalls(update.Contents, functionCalls);

                if (functionCalls.Count == 0)
                {
                    lastYieldedUpdateIndex++;
                    yield return update;
                    RemoveToolCallArgumentPreviews(update, addedPreviewContents);
                }
            }

            MarkServerHandledFunctionCalls(updates, functionCalls);

            for (; lastYieldedUpdateIndex < updates.Count; lastYieldedUpdateIndex++)
            {
                var update = updates[lastYieldedUpdateIndex];
                IReadOnlyList<ToolCallArgumentsDeltaContent>? addedPreviewContents = null;
                previewContentsByUpdate?.TryGetValue(update, out addedPreviewContents);
                yield return update;
                RemoveToolCallArgumentPreviews(update, addedPreviewContents);
            }

            var hasEffectiveProviderOutput = HasEffectiveProviderOutput(updates);
            var providerErrorText = CollectErrorContentText(updates);
            if (!hasEffectiveProviderOutput && !hasAnyEffectiveProviderOutput)
            {
                var message = BuildEmptyProviderResponseMessage(
                    "The model provider returned an empty streaming response before any assistant content, reasoning output, or tool call was received.",
                    providerErrorText);
                if (CompactionErrors.IsPromptTooLongMessage(providerErrorText))
                    throw new InvalidOperationException(message);

                throw new EmptyProviderResponseException(message);
            }
            if (!hasEffectiveProviderOutput && awaitingPostToolContinuation)
            {
                if (string.IsNullOrWhiteSpace(providerErrorText))
                    yield break;

                var message = BuildEmptyProviderResponseMessage(
                    "The model provider returned an error response after tool results were returned to the model.",
                    providerErrorText);
                if (CompactionErrors.IsPromptTooLongMessage(providerErrorText))
                    throw new InvalidOperationException(message);

                throw new EmptyProviderResponseException(message);
            }

            hasAnyEffectiveProviderOutput |= hasEffectiveProviderOutput;
            awaitingPostToolContinuation = false;

            var response = updates.ToChatResponse();
            (responseMessages ??= []).AddRange(response.Messages);

            if (ShouldTerminateLoopBasedOnHandleableFunctions(functionCalls, options))
            {
                FixupHistories(
                    originalMessages,
                    ref currentMessages,
                    ref augmentedHistory,
                    response,
                    responseMessages,
                    ref lastIterationHadConversationId);

                var history = augmentedHistory ?? throw new InvalidOperationException("Augmented history was not initialized.");
                if (guidanceContinuationCount < MaximumGuidanceContinuationsPerRequest &&
                    await TryAppendGuidanceAsync(history, cancellationToken))
                {
                    guidanceContinuationCount++;
                    currentMessages = history;
                    UpdateOptionsForNextIteration(ref options, response.ConversationId);
                    continue;
                }

                yield break;
            }

            FixupHistories(
                originalMessages,
                ref currentMessages,
                ref augmentedHistory,
                response,
                responseMessages,
                ref lastIterationHadConversationId);
            var nextHistory = augmentedHistory ?? throw new InvalidOperationException("Augmented history was not initialized.");

            var toolMessages = await InvokeFunctionsAsync(
                nextHistory,
                options,
                functionCalls,
                iteration,
                consecutiveErrorCount,
                cancellationToken);

            var anyTerminated = false;
            foreach (var message in toolMessages.Messages)
            {
                nextHistory.Add(message);
                responseMessages.Add(message);
                yield return new ChatResponseUpdate
                {
                    Role = message.Role,
                    Contents = message.Contents,
                    MessageId = message.MessageId ?? toolMessageId,
                    ResponseId = message.MessageId ?? toolMessageId,
                    ConversationId = response.ConversationId,
                    CreatedAt = DateTimeOffset.UtcNow,
                    AdditionalProperties = message.AdditionalProperties
                };
            }

            foreach (var message in toolMessages.ModelOnlyMessages)
            {
                nextHistory.Add(message);
                responseMessages.Add(message);
            }

            consecutiveErrorCount = toolMessages.ConsecutiveErrorCount;
            anyTerminated = toolMessages.ShouldTerminate;

            if (anyTerminated)
                yield break;

            await TryAppendGuidanceAsync(nextHistory, cancellationToken);
            UpdateOptionsForNextIteration(ref options, response.ConversationId);
            currentMessages = nextHistory;
            awaitingPostToolContinuation = toolMessages.Messages.Count > 0;
        }
    }

    private static async Task<IReadOnlyList<ChatMessage>> PrepareMessagesForSamplingAsync(
        IEnumerable<ChatMessage> currentMessages,
        ChatOptions? options,
        CancellationToken cancellationToken)
    {
        var messages = currentMessages as IReadOnlyList<ChatMessage> ?? currentMessages.ToList();
        var compaction = PreSamplingCompactionRuntimeScope.Current;
        if (compaction == null)
            return ModelRequestHistorySanitizer.Sanitize(messages);

        var snapshotBeforeCompaction = PromptRequestSnapshot.Capture(
            messages,
            options,
            compaction.ProviderId,
            compaction.Mode,
            compaction.ThreadId,
            compaction.TurnId,
            compaction.EstimatedInputTokens);
        var replacement = compaction.TryCompactWithSnapshotAsync is { } compactWithSnapshot
            ? await compactWithSnapshot(messages, snapshotBeforeCompaction, cancellationToken)
            : await compaction.TryCompactAsync(messages, cancellationToken);
        var preparedMessages = ModelRequestHistorySanitizer.Sanitize(replacement ?? messages);
        if (compaction.CaptureSnapshotAsync is { } capture)
        {
            var snapshot = PromptRequestSnapshot.Capture(
                preparedMessages,
                options,
                compaction.ProviderId,
                compaction.Mode,
                compaction.ThreadId,
                compaction.TurnId,
                compaction.EstimatedInputTokens);
            await capture(snapshot, cancellationToken);
        }

        return preparedMessages;
    }

    private static void FixupHistories(
        IEnumerable<ChatMessage> originalMessages,
        ref IEnumerable<ChatMessage> currentMessages,
        ref List<ChatMessage>? augmentedHistory,
        ChatResponse response,
        List<ChatMessage> allTurnsResponseMessages,
        ref bool lastIterationHadConversationId)
    {
        if (response.ConversationId is not null)
        {
            (augmentedHistory ??= []).Clear();
            lastIterationHadConversationId = true;
        }
        else if (lastIterationHadConversationId)
        {
            augmentedHistory ??= [];
            augmentedHistory.Clear();
            augmentedHistory.AddRange(originalMessages);
            augmentedHistory.AddRange(allTurnsResponseMessages);
            lastIterationHadConversationId = false;
        }
        else
        {
            augmentedHistory ??= originalMessages.ToList();
            augmentedHistory.AddMessages(response);
            lastIterationHadConversationId = false;
        }

        currentMessages = augmentedHistory;
    }

    private static void CopyFunctionCalls(IList<AIContent> contents, List<FunctionCallContent> calls)
    {
        foreach (var content in contents)
        {
            if (content is FunctionCallContent { InformationalOnly: false } functionCall)
                calls.Add(functionCall);
        }
    }

    private static void NormalizeFunctionCallArguments(IList<AIContent> contents)
    {
        foreach (var content in contents)
        {
            if (content is FunctionCallContent { Arguments: null } functionCall)
                functionCall.Arguments = new Dictionary<string, object?>();
        }
    }

    private static bool HasEffectiveProviderOutput(IEnumerable<ChatResponseUpdate> updates)
    {
        foreach (var update in updates)
        {
            if (update.FinishReason == ChatFinishReason.Length)
                return true;

            foreach (var content in update.Contents)
            {
                if (IsEffectiveProviderOutput(content))
                    return true;
            }
        }

        return false;
    }

    private static bool IsEffectiveProviderOutput(AIContent content) =>
        content switch
        {
            UsageContent => false,
            ErrorContent => false,
            TextContent text => !string.IsNullOrEmpty(text.Text),
            TextReasoningContent reasoning => ReasoningContentHelper.TryGetText(reasoning, out _),
            ToolCallArgumentsDeltaContent delta => !string.IsNullOrEmpty(delta.ArgumentsDelta)
                || !string.IsNullOrWhiteSpace(delta.ToolName)
                || !string.IsNullOrWhiteSpace(delta.CallId),
            FunctionCallContent { InformationalOnly: false } => true,
            FunctionCallContent => false,
            _ => true
        };

    private static string? CollectErrorContentText(IEnumerable<ChatResponseUpdate> updates)
    {
        List<string>? values = null;
        foreach (var update in updates)
        {
            foreach (var content in update.Contents)
            {
                if (content is not ErrorContent error)
                    continue;

                values ??= [];
                Add(error.ErrorCode);
                Add(error.Message);
                Add(error.Details?.ToString());
            }
        }

        return values is { Count: > 0 }
            ? string.Join(" ", values)
            : null;

        void Add(string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                values!.Add(value.Trim());
        }
    }

    private static string BuildEmptyProviderResponseMessage(string fallbackMessage, string? providerErrorText)
    {
        if (string.IsNullOrWhiteSpace(providerErrorText))
            return fallbackMessage;

        return fallbackMessage + " Provider error: " + providerErrorText;
    }

    private static void MarkServerHandledFunctionCalls(List<ChatResponseUpdate> updates, List<FunctionCallContent> functionCalls)
    {
        if (functionCalls.Count == 0)
            return;

        HashSet<string>? resultCallIds = null;
        foreach (var update in updates)
        {
            foreach (var content in update.Contents)
            {
                if (content is FunctionResultContent result)
                    (resultCallIds ??= []).Add(result.CallId);
            }
        }

        if (resultCallIds == null)
            return;

        for (var i = functionCalls.Count - 1; i >= 0; i--)
        {
            if (!resultCallIds.Contains(functionCalls[i].CallId))
                continue;

            functionCalls[i].InformationalOnly = true;
            functionCalls.RemoveAt(i);
        }
    }

    private async Task<bool> TryAppendGuidanceAsync(List<ChatMessage> augmentedHistory, CancellationToken cancellationToken)
    {
        var context = TurnGuidanceRuntimeScope.Current;
        if (context == null)
            return false;

        var guidanceMessage = await context.TryDrainGuidanceMessageAsync(cancellationToken);
        if (guidanceMessage == null)
            return false;

        augmentedHistory.Add(guidanceMessage);
        return true;
    }

    private IReadOnlyList<ToolCallArgumentsDeltaContent>? AddToolCallArgumentPreviews(
        ChatResponseUpdate update,
        Dictionary<int, ToolCallTracker> trackers)
    {
        if (!EnableToolCallArgumentPreviews)
            return null;

        List<ToolCallArgumentsDeltaContent>? addedContents = null;
        foreach (var delta in ExtractDeltas(update.RawRepresentation))
        {
            if (!trackers.TryGetValue(delta.Index, out var tracker))
            {
                tracker = new ToolCallTracker();
                trackers[delta.Index] = tracker;
            }

            tracker.CallId ??= delta.CallId;
            tracker.ToolName ??= delta.ToolName;

            if (string.IsNullOrEmpty(delta.ArgumentsDelta))
                continue;
            if (tracker.ToolName is null)
                continue;
            if (!IsEligible(tracker.ToolName))
                continue;

            var isFirst = !tracker.FirstChunkEmitted;
            tracker.FirstChunkEmitted = true;
            var content = new ToolCallArgumentsDeltaContent
            {
                ToolCallIndex = delta.Index,
                ToolName = isFirst ? tracker.ToolName : null,
                CallId = isFirst ? tracker.CallId : null,
                ArgumentsDelta = delta.ArgumentsDelta
            };
            update.Contents.Add(content);
            (addedContents ??= []).Add(content);
        }

        return addedContents;
    }

    private static void RemoveToolCallArgumentPreviews(
        ChatResponseUpdate update,
        IReadOnlyList<ToolCallArgumentsDeltaContent>? addedContents)
    {
        if (addedContents is not { Count: > 0 })
            return;

        foreach (var content in addedContents)
            update.Contents.Remove(content);
    }

    private bool IsEligible(string toolName)
    {
        if (IsStreamableTool is not null)
            return IsStreamableTool(toolName);
        if (StreamableToolNames is not null)
            return StreamableToolNames.Contains(toolName);
        return true;
    }

    internal static IEnumerable<ToolCallDeltaChunk> ExtractDeltas(object? rawRepresentation)
    {
        if (rawRepresentation is OpenAiStreamingUpdate openAiUpdate
            && openAiUpdate.ToolCallUpdates is { Count: > 0 } toolCallUpdates)
        {
            foreach (var toolCallUpdate in toolCallUpdates)
            {
                yield return new ToolCallDeltaChunk(
                    toolCallUpdate.Index,
                    toolCallUpdate.FunctionName,
                    toolCallUpdate.ToolCallId,
                    toolCallUpdate.FunctionArgumentsUpdate?.ToString());
            }

            yield break;
        }

        if (rawRepresentation is StreamingResponseOutputItemAddedUpdate
            {
                Item: FunctionCallResponseItem functionCallItem
            } outputItemAdded)
        {
            yield return new ToolCallDeltaChunk(
                outputItemAdded.OutputIndex,
                functionCallItem.FunctionName,
                functionCallItem.CallId,
                null);
            yield break;
        }

        if (rawRepresentation is StreamingResponseFunctionCallArgumentsDeltaUpdate functionArgumentsDelta)
        {
            yield return new ToolCallDeltaChunk(
                functionArgumentsDelta.OutputIndex,
                null,
                null,
                functionArgumentsDelta.Delta.ToString());
            yield break;
        }

        if (rawRepresentation is AnthropicBetaRawMessageStreamEvent anthropicStreamEvent)
            rawRepresentation = anthropicStreamEvent.Value;

        if (rawRepresentation is AnthropicBetaRawContentBlockStartEvent anthropicStart
            && anthropicStart.ContentBlock.TryPickBetaToolUse(out var anthropicToolUse)
            && TryConvertToolCallIndex(anthropicStart.Index, out var anthropicStartIndex))
        {
            yield return new ToolCallDeltaChunk(
                anthropicStartIndex,
                anthropicToolUse.Name,
                anthropicToolUse.ID,
                null);
            yield break;
        }

        if (rawRepresentation is AnthropicBetaRawContentBlockDeltaEvent anthropicDelta
            && anthropicDelta.Delta.TryPickInputJson(out var anthropicInputJson)
            && TryConvertToolCallIndex(anthropicDelta.Index, out var anthropicDeltaIndex))
        {
            yield return new ToolCallDeltaChunk(
                anthropicDeltaIndex,
                null,
                null,
                anthropicInputJson.PartialJson);
            yield break;
        }

        if (rawRepresentation is IToolCallDeltaChunkSource source)
        {
            foreach (var chunk in source.GetToolCallDeltaChunks())
                yield return chunk;
        }
    }

    private static bool TryConvertToolCallIndex(long index, out int converted)
    {
        if (index is < 0 or > int.MaxValue)
        {
            converted = default;
            return false;
        }

        converted = (int)index;
        return true;
    }

    private bool ShouldTerminateLoopBasedOnHandleableFunctions(List<FunctionCallContent> functionCalls, ChatOptions? options)
    {
        if (functionCalls.Count == 0)
            return true;

        if (!HasAnyTools(options?.Tools, AdditionalTools))
            return TerminateOnUnknownCalls;

        foreach (var call in functionCalls)
        {
            var tool = FindToolDeclaration(call, options);
            if (tool is not null)
            {
                if (tool is not AIFunction)
                    return true;
            }
            else if (TerminateOnUnknownCalls)
            {
                return true;
            }
        }

        return false;
    }

    private async Task<FunctionInvocationBatch> InvokeFunctionsAsync(
        List<ChatMessage> messages,
        ChatOptions? options,
        List<FunctionCallContent> functionCalls,
        int iteration,
        int consecutiveErrorCount,
        CancellationToken cancellationToken)
    {
        var captureExceptions = consecutiveErrorCount < MaximumConsecutiveErrorsPerRequest;
        var results = AllowConcurrentInvocation && functionCalls.Count > 1
            ? await Task.WhenAll(functionCalls.Select((call, index) => InvokeFunctionAsync(
                messages,
                options,
                call,
                iteration,
                index,
                functionCalls.Count,
                captureExceptions,
                cancellationToken)))
            : await InvokeFunctionsSeriallyAsync(
                messages,
                options,
                functionCalls,
                iteration,
                consecutiveErrorCount,
                cancellationToken);

        var contents = new List<AIContent>();
        var shouldTerminate = false;
        var exceptions = new List<Exception>();
        var anyException = false;

        foreach (var result in results)
        {
            shouldTerminate |= result.ShouldTerminate;
            result.Call.InformationalOnly = true;

            var content = CreateFunctionResultContent(result);
            contents.Add(content);

            if (content.Exception != null)
            {
                anyException = true;
                exceptions.Add(content.Exception);
            }
        }

        if (anyException)
        {
            consecutiveErrorCount++;
            if (consecutiveErrorCount > MaximumConsecutiveErrorsPerRequest)
                ThrowFunctionExceptions(exceptions);
        }
        else
        {
            consecutiveErrorCount = 0;
        }

        var messageId = Guid.NewGuid().ToString("N");
        var modelOnlyMessages = CreateHookFeedbackMessages(results);
        return new FunctionInvocationBatch(
            contents.Count == 0 ? [] : [new ChatMessage(ChatRole.Tool, contents) { MessageId = messageId }],
            modelOnlyMessages,
            shouldTerminate,
            consecutiveErrorCount);
    }

    private async Task<FunctionInvocationOutcome[]> InvokeFunctionsSeriallyAsync(
        List<ChatMessage> messages,
        ChatOptions? options,
        List<FunctionCallContent> functionCalls,
        int iteration,
        int consecutiveErrorCount,
        CancellationToken cancellationToken)
    {
        var outcomes = new List<FunctionInvocationOutcome>(functionCalls.Count);
        for (var index = 0; index < functionCalls.Count; index++)
        {
            var outcome = await InvokeFunctionAsync(
                messages,
                options,
                functionCalls[index],
                iteration,
                index,
                functionCalls.Count,
                captureExceptions: consecutiveErrorCount < MaximumConsecutiveErrorsPerRequest,
                cancellationToken);
            outcomes.Add(outcome);
            if (outcome.ShouldTerminate)
                break;
        }

        return outcomes.ToArray();
    }

    private async Task<FunctionInvocationOutcome> InvokeFunctionAsync(
        List<ChatMessage> messages,
        ChatOptions? options,
        FunctionCallContent call,
        int iteration,
        int index,
        int count,
        bool captureExceptions,
        CancellationToken cancellationToken)
    {
        var toolExecution = ToolExecutionTracker.Claim(call.CallId);
        var prePolicyDecision = ToolCallPolicy?.Invoke(call);
        if (prePolicyDecision is { Kind: not ModeToolPolicyDecisionKind.Allow })
        {
            var message = prePolicyDecision.Message ?? "TOOL_POLICY_DENIED";
            CompleteDeniedToolCall(call, toolExecution, message);
            return new FunctionInvocationOutcome(call, FunctionInvocationStatus.RanToCompletion, message, null, false, []);
        }

        var tool = FindTool(call, options);
        if (tool is not AIFunction function)
        {
            toolExecution?.CompleteFailure($"Requested function \"{call.Name}\" not found.");
            return new FunctionInvocationOutcome(call, FunctionInvocationStatus.NotFound, null, null, false, []);
        }

        var arguments = new AIFunctionArguments(call.Arguments)
        {
            Services = services
        };
        var context = new FunctionInvocationContext
        {
            Function = function,
            Arguments = arguments,
            CallContent = call,
            Messages = messages,
            Options = options,
            Iteration = iteration + 1,
            FunctionCallIndex = index,
            FunctionCount = count,
            IsStreaming = true
        };

        var previousContext = CurrentInvocationContext.Value;
        var hookFeedback = new ToolHookFeedbackCollector();
        try
        {
            CurrentInvocationContext.Value = context;
            using var hookFeedbackScope = ToolHookFeedbackScope.Set(hookFeedback);
            var policyDecision = ModeToolPolicy?.Invoke(context);
            if (policyDecision is { Kind: not ModeToolPolicyDecisionKind.Allow })
            {
                var message = policyDecision.Message ?? "MODE_POLICY_DENIED";
                CompleteDeniedToolCall(call, toolExecution, message);
                return new FunctionInvocationOutcome(call, FunctionInvocationStatus.RanToCompletion, message, null, context.Terminate, hookFeedback.Snapshot());
            }

            var value = FunctionInvoker == null
                ? await function.InvokeAsync(arguments, cancellationToken)
                : await FunctionInvoker(context, cancellationToken);
            if (value is FunctionResultContent { Exception: { } resultException })
                toolExecution?.CompleteFailure(SanitizeToolFailureMessage(resultException.Message), value);
            else
                toolExecution?.CompleteSuccess(value);
            await NotifyToolHandlerFinishedAsync(call.Name, call.CallId, cancellationToken);
            return new FunctionInvocationOutcome(call, FunctionInvocationStatus.RanToCompletion, value, null, context.Terminate, hookFeedback.Snapshot());
        }
        catch (OperationCanceledException ex)
        {
            toolExecution?.CompleteCancelled(ex.Message);
            throw;
        }
        catch (Exception ex) when (captureExceptions && ex is not OperationCanceledException)
        {
            toolExecution?.CompleteFailure(SanitizeToolFailureMessage(ex.Message));
            await NotifyToolHandlerFinishedAsync(call.Name, call.CallId, cancellationToken);
            return new FunctionInvocationOutcome(call, FunctionInvocationStatus.Exception, null, ex, false, hookFeedback.Snapshot());
        }
        catch (Exception ex)
        {
            toolExecution?.CompleteFailure(SanitizeToolFailureMessage(ex.Message));
            await NotifyToolHandlerFinishedAsync(call.Name, call.CallId, cancellationToken);
            throw;
        }
        finally
        {
            CurrentInvocationContext.Value = previousContext;
        }
    }

    private static IReadOnlyList<ChatMessage> CreateHookFeedbackMessages(IReadOnlyList<FunctionInvocationOutcome> results)
    {
        var feedback = results
            .SelectMany(result => result.HookFeedback)
            .Where(item => !string.IsNullOrWhiteSpace(item.Text))
            .ToList();
        if (feedback.Count == 0)
            return [];

        return [new ChatMessage(ChatRole.User, BuildHookFeedbackReminder(feedback))];
    }

    private static string BuildHookFeedbackReminder(IReadOnlyList<ToolHookFeedback> feedback)
    {
        var sections = new List<string>
        {
            "<system-reminder>",
            "## Lifecycle Hook Feedback",
            "Model-visible context returned by lifecycle hooks."
        };

        foreach (var item in feedback)
        {
            var label = item.IsBlockingFeedback ? "exit-code-2 feedback" : "additionalContext";
            sections.Add($"### {item.Event} {label}\n{item.Text}");
        }

        sections.Add("</system-reminder>");
        return string.Join("\n\n", sections);
    }

    private static async Task NotifyToolHandlerFinishedAsync(
        string toolName,
        string callId,
        CancellationToken cancellationToken)
    {
        var callback = TurnGuidanceRuntimeScope.Current?.OnToolHandlerFinishedAsync;
        if (callback == null)
            return;

        try
        {
            await callback(toolName, callId, cancellationToken);
        }
        catch
        {
            // Tool lifecycle observers must not change the tool result delivered to the model.
        }
    }

    private static void CompleteDeniedToolCall(
        FunctionCallContent call,
        ToolExecutionTracker? toolExecution,
        string message)
    {
        if (string.Equals(call.Name, "Exec", StringComparison.Ordinal))
            CommandExecutionTracker.CompletePendingFailureByCallId(call.CallId, message);
        toolExecution?.CompleteFailure(message);
    }

    private FunctionResultContent CreateFunctionResultContent(FunctionInvocationOutcome result)
    {
        if (result.Status == FunctionInvocationStatus.RanToCompletion)
        {
            if (result.Value is FunctionResultContent content && content.CallId == result.Call.CallId)
                return content;

            return new FunctionResultContent(result.Call.CallId, result.Value ?? "Success: Function completed.");
        }

        if (result.Status == FunctionInvocationStatus.InvalidArguments)
            return CreateInvalidToolArgumentsResult(result.Call.CallId, result.Value?.ToString() ?? "Error: Invalid tool arguments.");

        if (result.Status == FunctionInvocationStatus.NotFound)
        {
            return CreateToolFailureResult(
                result.Call.CallId,
                $"Error: Requested function \"{result.Call.Name}\" not found.",
                ToolErrorCodes.NotFound);
        }

        var message = result.Status switch
        {
            FunctionInvocationStatus.Exception => CreateFunctionFailureMessage(result.Exception),
            _ => "Error: Unknown error."
        };

        return new FunctionResultContent(result.Call.CallId, message)
        {
            Exception = result.Exception
        };
    }

    private string CreateFunctionFailureMessage(Exception? exception)
    {
        var reason = exception == null
            ? null
            : IncludeDetailedErrors
                ? $"{exception.GetType().Name}: {exception.Message}"
                : exception.Message;
        var safeReason = SanitizeToolFailureMessage(reason);
        return string.IsNullOrEmpty(safeReason)
            ? "Error: Function failed."
            : $"Error: Function failed. Reason: {safeReason}";
    }

    private static string SanitizeToolFailureMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return string.Empty;

        var sanitized = AnsiEscapeRegex.Replace(message, string.Empty);
        sanitized = BearerSecretRegex.Replace(sanitized, "Bearer ***");
        sanitized = SecretAssignmentRegex.Replace(sanitized, "$1=***");
        sanitized = WhitespaceRegex.Replace(sanitized, " ").Trim();

        return sanitized.Length <= ToolFailureMessageMaxChars
            ? sanitized
            : sanitized[..(ToolFailureMessageMaxChars - 3)] + "...";
    }

    internal static bool IsInvalidToolArgumentsResult(FunctionResultContent content)
    {
        if (content.AdditionalProperties == null)
            return false;

        return content.AdditionalProperties.TryGetValue(InvalidToolArgumentsMetadataKey, out var value)
            && value is bool invalid
            && invalid;
    }

    internal static string? GetToolResultErrorCode(FunctionResultContent content)
    {
        if (content.AdditionalProperties == null
            || !content.AdditionalProperties.TryGetValue(ToolResultErrorCodeMetadataKey, out var value))
        {
            return null;
        }

        return value as string;
    }

    private static FunctionResultContent CreateInvalidToolArgumentsResult(string callId, string message)
    {
        var content = new FunctionResultContent(callId, message)
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [InvalidToolArgumentsMetadataKey] = true,
                [ToolResultErrorCodeMetadataKey] = ToolErrorCodes.InputInvalid
            }
        };
        return content;
    }

    internal static FunctionResultContent CreateToolFailureResult(string callId, string message, string errorCode) =>
        new(callId, message)
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [ToolResultErrorCodeMetadataKey] = errorCode
            }
        };

    private AITool? FindTool(FunctionCallContent call, ChatOptions? options)
    {
        static AITool? FindIn(IEnumerable<AITool>? tools, FunctionCallContent functionCall) =>
            tools?.FirstOrDefault(tool => IsMatchingTool(tool, functionCall));

        return FindIn(options?.Tools, call) ?? FindIn(AdditionalTools, call);
    }

    private AIFunctionDeclaration? FindToolDeclaration(FunctionCallContent call, ChatOptions? options)
    {
        static AIFunctionDeclaration? FindIn(IEnumerable<AITool>? tools, FunctionCallContent functionCall) =>
            tools?.OfType<AIFunctionDeclaration>().FirstOrDefault(tool => IsMatchingTool(tool, functionCall));

        return FindIn(options?.Tools, call) ?? FindIn(AdditionalTools, call);
    }

    private static bool IsMatchingTool(AITool tool, FunctionCallContent call)
    {
        if (ResponsesToolSearchMapper.TryGetFunctionCallNamespace(call, out var toolNamespace))
        {
            return CanonicalToolIdentityMetadataResolver.TryGet(tool, out var canonicalName, out _)
                   && string.Equals(canonicalName.Namespace, toolNamespace, StringComparison.Ordinal)
                   && string.Equals(canonicalName.Name, call.Name, StringComparison.Ordinal);
        }

        return string.Equals(tool.Name, call.Name, StringComparison.Ordinal);
    }

    private static bool HasAnyTools(params IList<AITool>?[] toolLists) =>
        toolLists.Any(tools => tools is { Count: > 0 });

    private static void ResetProviderContinuationAfterHistoryReplacement(ref ChatOptions? options)
    {
        if (options?.ConversationId == null && options?.ContinuationToken == null)
            return;

        options = options?.Clone();
        if (options == null)
            return;

        options.ConversationId = null;
        options.ContinuationToken = null;
    }

    private static void UpdateOptionsForNextIteration(ref ChatOptions? options, string? conversationId)
    {
        if (options == null)
        {
            if (conversationId != null)
                options = new ChatOptions { ConversationId = conversationId };
        }
        else if (options.ToolMode is RequiredChatToolMode)
        {
            options = options.Clone();
            options.ToolMode = null;
            options.ConversationId = conversationId;
        }
        else if (options.ConversationId != conversationId)
        {
            options = options.Clone();
            options.ConversationId = conversationId;
        }
        else if (options.ContinuationToken != null)
        {
            options = options.Clone();
        }

        if (options?.ContinuationToken != null)
            options.ContinuationToken = null;
    }

    private static void ThrowFunctionExceptions(List<Exception> exceptions)
    {
        if (exceptions.Count == 1)
            ExceptionDispatchInfo.Capture(exceptions[0]).Throw();

        throw new AggregateException(exceptions);
    }

    private sealed record FunctionInvocationBatch(
        IReadOnlyList<ChatMessage> Messages,
        IReadOnlyList<ChatMessage> ModelOnlyMessages,
        bool ShouldTerminate,
        int ConsecutiveErrorCount);

    private sealed record FunctionInvocationOutcome(
        FunctionCallContent Call,
        FunctionInvocationStatus Status,
        object? Value,
        Exception? Exception,
        bool ShouldTerminate,
        IReadOnlyList<ToolHookFeedback> HookFeedback);

    private enum FunctionInvocationStatus
    {
        RanToCompletion,
        NotFound,
        InvalidArguments,
        Exception
    }

    private sealed class ToolCallTracker
    {
        public string? ToolName { get; set; }

        public string? CallId { get; set; }

        public bool FirstChunkEmitted { get; set; }
    }
}

/// <summary>
/// Internal test seam for providing tool-call chunks without constructing provider SDK types.
/// </summary>
internal interface IToolCallDeltaChunkSource
{
    IEnumerable<ToolCallDeltaChunk> GetToolCallDeltaChunks();
}

/// <summary>
/// Normalized tool-call chunk extracted from provider-native streaming payload.
/// </summary>
internal readonly record struct ToolCallDeltaChunk(
    int Index,
    string? ToolName,
    string? CallId,
    string? ArgumentsDelta);
