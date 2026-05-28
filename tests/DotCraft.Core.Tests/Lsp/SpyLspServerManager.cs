using System.Text.Json;
using DotCraft.Configuration;
using DotCraft.Hosting;
using DotCraft.Lsp;

namespace DotCraft.Tests.Lsp;

internal sealed class SpyLspServerManager(string workspacePath) : LspServerManager(
    new AppConfig
    {
        Tools = new AppConfig.ToolsConfig
        {
            Lsp = new AppConfig.LspToolsConfig { Enabled = true }
        }
    },
    new DotCraftPaths
    {
        WorkspacePath = workspacePath,
        CraftPath = Path.Combine(workspacePath, ".craft")
    })
{
    private readonly HashSet<string> _openFiles = new(StringComparer.OrdinalIgnoreCase);

    public List<(string FilePath, string Content)> OpenCalls { get; } = [];

    public List<(string FilePath, string Content)> ChangeCalls { get; } = [];

    public List<string> SaveCalls { get; } = [];

    public List<string> RequestMethods { get; } = [];

    public List<JsonElement> RequestParams { get; } = [];

    public Queue<JsonElement?> RequestResults { get; } = new();

    public override bool IsFileOpen(string filePath) => _openFiles.Contains(Path.GetFullPath(filePath));

    public override Task OpenFileAsync(string filePath, string content, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(filePath);
        _openFiles.Add(fullPath);
        OpenCalls.Add((fullPath, content));
        return Task.CompletedTask;
    }

    public override Task<JsonElement?> SendRequestAsync(
        string filePath,
        string method,
        object? @params,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        RequestMethods.Add(method);
        RequestParams.Add(JsonSerializer.SerializeToElement(@params));
        return Task.FromResult(RequestResults.Count > 0 ? RequestResults.Dequeue() : (JsonElement?)null);
    }

    public override Task ChangeFileAsync(string filePath, string content, CancellationToken cancellationToken = default)
    {
        ChangeCalls.Add((Path.GetFullPath(filePath), content));
        return Task.CompletedTask;
    }

    public override Task SaveFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        SaveCalls.Add(Path.GetFullPath(filePath));
        return Task.CompletedTask;
    }
}
