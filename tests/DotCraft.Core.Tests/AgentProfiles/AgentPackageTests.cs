using System.IO.Compression;
using System.Text;
using System.Text.Json;
using DotCraft.Agents.Packages;
using Xunit;

namespace DotCraft.Tests.Agents;

public sealed class AgentPackageTests : IDisposable
{
    private const string Document =
        "---\nname: Release captain\ndescription: Ships releases.\nskills:\n  preload:\n    - release-notes\n---\n\nShip the release.\n";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"agent_package_{Guid.NewGuid():N}");

    public AgentPackageTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }

    [Fact]
    public void Write_ThenRead_CarriesBundledSkillAndMarketplaceReference()
    {
        var skill = Path.Combine(_root, "skills", "release-notes");
        Directory.CreateDirectory(skill);
        File.WriteAllText(Path.Combine(skill, "SKILL.md"), Skill("release-notes"));
        File.WriteAllText(Path.Combine(skill, ".dotcraft-skill.json"), "{}");
        var marketplace = new AgentPackageMarketplace("tools", "git", "https://example.com/tools.git", "main", ".craft-plugin/marketplace.json", []);
        var path = Path.Combine(_root, "release-captain.agent.zip");

        AgentPackageWriter.Write(
            path,
            "Release captain",
            "Ships releases.",
            Document,
            [
                new AgentPackageItem(Entry(AgentPackageKinds.Skill, "release-notes"), skill),
                new AgentPackageItem(Entry(AgentPackageKinds.Plugin, "deploy-tools") with { Marketplace = marketplace }, null)
            ],
            Path.Combine(_root, "staging"),
            DateTimeOffset.UnixEpoch);

        var content = AgentPackageReader.Read(path);
        Assert.False(content.Markdown);
        Assert.Equal("Release captain", content.Manifest.Name);
        Assert.Equal(Document, content.Document);
        var bundled = Assert.Single(content.Manifest.Packages, package => package.Kind == AgentPackageKinds.Skill);
        Assert.Equal("packages/skill-release-notes.zip", bundled.File);
        Assert.Equal(64, bundled.Sha256.Length);
        var referenced = Assert.Single(content.Manifest.Packages, package => package.Kind == AgentPackageKinds.Plugin);
        Assert.Null(referenced.File);
        Assert.Equal(("tools", "git", "https://example.com/tools.git", "main"),
            (referenced.Marketplace!.Name, referenced.Marketplace.SourceKind, referenced.Marketplace.Source, referenced.Marketplace.Ref));

        var extracted = Path.Combine(_root, "extracted");
        AgentPackageReader.ExtractPackage(path, bundled, extracted);
        Assert.Equal(Skill("release-notes"), File.ReadAllText(Path.Combine(extracted, "SKILL.md")));
        Assert.False(File.Exists(Path.Combine(extracted, ".dotcraft-skill.json")));
    }

    [Fact]
    public void Read_RefusesAnEntryOutsideTheZip()
    {
        var path = Zip(new Dictionary<string, byte[]>
        {
            ["agent.json"] = Manifest(),
            ["profile.md"] = Encoding.UTF8.GetBytes(Document),
            ["../escape.txt"] = "outside"u8.ToArray()
        });

        var error = Assert.Throws<AgentPackageException>(() => AgentPackageReader.Read(path));
        Assert.Equal(AgentPackageException.InvalidCode, error.Code);
    }

    [Fact]
    public void Read_RefusesAnotherFormat()
    {
        var path = Zip(new Dictionary<string, byte[]>
        {
            ["agent.json"] = Manifest(format: "dotcraft-agent/2"),
            ["profile.md"] = Encoding.UTF8.GetBytes(Document)
        });

        var error = Assert.Throws<AgentPackageException>(() => AgentPackageReader.Read(path));
        Assert.Equal(AgentPackageException.InvalidCode, error.Code);
    }

    [Fact]
    public void Read_RefusesAPackageThatIsNotWhatItClaims()
    {
        var path = Zip(new Dictionary<string, byte[]>
        {
            ["agent.json"] = Manifest(("skill", "release-notes")),
            ["profile.md"] = Encoding.UTF8.GetBytes(Document),
            ["packages/skill-release-notes.zip"] = PackageZip(new Dictionary<string, byte[]>
            {
                ["SKILL.md"] = Encoding.UTF8.GetBytes(Skill("something-else"))
            })
        });

        var error = Assert.Throws<AgentPackageException>(() => AgentPackageReader.Read(path));
        Assert.Equal(AgentPackageException.InvalidCode, error.Code);
    }

    [Fact]
    public void Read_TakesAMarkdownFileAsTheDocumentAlone()
    {
        var path = Path.Combine(_root, "release-captain.md");
        File.WriteAllText(path, Document.Replace("\n", "\r\n"));

        var content = AgentPackageReader.Read(path);

        Assert.True(content.Markdown);
        Assert.Equal("Release captain", content.Manifest.Name);
        Assert.Equal("Ships releases.", content.Manifest.Description);
        Assert.Empty(content.Manifest.Packages);
        Assert.Equal(Document, content.Document);
    }

    [Fact]
    public void Read_AcceptsAPackageWrittenInUniverseLayout()
    {
        var path = Zip(new Dictionary<string, byte[]>
        {
            ["agent.json"] = Manifest(("skill", "release-notes"), ("plugin", "release-tools")),
            ["profile.md"] = Encoding.UTF8.GetBytes(Document),
            ["packages/skill-release-notes.zip"] = PackageZip(new Dictionary<string, byte[]>
            {
                ["SKILL.md"] = Encoding.UTF8.GetBytes(Skill("release-notes"))
            }),
            ["packages/plugin-release-tools.zip"] = PackageZip(new Dictionary<string, byte[]>
            {
                [".craft-plugin/plugin.json"] = """{ "schemaVersion": 1, "id": "release-tools", "displayName": "Release tools" }"""u8.ToArray(),
                ["skills/probe/SKILL.md"] = Encoding.UTF8.GetBytes(Skill("probe"))
            })
        });

        var content = AgentPackageReader.Read(path);
        Assert.Equal(["release-notes", "release-tools"], content.Manifest.Packages.Select(package => package.Name));

        var extracted = Path.Combine(_root, "plugin");
        AgentPackageReader.ExtractPackage(path, content.Manifest.Packages[1], extracted);
        Assert.True(File.Exists(Path.Combine(extracted, ".craft-plugin", "plugin.json")));
        Assert.True(File.Exists(Path.Combine(extracted, "skills", "probe", "SKILL.md")));
    }

    [Fact]
    public void WithIdentity_RewritesOnlyNameAndDescription()
    {
        const string document =
            "---\nname: Old name\ndescription: >\n  Folded\n\n  description\nskills:\n  preload:\n    - release-notes\n---\n\nBody: keep.\n";

        var rewritten = AgentPackageDocument.WithIdentity(document, "New: name", "Ships releases.");

        Assert.Equal(
            "---\nname: \"New: name\"\ndescription: Ships releases.\nskills:\n  preload:\n    - release-notes\n---\n\nBody: keep.\n",
            rewritten);
        Assert.Equal("New: name", AgentPackageDocument.ReadName(rewritten));
    }

    private static AgentPackageEntry Entry(string kind, string name) =>
        new(kind, name, name, null, string.Empty, false, kind == AgentPackageKinds.Skill ? [name] : [], [], null, null);

    private static string Skill(string name) => $"---\nname: {name}\ndescription: Does {name}.\n---\n\nDo it.\n";

    private string Zip(Dictionary<string, byte[]> files)
    {
        var path = Path.Combine(_root, $"{Guid.NewGuid():N}.zip");
        File.WriteAllBytes(path, PackageZip(files));
        return path;
    }

    private static byte[] PackageZip(Dictionary<string, byte[]> files)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, bytes) in files)
            {
                using var stream = archive.CreateEntry(name).Open();
                stream.Write(bytes);
            }
        }
        return output.ToArray();
    }

    private static byte[] Manifest(params (string Kind, string Name)[] packages) => Manifest("dotcraft-agent/1", packages);

    private static byte[] Manifest(string format, params (string Kind, string Name)[] packages) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            format,
            name = "Release captain",
            description = "Ships releases.",
            profile = "profile.md",
            exportedAt = DateTimeOffset.UnixEpoch,
            packages = packages.Select(package => new
            {
                kind = package.Kind,
                name = package.Name,
                displayName = package.Name,
                version = "1.0.0",
                sha256 = "",
                dotnet = false,
                skills = Array.Empty<string>(),
                mcpServers = Array.Empty<string>(),
                marketplace = (object?)null,
                file = $"packages/{package.Kind}-{package.Name}.zip"
            })
        }));
}
