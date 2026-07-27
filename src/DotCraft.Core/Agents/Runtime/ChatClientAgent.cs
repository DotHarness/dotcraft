using System.Runtime.CompilerServices;
using DotCraft.Context;
using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

/// <summary>
/// Represents an immutable DotCraft agent backed by an MEAI <see cref="IChatClient"/> pipeline.
/// </summary>
/// <remarks>
/// Conversation history remains owned by Session Core and is supplied explicitly for each
/// invocation.
/// </remarks>
public sealed class ChatClientAgent
{
    private readonly ChatClientAgentOptions _options;
    private readonly IReadOnlyList<AIContextProvider> _contextProviders;

    public ChatClientAgent(
        IChatClient chatClient,
        ChatOptions? chatOptions = null,
        MemoryContextProvider? contextProvider = null,
        string? name = null)
        : this(
            chatClient,
            new ChatClientAgentOptions
            {
                Name = name,
                ChatOptions = chatOptions,
                AIContextProviders = contextProvider is null ? null : [contextProvider]
            })
    {
    }

    public ChatClientAgent(
        IChatClient chatClient,
        ChatClientAgentOptions options)
    {
        ChatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Clone();
        _contextProviders = (_options.AIContextProviders?.ToList() ?? []).AsReadOnly();
        Metadata = new AgentMetadata(chatClient.GetService<ChatClientMetadata>()?.ProviderName);
    }

    public IChatClient ChatClient { get; }

    public string? Id => _options.Id;

    public string? Name => _options.Name;

    public string? Description => _options.Description;

    public string? Instructions => _options.ChatOptions?.Instructions;

    public AgentMetadata Metadata { get; }

    public ChatOptions? ChatOptions => _options.ChatOptions?.Clone();

    public IReadOnlyList<AIContextProvider> AIContextProviders => _contextProviders;

    public IAsyncEnumerable<ChatResponseUpdate> RunStreamingAsync(
        string input,
        IList<ChatMessage> history,
        CancellationToken cancellationToken = default) =>
        RunStreamingAsync(
            new ChatMessage(ChatRole.User, input ?? throw new ArgumentNullException(nameof(input))),
            history,
            cancellationToken);

    public IAsyncEnumerable<ChatResponseUpdate> RunStreamingAsync(
        string input,
        IList<ChatMessage> history,
        ChatClientAgentRunOptions? runOptions,
        CancellationToken cancellationToken = default) =>
        RunStreamingAsync(
            new ChatMessage(ChatRole.User, input ?? throw new ArgumentNullException(nameof(input))),
            history,
            runOptions,
            cancellationToken);

    public IAsyncEnumerable<ChatResponseUpdate> RunStreamingAsync(
        ChatMessage input,
        IList<ChatMessage> history,
        CancellationToken cancellationToken = default) =>
        RunStreamingAsync([input], history, runOptions: null, cancellationToken);

    public IAsyncEnumerable<ChatResponseUpdate> RunStreamingAsync(
        ChatMessage input,
        IList<ChatMessage> history,
        ChatClientAgentRunOptions? runOptions,
        CancellationToken cancellationToken = default) =>
        RunStreamingAsync([input], history, runOptions, cancellationToken);

    public async IAsyncEnumerable<ChatResponseUpdate> RunStreamingAsync(
        IEnumerable<ChatMessage> input,
        IList<ChatMessage> history,
        ChatClientAgentRunOptions? runOptions = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(history);
        var inputMessages = MaterializeInput(input);
        var invocation = await PrepareInvocationAsync(
                inputMessages,
                history,
                runOptions,
                cancellationToken)
            .ConfigureAwait(false);
        var updates = new List<ChatResponseUpdate>();
        Exception? failure = null;
        var completed = false;

        var enumerator = invocation.ChatClient
            .GetStreamingResponseAsync(invocation.Messages, invocation.Options, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
        try
        {
            while (true)
            {
                ChatResponseUpdate update;
                try
                {
                    if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                    {
                        completed = true;
                        break;
                    }
                    update = enumerator.Current;
                }
                catch (Exception ex)
                {
                    failure = ex;
                    throw;
                }

                update.AuthorName ??= Name;
                updates.Add(update);
                yield return update;
            }
        }
        finally
        {
            try
            {
                await enumerator.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (failure is not null || !completed)
            {
                failure ??= ex;
            }

            if (!completed)
            {
                failure ??= new OperationCanceledException(
                    "The agent response stream was not consumed to completion.");
            }

            var responseMessages = completed ? updates.ToChatResponse().Messages : null;
            await NotifyProvidersAsync(
                    invocation.Messages,
                    responseMessages,
                    failure,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var response = updates.ToChatResponse();
        CommitHistory(history, inputMessages, response.Messages);
    }

    public Task<ChatResponse> RunAsync(
        ChatMessage input,
        IList<ChatMessage>? history = null,
        CancellationToken cancellationToken = default) =>
        RunAsync([input], history, runOptions: null, cancellationToken);

    public Task<ChatResponse> RunAsync(
        ChatMessage input,
        IList<ChatMessage>? history,
        ChatClientAgentRunOptions? runOptions,
        CancellationToken cancellationToken = default) =>
        RunAsync([input], history, runOptions, cancellationToken);

    public async Task<ChatResponse> RunAsync(
        IEnumerable<ChatMessage> input,
        IList<ChatMessage>? history = null,
        ChatClientAgentRunOptions? runOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        history ??= new List<ChatMessage>();
        var inputMessages = MaterializeInput(input);
        var invocation = await PrepareInvocationAsync(
                inputMessages,
                history,
                runOptions,
                cancellationToken)
            .ConfigureAwait(false);

        ChatResponse response;
        try
        {
            response = await invocation.ChatClient
                .GetResponseAsync(invocation.Messages, invocation.Options, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await NotifyProvidersAsync(
                    invocation.Messages,
                    responseMessages: null,
                    ex,
                    cancellationToken)
                .ConfigureAwait(false);
            throw;
        }

        foreach (var message in response.Messages)
            message.AuthorName ??= Name;

        await NotifyProvidersAsync(
                invocation.Messages,
                response.Messages,
                failure: null,
                cancellationToken)
            .ConfigureAwait(false);
        CommitHistory(history, inputMessages, response.Messages);
        return response;
    }

    public Task<ChatResponse> RunAsync(
        string input,
        CancellationToken cancellationToken = default) =>
        RunAsync(
            new ChatMessage(ChatRole.User, input ?? throw new ArgumentNullException(nameof(input))),
            cancellationToken: cancellationToken);

    public Task<ChatResponse> RunAsync(
        string input,
        IList<ChatMessage>? history,
        ChatClientAgentRunOptions? runOptions = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(
            new ChatMessage(ChatRole.User, input ?? throw new ArgumentNullException(nameof(input))),
            history,
            runOptions,
            cancellationToken);

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        if (serviceKey is null)
        {
            if (serviceType.IsInstanceOfType(this))
                return this;
            if (serviceType.IsInstanceOfType(Metadata))
                return Metadata;
            if (serviceType == typeof(ChatClientAgentOptions))
                return _options.Clone();
            if (serviceType == typeof(ChatOptions))
                return ChatOptions;
            if (serviceType.IsInstanceOfType(ChatClient))
                return ChatClient;
        }

        foreach (var provider in _contextProviders)
        {
            if (provider.GetService(serviceType, serviceKey) is { } service)
                return service;
        }

        return ChatClient.GetService(serviceType, serviceKey);
    }

    public T? GetService<T>(object? serviceKey = null)
        where T : class =>
        GetService(typeof(T), serviceKey) as T;

    private async ValueTask<PreparedInvocation> PrepareInvocationAsync(
        IReadOnlyList<ChatMessage> input,
        IList<ChatMessage> history,
        ChatClientAgentRunOptions? runOptions,
        CancellationToken cancellationToken)
    {
        var options = CreateConfiguredChatOptions(runOptions);
        var messages = new List<ChatMessage>(history.Count + input.Count);
        messages.AddRange(history);
        messages.AddRange(input);

        if (_contextProviders.Count > 0)
        {
            var context = new AIContext
            {
                Instructions = options?.Instructions,
                Messages = messages,
                Tools = options?.Tools
            };
            foreach (var provider in _contextProviders)
            {
                context = await provider
                    .InvokingAsync(new AIContextProvider.InvokingContext(this, context), cancellationToken)
                    .ConfigureAwait(false);
            }

            messages = context.Messages?.ToList() ?? [];
            if (context.Instructions is not null || options?.Instructions is not null)
            {
                options ??= new ChatOptions();
                options.Instructions = context.Instructions;
            }
            var tools = context.Tools?.ToList();
            if (tools is { Count: > 0 } || options?.Tools is { Count: > 0 })
            {
                options ??= new ChatOptions();
                options.Tools = tools;
            }
        }

        var chatClient = ChatClient;
        if (runOptions?.ChatClientFactory is { } chatClientFactory)
        {
            chatClient = chatClientFactory(ChatClient)
                ?? throw new InvalidOperationException(
                    $"{nameof(ChatClientAgentRunOptions.ChatClientFactory)} returned null.");
        }
        return new PreparedInvocation(chatClient, messages, options);
    }

    private ChatOptions? CreateConfiguredChatOptions(ChatClientAgentRunOptions? runOptions)
    {
        var request = runOptions?.ChatOptions?.Clone();
        var defaults = _options.ChatOptions;
        if (defaults is null)
            return ApplyRunOverrides(request, runOptions);
        if (request is null)
            return ApplyRunOverrides(defaults.Clone(), runOptions);

        request.AllowMultipleToolCalls ??= defaults.AllowMultipleToolCalls;
        request.ConversationId ??= defaults.ConversationId;
        request.FrequencyPenalty ??= defaults.FrequencyPenalty;
        request.MaxOutputTokens ??= defaults.MaxOutputTokens;
        request.ModelId ??= defaults.ModelId;
        request.PresencePenalty ??= defaults.PresencePenalty;
        request.ResponseFormat ??= defaults.ResponseFormat;
        request.Reasoning ??= defaults.Reasoning;
        request.Seed ??= defaults.Seed;
        request.Temperature ??= defaults.Temperature;
        request.TopP ??= defaults.TopP;
        request.TopK ??= defaults.TopK;
        request.ToolMode ??= defaults.ToolMode;
        request.Instructions = MergeInstructions(defaults.Instructions, request.Instructions);

        if (defaults.AdditionalProperties is { Count: > 0 })
        {
            request.AdditionalProperties ??= new AdditionalPropertiesDictionary();
            foreach (var property in defaults.AdditionalProperties)
                request.AdditionalProperties.TryAdd(property.Key, property.Value);
        }

        if (defaults.RawRepresentationFactory is { } defaultFactory)
        {
            request.RawRepresentationFactory = request.RawRepresentationFactory is { } requestFactory
                ? client => requestFactory(client) ?? defaultFactory(client)
                : defaultFactory;
        }

        request.StopSequences = ConcatCollections(request.StopSequences, defaults.StopSequences);
        request.Tools = ConcatCollections(request.Tools, defaults.Tools);
        return ApplyRunOverrides(request, runOptions);
    }

    private static ChatOptions? ApplyRunOverrides(
        ChatOptions? options,
        ChatClientAgentRunOptions? runOptions)
    {
        if (runOptions?.ResponseFormat is not null)
        {
            options ??= new ChatOptions();
            options.ResponseFormat = runOptions.ResponseFormat;
        }
        if (runOptions?.AdditionalProperties is { Count: > 0 })
        {
            options ??= new ChatOptions();
            options.AdditionalProperties ??= new AdditionalPropertiesDictionary();
            foreach (var property in runOptions.AdditionalProperties)
                options.AdditionalProperties[property.Key] = property.Value;
        }
        return options;
    }

    private async ValueTask NotifyProvidersAsync(
        IReadOnlyList<ChatMessage> requestMessages,
        IEnumerable<ChatMessage>? responseMessages,
        Exception? failure,
        CancellationToken cancellationToken)
    {
        foreach (var provider in _contextProviders)
        {
            try
            {
                var context = failure is null
                    ? new AIContextProvider.InvokedContext(this, requestMessages, responseMessages ?? [])
                    : new AIContextProvider.InvokedContext(this, requestMessages, failure);
                await provider.InvokedAsync(context, cancellationToken).ConfigureAwait(false);
            }
            catch when (failure is not null)
            {
                // Preserve the model, cancellation, or stream-disposal failure.
            }
        }
    }

    private void CommitHistory(
        IList<ChatMessage> history,
        IReadOnlyList<ChatMessage> input,
        IEnumerable<ChatMessage> response)
    {
        foreach (var message in input)
            history.Add(message);
        foreach (var message in response)
        {
            message.AuthorName ??= Name;
            history.Add(message);
        }
    }

    private static List<ChatMessage> MaterializeInput(IEnumerable<ChatMessage> input)
    {
        var messages = input.ToList();
        if (messages.Any(static message => message is null))
            throw new ArgumentException("Input messages cannot contain null values.", nameof(input));
        return messages;
    }

    private static string? MergeInstructions(string? first, string? second) =>
        (!string.IsNullOrWhiteSpace(first), !string.IsNullOrWhiteSpace(second)) switch
        {
            (false, false) => null,
            (true, false) => first,
            (false, true) => second,
            (true, true) => first + "\n" + second
        };

    private static IList<T>? ConcatCollections<T>(IList<T>? request, IList<T>? defaults)
    {
        if (defaults is not { Count: > 0 })
            return request;
        if (request is not { Count: > 0 })
            return [.. defaults];
        return [.. request, .. defaults];
    }

    private sealed record PreparedInvocation(
        IChatClient ChatClient,
        List<ChatMessage> Messages,
        ChatOptions? Options);
}

/// <summary>
/// Provides extensions for creating DotCraft agents from MEAI chat clients.
/// </summary>
public static class ChatClientExtensions
{
    public static ChatClientAgent AsAIAgent(
        this IChatClient chatClient,
        ChatOptions? options = null,
        string? name = null) =>
        new(chatClient, options, name: name);

    public static ChatClientAgent AsAIAgent(
        this IChatClient chatClient,
        ChatClientAgentOptions options) =>
        new(chatClient, options);
}
