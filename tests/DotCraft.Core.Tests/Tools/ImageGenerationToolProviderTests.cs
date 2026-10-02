using System.ClientModel.Primitives;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using ModelPreference = DotCraft.Configuration.ModelPreference;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class ImageGenerationToolProviderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dotcraft-imagegen-test-" + Guid.NewGuid().ToString("N"));

    public ImageGenerationToolProviderTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".craft"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string DataPath => Path.Combine(_root, ".craft");

    public static TheoryData<string, AppConfig, bool> Eligibility => new()
    {
        { "official responses", OpenAIConfig(), true },
        { "official chat completions", OpenAIConfig(protocol: ModelProviderProtocols.OpenAIChatCompletions), true },
        { "chatgpt oauth", ChatGptOAuthConfig(), true },
        { "custom endpoint", OpenAIConfig(endpoint: "https://compatible.example/v1"), false },
        { "custom endpoint opted in", OpenAIConfig(endpoint: "https://compatible.example/v1", supportsImageGeneration: true), true },
        { "official opted out", OpenAIConfig(supportsImageGeneration: false), false },
        { "chatgpt oauth opted out", ChatGptOAuthConfig(supportsImageGeneration: false), false },
        { "missing api key", OpenAIConfig(apiKey: string.Empty, supportsImageGeneration: true), false },
        { "unsupported auth", OpenAIConfig(authMethod: "customOAuth", supportsImageGeneration: true), false },
        { "anthropic", OpenAIConfig(protocol: ModelProviderProtocols.Anthropic, supportsImageGeneration: true), false },
        { "tool disabled", Disabled(OpenAIConfig()), false }
    };

    [Theory]
    [MemberData(nameof(Eligibility))]
    public async Task GetRegistrations_ExposesImagegenOnlyForSupportedProviders(string scenario, AppConfig config, bool expected)
    {
        var registrations = await CreateSource(config, new FakeImageProvider()).GetRegistrationsAsync(CreatePlanningContext());

        if (!expected)
        {
            Assert.True(registrations.Count == 0, scenario);
            return;
        }

        var registration = Assert.Single(registrations);
        Assert.Equal(new ToolName("image_gen", "imagegen"), registration.Definition.Name);
        Assert.Equal(ToolProjectionShape.ImageGeneration, registration.ProjectionShape);
    }

    [Fact]
    public async Task ResponsesRequest_SendsTheReservedImagegenDefinitionVerbatim()
    {
        var registration = Assert.Single(
            await CreateSource(OpenAIConfig(), new FakeImageProvider()).GetRegistrationsAsync(CreatePlanningContext()));
        var snapshot = new EffectiveToolSnapshotBuilder().Build([registration], 1);
        var tools = ToolSchemaSanitizer.SanitizeTools(AgentFactory.ProjectSnapshotTools(snapshot));

        var request = JsonNode.Parse(ModelReaderWriter.Write(ResponsesToolSearchMapper.CreateResponseOptions(
            "gpt-test",
            [new ChatMessage(ChatRole.User, "draw a cat")],
            new ChatOptions { Tools = tools })).ToString())!;

        var expected = JsonNode.Parse("""
            {
              "type": "namespace",
              "name": "image_gen",
              "description": "Tools in the image_gen namespace.",
              "tools": [
                {
                  "type": "function",
                  "name": "imagegen",
                  "description": null,
                  "parameters": {
                    "type": "object",
                    "properties": {
                      "num_last_images_to_include": { "type": ["integer", "null"] },
                      "prompt": { "type": "string" },
                      "referenced_image_paths": {
                        "type": ["array", "null"],
                        "items": {
                          "type": "string",
                          "description": "A path that is guaranteed to be absolute and normalized (though it is not guaranteed to be canonicalized or exist on the filesystem).\n\nIMPORTANT: When deserializing an `AbsolutePathBuf`, a base path must be set using [AbsolutePathBufGuard::new]. If no base path is set, the deserialization will fail unless the path being deserialized is already absolute."
                        }
                      },
                      "transparent_background": {
                        "type": "boolean",
                        "description": "Whether the output should have a transparent background. Defaults to false."
                      }
                    },
                    "required": ["prompt"],
                    "additionalProperties": false
                  },
                  "strict": false
                }
              ]
            }
            """)!;
        expected["tools"]![0]!["description"] = registration.Definition.Description;
        var actual = Assert.Single(request["tools"]!.AsArray())!;
        Assert.Equal(expected.ToJsonString(), actual.ToJsonString());
    }

    [Fact]
    public async Task Invoke_GeneratesSavesAndReturnsImageWithSavedPathHint()
    {
        var image = CreatePng(2);
        var provider = new FakeImageProvider { Result = image, ImagegenRequestId = "req_1", GenerationId = "gen_1" };
        var config = OpenAIConfig();
        config.Tools.ImageGeneration.Model = "gpt-image-test";
        var runtime = await CreateRuntimeAsync(config, provider);

        var result = await runtime.InvokeAsync(
            CreateInvocationContext("call_1"),
            new JsonObject { ["prompt"] = " a lighthouse ", ["transparent_background"] = true });

        Assert.True(result.Success, result.Content);
        var request = Assert.Single(provider.Requests);
        Assert.Equal("gpt-image-test", request.Model);
        Assert.Equal("a lighthouse", request.Prompt);
        Assert.True(request.TransparentBackground);
        Assert.Empty(request.ReferenceImageUrls);
        Assert.Equal("turn_1", request.TurnId);

        var savedPath = Path.Combine(DataPath, "generated_images", "thread_1", "call_1.png");
        Assert.Equal(image, await File.ReadAllBytesAsync(savedPath));
        var output = result.ContentItems!;
        Assert.Equal(image, Assert.IsType<DataContent>(output[0]).Data.ToArray());
        Assert.Equal("image/png", ((DataContent)output[0]).MediaType);
        var hint = Assert.IsType<TextContent>(output[1]).Text;
        Assert.StartsWith(
            $"Generated images are saved to {Path.GetDirectoryName(savedPath)} as {savedPath} by default.",
            hint,
            StringComparison.Ordinal);
        Assert.Equal(savedPath, result.Meta!.Value.GetProperty("savedPath").GetString());
        var item = ImageGenerationProjection.Completed(ImageGenerationProjection.Started("call_1", null), result);
        Assert.Equal("req_1", item.ImagegenRequestId);
        Assert.Equal("gen_1", item.GenerationId);
    }

    [Fact]
    public async Task Invoke_ReferencedImagePathsSendsEditWithDataUrls()
    {
        var reference = CreatePng(3);
        var referencePath = Path.Combine(_root, "reference.png");
        await File.WriteAllBytesAsync(referencePath, reference);
        var provider = new FakeImageProvider();
        var runtime = await CreateRuntimeAsync(OpenAIConfig(), provider);

        var result = await runtime.InvokeAsync(
            CreateInvocationContext("call_edit"),
            new JsonObject
            {
                ["prompt"] = "make it blue",
                ["referenced_image_paths"] = new JsonArray(referencePath)
            });

        Assert.True(result.Success, result.Content);
        var url = Assert.Single(Assert.Single(provider.Requests).ReferenceImageUrls);
        Assert.Equal("data:image/png;base64," + Convert.ToBase64String(reference), url);
    }

    [Fact]
    public async Task CollectRecentImages_TakesNewestImagesInChronologicalOrder()
    {
        var userImage = CreatePng(1);
        var toolImage = CreatePng(2);
        var generatedImage = CreatePng(3);
        var latestUserImage = CreatePng(4);
        var runtime = await CreateRuntimeAsync(OpenAIConfig(), new FakeImageProvider());
        ChatMessage[] messages =
        [
            new(ChatRole.User, [new TextContent("first"), new DataContent(userImage, "image/png")]),
            new(ChatRole.Tool, [new FunctionResultContent("call_read", (IList<AIContent>)
                [new TextContent("Image: a.png"), new DataContent(toolImage, "image/png")])]),
            new(ChatRole.Tool, [new FunctionResultContent("call_gen", (IList<AIContent>)
                [new DataContent(generatedImage, "image/png")])]),
            new(ChatRole.User, [new DataContent(latestUserImage, "image/png")])
        ];

        var recent = runtime.CollectRecentImages(messages, 3);
        var tooMany = runtime.CollectRecentImages(messages, 5);

        Assert.Null(recent.Error);
        Assert.Equal(
            new[] { toolImage, generatedImage, latestUserImage }.Select(DataUrl),
            recent.Urls);
        Assert.Equal("requested the last 5 conversation images, but only 4 were available", tooMany.Error);
    }

    [Theory]
    [InlineData("""{"prompt":"x","size":"1024x1024"}""", "unknown field `size`")]
    [InlineData("""{"prompt":"x","referenced_image_paths":["/a.png"],"num_last_images_to_include":1}""", "provide only one of")]
    [InlineData("""{"prompt":"x","num_last_images_to_include":6}""", "between 1 and 5")]
    public async Task Invoke_InvalidArgumentsReturnModelVisibleError(string arguments, string expected)
    {
        var provider = new FakeImageProvider();
        var runtime = await CreateRuntimeAsync(OpenAIConfig(), provider);

        var result = await runtime.InvokeAsync(
            CreateInvocationContext("call_bad"),
            JsonNode.Parse(arguments)!.AsObject());

        Assert.False(result.Success);
        Assert.Contains(expected, result.Content, StringComparison.Ordinal);
        Assert.Empty(provider.Requests);
    }

    [Fact]
    public async Task Invoke_ApiFailureReturnsErrorTextToModel()
    {
        var provider = new FakeImageProvider
        {
            Error = new ProviderImageException("Images API request failed with HTTP 429: image usage limit reached", "req_failed")
        };
        var runtime = await CreateRuntimeAsync(OpenAIConfig(), provider);

        var result = await runtime.InvokeAsync(CreateInvocationContext("call_fail"), new JsonObject { ["prompt"] = "x" });

        Assert.False(result.Success);
        Assert.Equal(ImageGenerationToolRuntime.FailedErrorCode, result.Error?.Code);
        Assert.Equal(
            "image generation failed: Images API request failed with HTTP 429: image usage limit reached",
            result.Content);
        Assert.False(Directory.Exists(Path.Combine(DataPath, "generated_images")));
        var item = ImageGenerationProjection.Completed(ImageGenerationProjection.Started("call_fail", null), result);
        Assert.Equal("failed", item.Status);
        Assert.Equal("req_failed", item.ImagegenRequestId);
    }

    private async Task<ImageGenerationToolRuntime> CreateRuntimeAsync(AppConfig config, FakeImageProvider provider)
    {
        var registration = Assert.Single(await CreateSource(config, provider).GetRegistrationsAsync(CreatePlanningContext()));
        return Assert.IsType<ImageGenerationToolRuntime>(registration.Binding.Runtime);
    }

    private static ImageGenerationToolSource CreateSource(AppConfig config, FakeImageProvider provider) =>
        new(config, new ChatClientRegistry(provider), new AutoApproveApprovalService(), new PathBlacklist([]));

    private ToolPlanningContext CreatePlanningContext() =>
        new("thread_1", "turn_1", _root, DataPath, "agent", null, null, 1);

    private ToolInvocationContext CreateInvocationContext(string callId) =>
        new("thread_1", "turn_1", callId, ToolInvocationAudience.Model,
            new ToolName("image_gen", "imagegen"),
            new ToolDefinitionId(ToolSourceKind.CoreNative, "image-generation", new SourceToolId("imagegen")),
            new RuntimeBindingId("native:image-generation:imagegen:1"), 1, DateTimeOffset.UtcNow,
            WorkspacePath: _root);

    private static string DataUrl(byte[] bytes) => "data:image/png;base64," + Convert.ToBase64String(bytes);

    private static byte[] CreatePng(int size)
    {
        using var image = new Image<Rgba32>(size, size, new Rgba32(10, 20, 30, 255));
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    private static AppConfig Disabled(AppConfig config)
    {
        config.Tools.ImageGeneration.Enabled = false;
        return config;
    }

    private static AppConfig OpenAIConfig(
        string? endpoint = null,
        string protocol = ModelProviderProtocols.OpenAIResponses,
        string apiKey = "sk-test",
        bool? supportsImageGeneration = null,
        string authMethod = ModelProviderAuthMethods.ApiKey)
    {
        var config = new AppConfig
        {
            ProviderId = "openai",
            ProviderPreferences = new() { ["openai"] = new ModelPreference { Model = "gpt-5" } }
        };
        config.Providers["openai"] = new AppConfig.ModelProviderConfig
        {
            Protocol = protocol,
            ApiKey = apiKey,
            EndPoint = endpoint ?? string.Empty,
            AuthMethod = authMethod,
            SupportsImageGeneration = supportsImageGeneration
        };
        return config;
    }

    private static AppConfig ChatGptOAuthConfig(bool? supportsImageGeneration = null)
    {
        var config = new AppConfig
        {
            ProviderId = "chatgpt",
            ProviderPreferences = new() { ["chatgpt"] = new ModelPreference { Model = "gpt-5" } }
        };
        config.Providers["chatgpt"] = new AppConfig.ModelProviderConfig
        {
            Protocol = ModelProviderProtocols.OpenAIResponses,
            AuthMethod = ModelProviderAuthMethods.ChatGptOAuth,
            SupportsImageGeneration = supportsImageGeneration
        };
        return config;
    }

    private sealed class FakeImageProvider : IModelProvider, IProviderImageGeneration
    {
        public List<ProviderImageRequest> Requests { get; } = [];

        public byte[] Result { get; init; } = CreatePng(1);

        public Exception? Error { get; init; }

        public string? ImagegenRequestId { get; init; }

        public string? GenerationId { get; init; }

        public IReadOnlyCollection<string> Protocols { get; } =
            [ModelProviderProtocols.OpenAIChatCompletions, ModelProviderProtocols.OpenAIResponses];

        public IChatClient CreateChatClient(EffectiveModelRuntime runtime) => throw new NotSupportedException();

        public Task<ProviderImageResult> GenerateImageAsync(
            EffectiveModelRuntime runtime,
            ProviderImageRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Error is null
                ? Task.FromResult(new ProviderImageResult(Result, ImagegenRequestId, GenerationId))
                : Task.FromException<ProviderImageResult>(Error);
        }
    }
}
