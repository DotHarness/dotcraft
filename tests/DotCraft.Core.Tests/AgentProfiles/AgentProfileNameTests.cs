using System.Text.Json;
using DotCraft.Agents;
using Xunit;

namespace DotCraft.Tests.Agents;

public sealed class AgentProfileNameTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "profile_names_" + Guid.NewGuid().ToString("N"));
    private AgentProfileStore Store => new(root, null);
    private static string Document(string name) => $"---\nname: {JsonSerializer.Serialize(name)}\ndescription: Test profile\n---\nBody\n";

    [Theory]
    [InlineData("Night Shift")]
    [InlineData("夜班助手")]
    [InlineData("../外部/文件: \"报告\" #1")]
    [InlineData("CON")]
    [InlineData("true")]
    public void Name_round_trips_without_becoming_a_path(string name)
    {
        var saved = Store.Upsert(name, AgentProfileSources.Workspace, Document(name));
        Assert.Equal(name, Store.Read(name).Name);
        Assert.Equal(Path.Combine(root, "agents", AgentProfileName.FileName(name)), saved.Path);
        Assert.Equal(name, AgentProfileDraftEditor.Parse(AgentProfileDraftEditor.ToMarkdown(new() { Name = name, Description = "Test" })).Name);
        Store.Remove(name, AgentProfileSources.Workspace);
        Assert.False(File.Exists(saved.Path));
    }

    [Fact]
    public void Unicode_is_canonical_but_case_is_significant()
    {
        Store.Upsert("  Cafe\u0301  ", AgentProfileSources.Workspace, Document("Café"));
        Store.Upsert("café", AgentProfileSources.Workspace, Document("café"));
        Assert.Equal("Café", Store.Read("Cafe\u0301").Name);
        Assert.Equal(2, Store.List(AgentProfileSources.Workspace).Count);
        Assert.True(AgentProfileName.IsValid(string.Concat(Enumerable.Repeat("😀", 240))));
        Assert.False(AgentProfileName.IsValid(new string('a', 241)));
        Assert.False(AgentProfileName.IsValid("name\n"));
    }

    [Fact]
    public void Handwritten_paths_are_updated_and_renames_preserve_conflicting_profiles()
    {
        Directory.CreateDirectory(Path.Combine(root, "agents"));
        var original = Path.Combine(root, "agents", "custom-file.md");
        File.WriteAllText(original, Document("原名称"));
        Assert.Equal(original, Store.Upsert("原名称", AgentProfileSources.Workspace, Document("原名称")).Path);
        Store.Upsert("目标", AgentProfileSources.Workspace, Document("目标"));
        Assert.Equal(AgentProfileErrorKind.Conflict, Assert.Throws<AgentProfileException>(() =>
            Store.Upsert("目标", AgentProfileSources.Workspace, Document("目标"), "原名称")).Kind);
        Assert.True(File.Exists(original));
        Store.Upsert("新名称", AgentProfileSources.Workspace, Document("新名称"), "原名称");
        Assert.False(File.Exists(original));
        Assert.Equal("新名称", Store.Read("新名称").Name);
    }

    [Fact]
    public void Failed_rename_keeps_the_original_document()
    {
        var original = Store.Upsert("Original", AgentProfileSources.Workspace, Document("Original"));
        var audit = Path.Combine(root, "agents", "audit.jsonl");
        using (File.Open(audit, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.Throws<IOException>(() => Store.Upsert("Renamed", AgentProfileSources.Workspace, Document("Renamed"), "Original"));
        Assert.True(File.Exists(original.Path));
        Assert.Equal("Original", Store.Read("Original").Name);
        Assert.DoesNotContain(Store.List(AgentProfileSources.Workspace), entry => entry.Name == "Renamed");
    }

    [Fact]
    public void Duplicate_names_are_diagnostics_and_cannot_be_overwritten()
    {
        Directory.CreateDirectory(Path.Combine(root, "agents"));
        File.WriteAllText(Path.Combine(root, "agents", "a.md"), Document("重复"));
        File.WriteAllText(Path.Combine(root, "agents", "b.md"), Document("重复"));
        Assert.All(Store.List(AgentProfileSources.Workspace), entry => Assert.False(entry.Valid));
        Assert.Equal(AgentProfileErrorKind.Conflict, Assert.Throws<AgentProfileException>(() =>
            Store.Upsert("重复", AgentProfileSources.Workspace, Document("重复"))).Kind);
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
