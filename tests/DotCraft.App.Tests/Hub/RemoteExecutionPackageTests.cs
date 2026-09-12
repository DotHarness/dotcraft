using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using DotCraft.Hub;
using Xunit;

namespace DotCraft.Tests.Hub;

public sealed class RemoteExecutionPackageTests
{
    [Fact]
    public async Task CandidateHarnessPackageTransfersOverSatellite_WithoutApplicationInstallation()
    {
        var fixture = typeof(RemoteExecutionPackageTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(item => item.Key == "RemoteExecutionPackageProject").Value!;
        var project = Path.GetFullPath(fixture, AppContext.BaseDirectory);
        var root = Path.Combine(Path.GetTempPath(), "dotcraft-package-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var version = XDocument.Load(project).Descendants("Version").Single().Value;
            var packages = Path.Combine(root, "packages");
            await RunAsync(Path.GetDirectoryName(project)!, ["pack", project, "-c", "Release", "-o", packages, "--disable-build-servers"]);
            using (var package = ZipFile.OpenRead(Path.Combine(packages, $"DotCraft.Harness.{version}.nupkg")))
                foreach (var assembly in new[] { "DotCraft.Core", "DotCraft.Runtime", "DotCraft.Protocol", "DotCraft.RemoteTools", "DotCraft.Harness" })
                    Assert.Single(package.Entries, entry => entry.FullName == $"lib/net10.0/{assembly}.dll");

            var consumer = Path.Combine(root, "consumer");
            Directory.CreateDirectory(consumer);
            File.Copy(Path.Combine(AppContext.BaseDirectory, "RemoteExecutionConsumer.cs.txt"), Path.Combine(consumer, "Program.cs"));
            new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                new XElement("PropertyGroup", new XElement("OutputType", "Exe"), new XElement("TargetFramework", "net10.0"),
                    new XElement("ImplicitUsings", "enable"), new XElement("Nullable", "enable")),
                new XElement("ItemGroup", new XElement("PackageReference", new XAttribute("Include", "DotCraft.Harness"), new XAttribute("Version", version)))))
                .Save(Path.Combine(consumer, "Consumer.csproj"));
            new XDocument(new XElement("configuration",
                new XElement("packageSources", new XElement("clear"),
                    new XElement("add", new XAttribute("key", "candidate"), new XAttribute("value", packages)),
                    new XElement("add", new XAttribute("key", "nuget.org"), new XAttribute("value", "https://api.nuget.org/v3/index.json"))),
                new XElement("packageSourceMapping",
                    new XElement("packageSource", new XAttribute("key", "candidate"), new XElement("package", new XAttribute("pattern", "DotCraft.Harness"))),
                    new XElement("packageSource", new XAttribute("key", "nuget.org"), new XElement("package", new XAttribute("pattern", "*"))))))
                .Save(Path.Combine(consumer, "NuGet.Config"));
            var cache = Path.Combine(root, "cache");
            await RunAsync(consumer, ["restore", "--configfile", "NuGet.Config", "--packages", cache, "--disable-build-servers"]);
            await RunAsync(consumer, ["build", "-c", "Release", "--no-restore", "--disable-build-servers"]);

            await using var scenario = await SatelliteScenario.StartAsync(Path.Combine(root, "device"), heartbeatInterval: TimeSpan.FromMilliseconds(100));
            string Bridge() => new UriBuilder(scenario.Hub.ApiBaseUrl)
            {
                Scheme = "ws", Path = $"/v1/satellites/{scenario.PeerId}/bridge", Query = "session=" + Guid.NewGuid().ToString("N")
            }.Uri.ToString();
            var input = JsonSerializer.Serialize(new
            {
                FirstUri = Bridge(), SecondUri = Bridge(), Credential = scenario.Hub.Token,
                HostId = scenario.PeerId, WorkspaceId = scenario.WorkspaceId, LocalRoot = Path.Combine(root, "local")
            });
            var output = await RunAsync(consumer, [Path.Combine(consumer, "bin", "Release", "net10.0", "Consumer.dll")], input);
            Assert.Contains("remote-execution-package-ok", output);
            await SatelliteBridgeEndToEndTests.WaitUntilAsync(async () =>
                (await scenario.Hub.GetAsync<HubSatelliteResponse[]>("/v1/satellites")).All(peer => peer.Workspaces.All(workspace => !workspace.Busy)));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static async Task<string> RunAsync(string directory, string[] arguments, string? input = null)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true
        };
        start.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (input is not null) await process.StandardInput.WriteLineAsync(input);
        process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); throw; }
        var text = await output + await error;
        Assert.True(process.ExitCode == 0, text);
        return text;
    }
}
