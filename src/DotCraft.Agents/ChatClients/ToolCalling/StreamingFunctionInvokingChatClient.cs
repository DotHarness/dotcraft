// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses the referenced Microsoft.Extensions.AI source to you under the MIT license.
// DotCraft adaptation: owns a compact streaming tool loop so same-turn guidance can be inserted at safe boundaries.

using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using DotCraft.Context.Compaction;
using DotCraft.Tools;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;

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
public sealed partial class StreamingFunctionInvokingChatClient(IChatClient innerClient, IServiceProvider? services = null)
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
        ArgumentNullException.ThrowIfNull(messages);
        var originalMessages = messages.ToList();
        var providerHistoryBridge = ProviderRequestContextScope.Current?.History
                                    ?? GetService(typeof(IProviderConversationHistory)) as IProviderConversationHistory;
        var currentMessages = (IEnumerable<ChatMessage>)originalMessages;
        List<ChatMessage>? augmentedHistory = null;
        List<ChatMessage>? responseMessages = null;
        var consecutiveErrorCount = 0;
        var lastIterationHadConversationId = false;
        var guidanceContinuationCount = 0;
        var toolMessageId = Guid.NewGuid().ToString("N");
        var hasAnyEffectiveProviderOutput = false;
        var awaitingPostToolContinuation = false;

        var initialMailbox = await TryDrainMailboxAsync(cancellationToken);
        if (initialMailbox != null)
        {
            originalMessages.Add(initialMailbox);
            currentMessages = originalMessages;
        }

        for (var iteration = 0; ; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var preparation = await PrepareMessagesForSamplingAsync(currentMessages, options, cancellationToken);
            var preparedMessages = preparation.Messages;
            if (preparation.NeutralHistoryWasReplaced && providerHistoryBridge != null)
            {
                await providerHistoryBridge.HistoryReplacedAsync(
                    preparedMessages,
                    options,
                    "compaction",
                    cancellationToken);
            }
            if (preparation.HistoryWasReplaced
                || !ReferenceEquals(preparedMessages, currentMessages))
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
                if (update is null)
                    throw new InvalidOperationException("The inner chat client streamed a null response update.");

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
                providerHistoryBridge?.MarkProjectionCovered(
                    augmentedHistory ?? throw new InvalidOperationException("Augmented history was not initialized."));

                var history = augmentedHistory ?? throw new InvalidOperationException("Augmented history was not initialized.");
                if (guidanceContinuationCount < MaximumGuidanceContinuationsPerRequest &&
                    await TryAppendAnswerBoundaryMessageAsync(history, cancellationToken))
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
            providerHistoryBridge?.MarkProjectionCovered(nextHistory);

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

            await TryAppendMailboxAsync(nextHistory, cancellationToken);
            await TryAppendGuidanceAsync(nextHistory, cancellationToken);
            UpdateOptionsForNextIteration(ref options, response.ConversationId);
            currentMessages = nextHistory;
            awaitingPostToolContinuation = toolMessages.Messages.Count > 0;
        }
    }

    private static async Task<StreamingSamplingPreparation> PrepareMessagesForSamplingAsync(
        IEnumerable<ChatMessage> currentMessages,
        ChatOptions? options,
        CancellationToken cancellationToken)
    {
        var messages = currentMessages as IReadOnlyList<ChatMessage> ?? currentMessages.ToList();
        var handler = StreamingSamplingRuntimeScope.Current;
        return handler is null
            ? new StreamingSamplingPreparation(
                ModelRequestHistorySanitizer.Sanitize(messages),
                NeutralHistoryWasReplaced: false,
                HistoryWasReplaced: false)
            : await handler(messages, options, cancellationToken).ConfigureAwait(false);
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
        var toolExecution = StreamingToolInvocationRuntimeScope.Current?.Begin(call.CallId);
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
        var hookFeedback = new List<StreamingToolHookFeedback>();
        try
        {
            CurrentInvocationContext.Value = context;
            using var hookFeedbackScope = StreamingToolFeedbackRuntimeScope.Set(hookFeedback.Add);
            var policyDecision = ModeToolPolicy?.Invoke(context);
            if (policyDecision is { Kind: not ModeToolPolicyDecisionKind.Allow })
            {
                var message = policyDecision.Message ?? "MODE_POLICY_DENIED";
                CompleteDeniedToolCall(call, toolExecution, message);
                return new FunctionInvocationOutcome(call, FunctionInvocationStatus.RanToCompletion, message, null, context.Terminate, hookFeedback.ToArray());
            }

            var value = FunctionInvoker == null
                ? await function.InvokeAsync(arguments, cancellationToken)
                : await FunctionInvoker(context, cancellationToken);
            if (value is FunctionResultContent { Exception: { } resultException })
                toolExecution?.CompleteFailure(SanitizeToolFailureMessage(resultException.Message), value);
            else
                toolExecution?.CompleteSuccess(value);
            await NotifyToolHandlerFinishedAsync(toolExecution, call.Name, call.CallId, cancellationToken);
            return new FunctionInvocationOutcome(call, FunctionInvocationStatus.RanToCompletion, value, null, context.Terminate, hookFeedback.ToArray());
        }
        catch (OperationCanceledException ex)
        {
            toolExecution?.CompleteCancelled(ex.Message);
            throw;
        }
        catch (Exception ex) when (captureExceptions && ex is not OperationCanceledException)
        {
            toolExecution?.CompleteFailure(SanitizeToolFailureMessage(ex.Message));
            await NotifyToolHandlerFinishedAsync(toolExecution, call.Name, call.CallId, cancellationToken);
            return new FunctionInvocationOutcome(call, FunctionInvocationStatus.Exception, null, ex, false, hookFeedback.ToArray());
        }
        catch (Exception ex)
        {
            toolExecution?.CompleteFailure(SanitizeToolFailureMessage(ex.Message));
            await NotifyToolHandlerFinishedAsync(toolExecution, call.Name, call.CallId, cancellationToken);
            throw;
        }
        finally
        {
            CurrentInvocationContext.Value = previousContext;
        }
    }

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
        if (ProviderFunctionCallMetadata.TryGetNamespace(call, out var toolNamespace))
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
        IReadOnlyList<StreamingToolHookFeedback> HookFeedback);

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
