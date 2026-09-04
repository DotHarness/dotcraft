using System.Text.Json;
using ContractCatalog = DotCraft.Protocol.AppServer.AppServerRpcCatalog;
using Xunit;

namespace DotCraft.Tests.Protocol.AppServer;

public sealed class AppServerProtocolBaselineTests
{
    private const string MessagesResource = "DotCraft.Tests.AppServerMessagesV1.json";

    [Fact]
    public void SharedMessages_CoverRequiredScenariosAndRemainPortable()
    {
        using var fixtures = ReadResource(MessagesResource);
        var catalogMethods = ContractCatalog.All
            .Select(static descriptor => descriptor.Name)
            .ToHashSet(StringComparer.Ordinal);

        var requiredCases = new[]
        {
            "initialize", "thread-start-response-before-notification", "thread-resume",
            "thread-read", "thread-list", "turn-start-and-complete",
            "turn-enqueue-and-interrupt", "turn-failed", "turn-cancelled",
            "approval-callback", "user-input-callback", "dynamic-tool-callback",
            "structured-error", "unknown-notification", "opaque-mcp-result",
            "app-binding", "automation"
        };
        var cases = fixtures.RootElement.GetProperty("cases").EnumerateArray().ToArray();
        var caseNames = cases.Select(static item => item.GetProperty("name").GetString()!).ToHashSet(StringComparer.Ordinal);
        Assert.All(requiredCases, name => Assert.Contains(name, caseNames));

        foreach (var fixtureCase in cases)
        foreach (var message in fixtureCase.GetProperty("messages").EnumerateArray())
        {
            Assert.Equal("2.0", message.GetProperty("jsonrpc").GetString());
            if (message.TryGetProperty("method", out var method))
            {
                var methodName = method.GetString()!;
                Assert.True(catalogMethods.Contains(methodName) || methodName == "fixture/unknownNotification", methodName);
            }
            else
            {
                Assert.True(message.TryGetProperty("result", out _) || message.TryGetProperty("error", out _));
                Assert.True(message.TryGetProperty("id", out _));
            }
        }

    }

    [Fact]
    public void SharedMessages_LockOrderingAndOpaqueJsonExamples()
    {
        using var fixtures = ReadResource(MessagesResource);
        var cases = fixtures.RootElement.GetProperty("cases").EnumerateArray().ToArray();
        var start = cases.Single(static item => item.GetProperty("name").GetString() == "thread-start-response-before-notification")
            .GetProperty("messages");
        Assert.True(start[1].TryGetProperty("result", out _));
        Assert.Equal("thread/started", start[2].GetProperty("method").GetString());

        var mcp = cases.Single(static item => item.GetProperty("name").GetString() == "opaque-mcp-result")
            .GetProperty("messages")[1]
            .GetProperty("result");
        Assert.True(mcp.GetProperty("futureResultField").GetProperty("kept").GetBoolean());
        Assert.Equal("kept", mcp.GetProperty("content")[0].GetProperty("futureContentField").GetString());

        var unknown = cases.Single(static item => item.GetProperty("name").GetString() == "unknown-notification")
            .GetProperty("messages")[0];
        Assert.True(unknown.GetProperty("params").GetProperty("preserveMe").GetBoolean());
    }

    private static JsonDocument ReadResource(string name) => JsonDocument.Parse(ReadResourceText(name));

    private static string ReadResourceText(string name)
    {
        using var stream = typeof(AppServerProtocolBaselineTests).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing embedded resource '{name}'.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
