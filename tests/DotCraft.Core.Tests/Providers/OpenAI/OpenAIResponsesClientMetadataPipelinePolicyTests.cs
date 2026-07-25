using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Agents;

namespace DotCraft.Tests.Agents;

public sealed class OpenAIResponsesClientMetadataPipelinePolicyTests
{
    private const string InstallationId = "11111111-1111-4111-8111-111111111111";

    [Fact]
    public void AddsClientMetadataWhenAbsent()
    {
        var original = JsonSerializer.Serialize(new
        {
            model = "gpt-5-codex",
            instructions = "You are a helpful assistant.",
            input = new[] { new { type = "message", role = "user", content = "hi" } }
        });

        var rewritten = OpenAIResponsesClientMetadataPipelinePolicy.AddInstallationIdMetadata(
            original,
            InstallationId);

        Assert.NotNull(rewritten);
        var node = JsonNode.Parse(rewritten!);
        Assert.NotNull(node);
        Assert.Equal(InstallationId, node!["client_metadata"]!["x-codex-installation-id"]!.GetValue<string>());
    }

    [Fact]
    public void MergesIntoExistingClientMetadataWithoutOverwriting()
    {
        var original = JsonSerializer.Serialize(new
        {
            model = "gpt-5-codex",
            client_metadata = new Dictionary<string, string>
            {
                ["caller-tag"] = "dotcraft"
            }
        });

        var rewritten = OpenAIResponsesClientMetadataPipelinePolicy.AddInstallationIdMetadata(
            original,
            InstallationId);

        Assert.NotNull(rewritten);
        var node = JsonNode.Parse(rewritten!);
        Assert.NotNull(node);
        Assert.Equal("dotcraft", node!["client_metadata"]!["caller-tag"]!.GetValue<string>());
        Assert.Equal(InstallationId, node["client_metadata"]!["x-codex-installation-id"]!.GetValue<string>());
    }

    [Fact]
    public void DoesNotRewriteWhenExistingInstallationIdMatches()
    {
        var original = JsonSerializer.Serialize(new
        {
            client_metadata = new Dictionary<string, string>
            {
                ["x-codex-installation-id"] = InstallationId
            }
        });

        var rewritten = OpenAIResponsesClientMetadataPipelinePolicy.AddInstallationIdMetadata(
            original,
            InstallationId);

        Assert.Null(rewritten); // signal: no change required
    }

    [Fact]
    public void OverwritesMismatchedExistingInstallationId()
    {
        var existingId = "22222222-2222-4222-8222-222222222222";
        var original = JsonSerializer.Serialize(new
        {
            client_metadata = new Dictionary<string, string>
            {
                ["caller-tag"] = "dotcraft",
                ["x-codex-installation-id"] = existingId
            }
        });

        var rewritten = OpenAIResponsesClientMetadataPipelinePolicy.AddInstallationIdMetadata(
            original,
            InstallationId);

        Assert.NotNull(rewritten);
        var node = JsonNode.Parse(rewritten!);
        Assert.NotNull(node);
        Assert.Equal("dotcraft", node!["client_metadata"]!["caller-tag"]!.GetValue<string>());
        Assert.Equal(InstallationId, node!["client_metadata"]!["x-codex-installation-id"]!.GetValue<string>());
    }

    [Fact]
    public void OverwritesMismatchedExistingInstallationIdAndCanonicalizesDuplicateTopLevelKeys()
    {
        var original = """
            {
              "model": "gpt-test",
              "input": [],
              "client_metadata": {
                "x-codex-installation-id": "22222222-2222-4222-8222-222222222222"
              },
              "input": [
                {
                  "type": "message",
                  "role": "user",
                  "content": [
                    {
                      "type": "input_text",
                      "text": "hello"
                    }
                  ]
                }
              ]
            }
            """;

        var rewritten = OpenAIResponsesClientMetadataPipelinePolicy.AddInstallationIdMetadata(
            original,
            InstallationId);

        Assert.NotNull(rewritten);
        using var document = JsonDocument.Parse(rewritten!);
        Assert.Equal(1, document.RootElement.EnumerateObject().Count(prop => prop.Name == "input"));
        Assert.Equal(
            InstallationId,
            document.RootElement
                .GetProperty("client_metadata")
                .GetProperty("x-codex-installation-id")
                .GetString());
        var input = Assert.Single(document.RootElement.GetProperty("input").EnumerateArray());
        Assert.Equal("hello", input.GetProperty("content")[0].GetProperty("text").GetString());
    }

    [Fact]
    public void AddsCodexTurnMetadataAndOverridesReservedCallerValues()
    {
        var original = JsonSerializer.Serialize(new
        {
            model = "gpt-5-codex",
            client_metadata = new Dictionary<string, string>
            {
                ["caller-tag"] = "dotcraft",
                ["session_id"] = "wrong-session",
                ["thread_id"] = "wrong-thread",
                ["turn_id"] = "wrong-turn",
                ["x-codex-window-id"] = "wrong-window",
                ["x-codex-turn-metadata"] = "{}"
            }
        });
        var turnMetadataJson = JsonSerializer.Serialize(new
        {
            installation_id = InstallationId,
            session_id = "thread-codex",
            thread_id = "thread-codex",
            turn_id = "turn_001",
            window_id = "0192b455-3e7c-7000-8000-000000000001",
            request_kind = "turn",
            turn_started_at_unix_ms = 1778544000000
        });
        var snapshot = new OpenAIResponsesCodexMetadataSnapshot(
            InstallationId,
            SessionId: "thread-codex",
            ThreadId: "thread-codex",
            ClientRequestId: "thread-codex",
            DefaultPromptCacheKey: "thread-codex",
            TurnId: "turn_001",
            WindowId: "0192b455-3e7c-7000-8000-000000000001",
            ParentThreadId: null,
            SubagentHeader: null,
            SubagentKind: null,
            TurnMetadataJson: turnMetadataJson,
            TurnState: null);

        var rewritten = OpenAIResponsesClientMetadataPipelinePolicy.AddCodexClientMetadata(
            original,
            snapshot);

        Assert.NotNull(rewritten);
        using var document = JsonDocument.Parse(rewritten!);
        var metadata = document.RootElement.GetProperty("client_metadata");
        Assert.Equal("dotcraft", metadata.GetProperty("caller-tag").GetString());
        Assert.Equal(InstallationId, metadata.GetProperty("x-codex-installation-id").GetString());
        Assert.Equal("thread-codex", metadata.GetProperty("session_id").GetString());
        Assert.Equal("thread-codex", metadata.GetProperty("thread_id").GetString());
        Assert.Equal("turn_001", metadata.GetProperty("turn_id").GetString());
        Assert.Equal("0192b455-3e7c-7000-8000-000000000001", metadata.GetProperty("x-codex-window-id").GetString());

        using var turnMetadata = JsonDocument.Parse(metadata.GetProperty("x-codex-turn-metadata").GetString()!);
        var root = turnMetadata.RootElement;
        Assert.Equal(InstallationId, root.GetProperty("installation_id").GetString());
        Assert.Equal("thread-codex", root.GetProperty("session_id").GetString());
        Assert.Equal("thread-codex", root.GetProperty("thread_id").GetString());
        Assert.Equal("turn_001", root.GetProperty("turn_id").GetString());
        Assert.Equal("0192b455-3e7c-7000-8000-000000000001", root.GetProperty("window_id").GetString());
        Assert.Equal("turn", root.GetProperty("request_kind").GetString());
        Assert.Equal(1778544000000, root.GetProperty("turn_started_at_unix_ms").GetInt64());
    }

    [Fact]
    public void RemoveUnsupportedOAuthResponsesFields_DropsMaxOutputTokens()
    {
        var original = JsonSerializer.Serialize(new
        {
            model = "gpt-5-codex",
            max_output_tokens = 12000,
            stream = true
        });

        var rewritten = OpenAIResponsesClientMetadataPipelinePolicy.RemoveUnsupportedOAuthResponsesFields(original);

        Assert.NotNull(rewritten);
        using var document = JsonDocument.Parse(rewritten!);
        Assert.False(document.RootElement.TryGetProperty("max_output_tokens", out _));
        Assert.Equal("gpt-5-codex", document.RootElement.GetProperty("model").GetString());
        Assert.True(document.RootElement.GetProperty("stream").GetBoolean());
    }

    [Fact]
    public void ReturnsNullOnMalformedJson()
    {
        Assert.Null(OpenAIResponsesClientMetadataPipelinePolicy.AddInstallationIdMetadata(
            "{not json",
            InstallationId));
    }

    [Fact]
    public void ReturnsNullOnEmptyBody()
    {
        Assert.Null(OpenAIResponsesClientMetadataPipelinePolicy.AddInstallationIdMetadata(
            string.Empty,
            InstallationId));
    }

    [Fact]
    public void ReturnsNullWhenRootIsNotAnObject()
    {
        Assert.Null(OpenAIResponsesClientMetadataPipelinePolicy.AddInstallationIdMetadata(
            "[]",
            InstallationId));
    }
}
