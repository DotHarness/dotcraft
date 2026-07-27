using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

/// <summary>
/// Contains request-local instructions, messages, and tools contributed by context providers.
/// </summary>
public sealed class AIContext
{
    /// <summary>Gets or sets invocation-local instructions.</summary>
    public string? Instructions { get; set; }

    /// <summary>Gets or sets invocation-local request messages.</summary>
    public IEnumerable<ChatMessage>? Messages { get; set; }

    /// <summary>Gets or sets invocation-local tools.</summary>
    public IEnumerable<AITool>? Tools { get; set; }
}

/// <summary>
/// Participates in the ordered context lifecycle of a <see cref="ChatClientAgent"/> invocation.
/// </summary>
public abstract class AIContextProvider
{
    /// <summary>Provides context before an agent invocation.</summary>
    public ValueTask<AIContext> InvokingAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return InvokingCoreAsync(context, cancellationToken);
    }

    protected virtual async ValueTask<AIContext> InvokingCoreAsync(
        InvokingContext context,
        CancellationToken cancellationToken)
    {
        var input = context.AIContext;
        var provided = await ProvideAIContextAsync(context, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"{GetType().Name}.{nameof(ProvideAIContextAsync)} returned null.");

        return new AIContext
        {
            Instructions = MergeInstructions(input.Instructions, provided.Instructions),
            Messages = Concat(input.Messages, provided.Messages),
            Tools = Concat(input.Tools, provided.Tools)
        };
    }

    protected virtual ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new AIContext());

    /// <summary>Observes the terminal result of an agent invocation.</summary>
    public ValueTask InvokedAsync(
        InvokedContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return InvokedCoreAsync(context, cancellationToken);
    }

    protected virtual ValueTask InvokedCoreAsync(
        InvokedContext context,
        CancellationToken cancellationToken) =>
        context.InvokeException is null
            ? StoreAIContextAsync(context, cancellationToken)
            : ValueTask.CompletedTask;

    protected virtual ValueTask StoreAIContextAsync(
        InvokedContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    /// <summary>Resolves a service exposed by this provider.</summary>
    public virtual object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
    }

    /// <summary>Resolves a typed service exposed by this provider.</summary>
    public TService? GetService<TService>(object? serviceKey = null)
        where TService : class =>
        GetService(typeof(TService), serviceKey) as TService;

    private static string? MergeInstructions(string? first, string? second) =>
        (first, second) switch
        {
            (null, null) => null,
            ({ } value, null) => value,
            (null, { } value) => value,
            ({ } a, { } b) => a + "\n" + b
        };

    private static IEnumerable<T>? Concat<T>(IEnumerable<T>? first, IEnumerable<T>? second) =>
        (first, second) switch
        {
            (null, null) => null,
            ({ } value, null) => value,
            (null, { } value) => value,
            ({ } a, { } b) => a.Concat(b)
        };

    public sealed class InvokingContext(
        ChatClientAgent agent,
        AIContext aiContext)
    {
        /// <summary>Gets the agent being invoked.</summary>
        public ChatClientAgent Agent { get; } =
            agent ?? throw new ArgumentNullException(nameof(agent));

        /// <summary>Gets the context produced by preceding providers.</summary>
        public AIContext AIContext { get; } =
            aiContext ?? throw new ArgumentNullException(nameof(aiContext));
    }

    public sealed class InvokedContext
    {
        /// <summary>Initializes a successful terminal context.</summary>
        public InvokedContext(
            ChatClientAgent agent,
            IEnumerable<ChatMessage> requestMessages,
            IEnumerable<ChatMessage> responseMessages)
        {
            Agent = agent ?? throw new ArgumentNullException(nameof(agent));
            RequestMessages = requestMessages ?? throw new ArgumentNullException(nameof(requestMessages));
            ResponseMessages = responseMessages ?? throw new ArgumentNullException(nameof(responseMessages));
        }

        /// <summary>Initializes a failed terminal context.</summary>
        public InvokedContext(
            ChatClientAgent agent,
            IEnumerable<ChatMessage> requestMessages,
            Exception invokeException)
        {
            Agent = agent ?? throw new ArgumentNullException(nameof(agent));
            RequestMessages = requestMessages ?? throw new ArgumentNullException(nameof(requestMessages));
            InvokeException = invokeException ?? throw new ArgumentNullException(nameof(invokeException));
        }

        /// <summary>Gets the invoked agent.</summary>
        public ChatClientAgent Agent { get; }

        /// <summary>Gets the exact request messages sent to the chat client.</summary>
        public IEnumerable<ChatMessage> RequestMessages { get; }

        /// <summary>Gets response messages when the invocation succeeded.</summary>
        public IEnumerable<ChatMessage>? ResponseMessages { get; }

        /// <summary>Gets the terminal exception when the invocation failed.</summary>
        public Exception? InvokeException { get; }
    }
}
