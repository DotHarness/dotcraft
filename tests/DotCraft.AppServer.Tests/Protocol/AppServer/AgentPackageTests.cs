using System.Text;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Agents.Packages;
using DotCraft.AppServer;
using DotCraft.Configuration;
using DotCraft.Skills;
using Methods = DotCraft.Protocol.AppServer.AppServerMethodNames;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AgentPackageTests : IDisposable
{
    private const string Document =
        "---\nname: Release captain\ndescription: Ships releases.\nskills:\n  preload:\n    - release-notes\n    - deploy-checklist\n---\n\nShip the release.\n";

    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"agent_packages_{Guid.NewGuid():N}");
    private readonly string _workspaceCraftPath;

    public AgentPackageTests()
    {
        _workspaceCraftPath = Path.Combine(_tempRoot, ".craft");
        Directory.CreateDirectory(_workspaceCraftPath);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
        }
    }

    [Fact]
    public async Task Import_InstallsBundledSkillIntoWorkspace_AndTheSameFileAgainFindsItInstalledAndTheNameTaken()
    {
        using var harness = CreateHarness();
        await harness.InitializeAsync(configChange: true);
        var file = PackageWithSkill("release-notes");

        var preview = await UploadAsync(harness, file, "release-captain.agent.zip");
        Assert.Equal("package", preview.GetProperty("kind").GetString());
        Assert.Equal("Release captain", preview.GetProperty("name").GetString());
        Assert.False(preview.GetProperty("nameTaken").GetBoolean());
        Assert.Empty(preview.GetProperty("problems").EnumerateArray());
        Assert.Equal("bundled", State(preview, "release-notes"));
        Assert.Equal(["deploy-checklist"], Strings(preview.GetProperty("unresolved").GetProperty("skills")));

        var commit = await SendAsync(harness, Methods.AgentProfileImportCommit, new
        {
            importId = preview.GetProperty("importId").GetString(),
            name = "Release captain",
            description = "Ships releases.",
            source = "workspace",
            packages = new[] { new { kind = "skill", name = "release-notes" } }
        });
        var profile = commit.GetProperty("result").GetProperty("profile");
        Assert.Equal("Release captain", profile.GetProperty("id").GetString());
        Assert.Equal("workspace", profile.GetProperty("source").GetString());
        Assert.True(profile.GetProperty("valid").GetBoolean());
        Assert.True(File.Exists(Path.Combine(_workspaceCraftPath, "skills", "release-notes", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(_workspaceCraftPath, "agents", AgentProfileName.FileName("Release captain"))));

        var again = await UploadAsync(harness, file, "release-captain.agent.zip");
        Assert.True(again.GetProperty("nameTaken").GetBoolean());
        Assert.Equal("installed", State(again, "release-notes"));

        var refused = await SendAsync(harness, Methods.AgentProfileImportCommit, new
        {
            importId = again.GetProperty("importId").GetString(),
            name = "Release captain",
            description = "Ships releases.",
            source = "workspace",
            packages = Array.Empty<object>()
        });
        Assert.Equal("agentNameTaken", refused.GetProperty("error").GetProperty("data").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Import_SavesADocumentAloneAsAUserProfileThatReadsBack()
    {
        using var harness = CreateHarness();
        await harness.InitializeAsync(configChange: true);

        var preview = await UploadAsync(harness, Encoding.UTF8.GetBytes(Document), "release-captain.md");
        Assert.Equal("markdown", preview.GetProperty("kind").GetString());
        Assert.Equal(["release-notes", "deploy-checklist"], Strings(preview.GetProperty("unresolved").GetProperty("skills")));

        var commit = await SendAsync(harness, Methods.AgentProfileImportCommit, new
        {
            importId = preview.GetProperty("importId").GetString(),
            name = "Personal captain",
            description = "Mine.",
            source = "user",
            packages = Array.Empty<object>()
        });
        Assert.Equal("user", commit.GetProperty("result").GetProperty("profile").GetProperty("source").GetString());
        var userData = Path.GetDirectoryName(harness.Monitor.Current.GlobalConfigPath)!;
        Assert.True(File.Exists(Path.Combine(userData, "agents", AgentProfileName.FileName("Personal captain"))));

        var read = await SendAsync(harness, Methods.AgentProfileRead, new { id = "Personal captain", source = "user" });
        var profile = read.GetProperty("result").GetProperty("profile");
        Assert.Equal("Mine.", profile.GetProperty("description").GetString());
        Assert.Contains("name: Personal captain\ndescription: Mine.\n", profile.GetProperty("rawContent").GetString());

        var expired = await SendAsync(harness, Methods.AgentProfileImportCommit, new
        {
            importId = preview.GetProperty("importId").GetString(),
            name = "Another captain",
            description = "Mine.",
            source = "user",
            packages = Array.Empty<object>()
        });
        Assert.Equal("importExpired", expired.GetProperty("error").GetProperty("data").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Import_RefusesAZipWithAnEntryOutsideIt()
    {
        using var harness = CreateHarness();
        await harness.InitializeAsync(configChange: true);
        var path = Path.Combine(_tempRoot, "escape.zip");
        using (var archive = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create))
        {
            using var stream = archive.CreateEntry("../agent.json").Open();
            stream.Write("{}"u8);
        }

        var response = await SendAsync(harness, Methods.AgentProfileImportUpload, new
        {
            fileName = "escape.zip",
            totalBytes = (int)new FileInfo(path).Length,
            offset = 0,
            dataBase64 = Convert.ToBase64String(File.ReadAllBytes(path))
        });

        Assert.Equal("agentPackageInvalid", response.GetProperty("error").GetProperty("data").GetProperty("code").GetString());
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(_workspaceCraftPath, "tmp", "agent-packages")));
    }

    [Fact]
    public async Task Export_PreselectsWhatTheProfileUsesAndWritesAFileTheReaderAccepts()
    {
        WriteSkill(Path.Combine(_workspaceCraftPath, "skills"), "release-notes");
        WriteSkill(Path.Combine(_workspaceCraftPath, "skills"), "unused");
        new AgentProfileStore(_workspaceCraftPath).Upsert("Release captain", "workspace", Document);
        using var harness = CreateHarness();
        await harness.InitializeAsync(configChange: true);

        var plan = (await SendAsync(harness, Methods.AgentProfileExportPlan, new { id = "Release captain", source = "workspace" }))
            .GetProperty("result");
        Assert.Equal("release-captain.agent.zip", plan.GetProperty("fileName").GetString());
        Assert.Equal(AgentPackageLimits.MaximumBytes, plan.GetProperty("maximumBytes").GetInt32());
        var packages = plan.GetProperty("packages").EnumerateArray()
            .ToDictionary(package => package.GetProperty("name").GetString()!, package => Strings(package.GetProperty("reasons")));
        Assert.Equal(["skill release-notes"], packages["release-notes"]);
        Assert.Empty(packages["unused"]);

        var bytes = new List<byte>();
        int total;
        do
        {
            var chunk = (await SendAsync(harness, Methods.AgentProfileExportRead, new
            {
                id = "Release captain",
                source = "workspace",
                packages = new[] { new { kind = "skill", name = "release-notes" } },
                offset = bytes.Count
            })).GetProperty("result");
            total = chunk.GetProperty("totalBytes").GetInt32();
            bytes.AddRange(Convert.FromBase64String(chunk.GetProperty("dataBase64").GetString()!));
        } while (bytes.Count < total);

        var path = Path.Combine(_tempRoot, "exported.agent.zip");
        File.WriteAllBytes(path, bytes.ToArray());
        var content = AgentPackageReader.Read(path);
        Assert.Equal(Document, content.Document);
        var package = Assert.Single(content.Manifest.Packages);
        Assert.Equal(("skill", "release-notes", "packages/skill-release-notes.zip"), (package.Kind, package.Name, package.File));
    }

    private AppServerTestHarness CreateHarness()
    {
        return new AppServerTestHarness(
            workspaceCraftPath: _workspaceCraftPath,
            skillsLoader: new SkillsLoader(_workspaceCraftPath),
            appConfigMonitor: new AppConfigMonitor(new AppConfig()),
            builtInPluginSourceRoots: []);
    }

    private byte[] PackageWithSkill(string name)
    {
        var source = Path.Combine(_tempRoot, "source");
        WriteSkill(source, name);
        var path = Path.Combine(_tempRoot, $"{Guid.NewGuid():N}.agent.zip");
        AgentPackageWriter.Write(
            path,
            "Release captain",
            "Ships releases.",
            Document,
            [new AgentPackageItem(new AgentPackageEntry("skill", name, name, null, "", false, [name], [], null, null), Path.Combine(source, name))],
            Path.Combine(_tempRoot, "writer"),
            DateTimeOffset.UnixEpoch);
        return File.ReadAllBytes(path);
    }

    private static void WriteSkill(string root, string name)
    {
        var directory = Path.Combine(root, name);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "SKILL.md"), $"---\nname: {name}\ndescription: Does {name}.\n---\n\nDo it.\n");
    }

    private static async Task<JsonElement> UploadAsync(AppServerTestHarness harness, byte[] file, string fileName)
    {
        const int chunkBytes = 1024 * 1024;
        string? importId = null;
        for (var offset = 0; ; offset += chunkBytes)
        {
            var response = await SendAsync(harness, Methods.AgentProfileImportUpload, new
            {
                importId,
                fileName,
                totalBytes = file.Length,
                offset,
                dataBase64 = Convert.ToBase64String(file, offset, Math.Min(chunkBytes, file.Length - offset))
            });
            var result = response.GetProperty("result");
            importId = result.GetProperty("importId").GetString();
            if (result.TryGetProperty("preview", out var preview))
                return preview;
        }
    }

    private static async Task<JsonElement> SendAsync(AppServerTestHarness harness, string method, object parameters)
    {
        harness.Transport.DrainSent();
        await harness.ExecuteRequestAsync(harness.BuildRequest(method, parameters));
        while (true)
        {
            using var message = await harness.Transport.ReadNextSentAsync();
            if (message.RootElement.TryGetProperty("id", out _))
                return message.RootElement.Clone();
        }
    }

    private static string? State(JsonElement preview, string name) =>
        preview.GetProperty("packages").EnumerateArray()
            .Single(package => package.GetProperty("name").GetString() == name)
            .GetProperty("state").GetString();

    private static string[] Strings(JsonElement array) =>
        array.EnumerateArray().Select(item => item.GetString()!).ToArray();
}
