using System.Runtime.CompilerServices;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using static SmokeAssertions;

internal sealed class FakeModelProvider : IModelProvider
{
    public IReadOnlyCollection<string> Protocols { get; } = [ModelProviderProtocols.OpenAIChatCompletions];

    public IChatClient CreateChatClient(EffectiveModelRuntime runtime) => new FakeChatClient();
}

internal sealed class FakeChatClient : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Respond(messages, options))));

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return new ChatResponseUpdate(ChatRole.Assistant, Respond(messages, options));
        await Task.CompletedTask;
    }

    private static List<AIContent> Respond(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        var history = messages.ToList();
        var inputIndex = history.FindLastIndex(message =>
            message.Role == ChatRole.User && message.Text.Contains("package-smoke|", StringComparison.Ordinal));
        if (inputIndex < 0 || options?.Tools is not { Count: > 0 } ||
            history.Skip(inputIndex + 1).Any(message => message.Contents.OfType<FunctionResultContent>().Any()))
            return [new TextContent("package-smoke-ok")];

        var text = history[inputIndex].Text;
        var command = text[text.IndexOf("package-smoke|", StringComparison.Ordinal)..].Split('\n')[0].Trim().Split('|');
        Ensure(command.Length == 4, "Invalid package smoke model command.");
        var canonicalName = new ToolName(command[1], command[2]);
        var name = ProviderToolProjector.Project([canonicalName])[canonicalName];
        var function = options.Tools.OfType<AIFunctionDeclaration>().SingleOrDefault(tool => tool.Name == name);
        Ensure(function is not null, "The model did not receive tool " + name + ". Available: " + string.Join(", ", options.Tools.Select(tool => tool.Name)));
        var properties = function!.JsonSchema.GetProperty("properties");
        Ensure(!properties.TryGetProperty("invocation", out _) && !properties.TryGetProperty("cancellationToken", out _),
            "Injected infrastructure parameters leaked into the model schema.");
        if (command[2] == "Envelope")
            Ensure(function.ReturnJsonSchema is null, "The runtime envelope leaked into the output schema.");

        Dictionary<string, object?> arguments = command[2] switch
        {
            "Build" => new() { ["pluginId"] = command[3] },
            "Echo" => new()
            {
                ["request"] = new Dictionary<string, object?> { ["value"] = command[3], ["mode"] = "upperCase" }
            },
            _ => []
        };
        return [new FunctionCallContent("package-call-" + Guid.NewGuid().ToString("N"), name, arguments)];
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose() { }
}
