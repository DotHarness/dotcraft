using System.Text.Json.Nodes;
using DotCraft.Configuration;

namespace DotCraft.Tests.Configuration;

internal sealed class ConfigurationServiceFixture : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dotcraft-config-" + Guid.NewGuid().ToString("N"));

    public ConfigurationServiceFixture(string? user = null, string? workspace = null)
    {
        UserPath = Path.Combine(_root, "home", ".craft", "config.json");
        WorkspacePath = Path.Combine(_root, "workspace", ".craft", "config.json");
        if (user != null)
            WriteFile(UserPath, user);
        if (workspace != null)
            WriteFile(WorkspacePath, workspace);

        Monitor = new AppConfigMonitor(AppConfig.LoadWithGlobalFallback(WorkspacePath, UserPath));
        Monitor.Changed += (_, args) => Events.Add(args);
        Service = new ConfigurationService(
            ConfigSchemaRegistrations.CreateDescriptorRegistry(),
            Monitor,
            UserPath,
            WorkspacePath);
    }

    public string UserPath { get; }

    public string WorkspacePath { get; }

    public AppConfigMonitor Monitor { get; }

    public ConfigurationService Service { get; }

    public List<AppConfigChangedEventArgs> Events { get; } = [];

    public string Scratch(string name) => Path.Combine(_root, name);

    public Task<ConfigWriteResult> WriteAsync(
        string keyPath,
        string? valueJson,
        ConfigMergeStrategy strategy = ConfigMergeStrategy.Replace,
        string? filePath = null,
        string? expectedVersion = null) =>
        Service.WriteAsync([Edit(keyPath, valueJson, strategy)], filePath, expectedVersion, "test");

    public static ConfigEdit Edit(
        string keyPath,
        string? valueJson,
        ConfigMergeStrategy strategy = ConfigMergeStrategy.Replace) =>
        new(keyPath, valueJson == null ? null : JsonNode.Parse(valueJson), strategy);

    public static JsonObject ReadFile(string path) => (JsonObject)JsonNode.Parse(File.ReadAllText(path))!;

    public static void WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
