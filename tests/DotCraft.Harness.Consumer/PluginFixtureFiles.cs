using System.Text.Json;
using DotCraft.Plugins;

internal static class PluginFixtureFiles
{
    private const string AssemblyName = "DotCraft.Harness.PluginFixture";

    public static string InstallBinary(string workspace, string buildOutput)
    {
        var pluginRoot = Path.Combine(workspace, "binary-plugin");
        var lib = Path.Combine(pluginRoot, "lib");
        Directory.CreateDirectory(lib);
        foreach (var extension in new[] { ".dll", ".deps.json" })
            File.Copy(Path.Combine(buildOutput, AssemblyName + extension), Path.Combine(lib, AssemblyName + extension));
        WriteManifest(pluginRoot, "package.binary");
        return pluginRoot;
    }

    public static void CreateSourceProject(string workspace)
    {
        var projectRoot = Path.Combine(workspace, ".craft", "plugin-projects", "package.source");
        var sourceRoot = Path.Combine(projectRoot, "src");
        Directory.CreateDirectory(sourceRoot);
        var hostRoot = Path.GetDirectoryName(typeof(IDotCraftPlugin).Assembly.Location)!;
        File.Copy(Path.Combine(hostRoot, "Fixtures", "Plugin.cs"), Path.Combine(sourceRoot, "Plugin.cs"));
        WriteManifest(Path.Combine(projectRoot, "plugin"), "package.source");
    }

    private static void WriteManifest(string pluginRoot, string id)
    {
        var manifestRoot = Path.Combine(pluginRoot, ".craft-plugin");
        Directory.CreateDirectory(manifestRoot);
        File.WriteAllText(Path.Combine(manifestRoot, "plugin.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            id,
            version = "1.0.0",
            displayName = "Harness package smoke fixture",
            capabilities = new[] { "dotnet" },
            dotnet = new
            {
                minHostVersion = "0.0.0",
                entryAssembly = "./lib/" + AssemblyName + ".dll",
                entryType = "DotCraft.Harness.PluginFixture.Plugin"
            }
        }));
    }
}
