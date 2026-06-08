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
