using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

/// <summary>
/// Describes the provider backing an agent.
/// </summary>
public sealed class AgentMetadata
{
    /// <summary>Initializes metadata for the specified provider.</summary>
    public AgentMetadata(string? providerName = null)
    {
        ProviderName = providerName;
    }

    /// <summary>Gets the provider name reported by the underlying chat client.</summary>
    public string? ProviderName { get; }
}

/// <summary>
/// Configures an immutable <see cref="ChatClientAgent"/>.
/// </summary>
public sealed class ChatClientAgentOptions
{
    /// <summary>Gets or sets the stable agent identifier.</summary>
    public string? Id { get; set; }

    /// <summary>Gets or sets the agent display name.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets the agent description.</summary>
    public string? Description { get; set; }

    /// <summary>Gets or sets the immutable default chat options.</summary>
    public ChatOptions? ChatOptions { get; set; }

    /// <summary>Gets or sets the ordered context providers.</summary>
    public IEnumerable<AIContextProvider>? AIContextProviders { get; set; }

    /// <summary>Creates an invocation-safe clone.</summary>
    public ChatClientAgentOptions Clone() =>
        new()
        {
            Id = Id,
            Name = Name,
            Description = Description,
            ChatOptions = ChatOptions?.Clone(),
            AIContextProviders = AIContextProviders?.ToList()
        };
}

/// <summary>
/// Provides request-local overrides for one agent invocation.
/// </summary>
public sealed class ChatClientAgentRunOptions
{
    /// <summary>Gets or sets request-local chat option overrides.</summary>
    public ChatOptions? ChatOptions { get; set; }

    /// <summary>Gets or sets request-local additional properties.</summary>
    public AdditionalPropertiesDictionary? AdditionalProperties { get; set; }

    /// <summary>Gets or sets the request-local response format.</summary>
    public ChatResponseFormat? ResponseFormat { get; set; }

    /// <summary>Gets or sets an optional request-local chat-client transformation.</summary>
    public Func<IChatClient, IChatClient>? ChatClientFactory { get; set; }

    /// <summary>Creates an invocation-safe clone.</summary>
    public ChatClientAgentRunOptions Clone() =>
        new()
        {
            ChatOptions = ChatOptions?.Clone(),
            AdditionalProperties = AdditionalProperties?.Clone(),
            ResponseFormat = ResponseFormat,
            ChatClientFactory = ChatClientFactory
        };
}
