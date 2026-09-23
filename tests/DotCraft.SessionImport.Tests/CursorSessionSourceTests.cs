namespace DotCraft.SessionImport.Tests;

public sealed class CursorSessionSourceTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly string _workspace;
    private readonly string _root;
    private readonly string _slug;

    public CursorSessionSourceTests()
    {
        _workspace = _temp.CreateDirectory("app");
        _root = _temp.CreateDirectory("cursor");
        _slug = WorkspacePathMatcher.CursorSlug(_workspace);
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void SlugReplacesEveryNonAlphanumericRunWithOneDash()
    {
        Assert.Equal("c-work-My-Proj", WorkspacePathMatcher.CursorSlug(@"c:\work\My Proj\"));
        Assert.Equal("srv-app-v2", WorkspacePathMatcher.CursorSlug("/srv/app.-v2"));
    }

    [Fact]
    public async Task ProjectNamesMustEncodeTheWorkspaceOrAnExistingSubfolder()
    {
        var nested = _temp.CreateDirectory("app", "client", "src");
        var hyphenated = _temp.CreateDirectory("app", "my-lib");
        var sibling = _temp.CreateDirectory("app-tools");
        Transcript(_slug, "root", nested: true);
        Transcript(WorkspacePathMatcher.CursorSlug(nested), "nested", nested: false);
        Transcript(WorkspacePathMatcher.CursorSlug(hyphenated), "hyphenated", nested: true);
        Transcript(WorkspacePathMatcher.CursorSlug(sibling), "sibling", nested: true);
        Transcript($"{_slug}-docs", "missing-folder", nested: true);
        Transcript("unrelated-project", "unrelated", nested: true);
        Transcript(_slug, "external", nested: true);
        Jsonl.Write(
            Path.Combine(_root, "projects", _slug, "agent-transcripts", "root", "subagents", "child.jsonl"),
            UserRecord("<user_query>child</user_query>"));

        var candidates = await new CursorSessionSource(_root).EnumerateCandidatesAsync(Scopes.For(_workspace, externalCliSessionIds: "external"));

        Assert.Equal(
            new[] { ("hyphenated", hyphenated), ("nested", nested), ("root", _workspace) },
            candidates.Select(static candidate => (candidate.SourceId, candidate.Cwd)).OrderBy(static entry => entry.SourceId, StringComparer.Ordinal));
    }

    [Fact]
    public async Task ConvertsTranscriptWithWrapperStrippingAndFileTimes()
    {
        var modifiedAt = new DateTimeOffset(2026, 9, 21, 8, 30, 0, TimeSpan.Zero);
        var path = Path.Combine(_root, "projects", _slug, "agent-transcripts", "composer-1", "composer-1.jsonl");
        Jsonl.WriteText(
            path,
            Jsonl.Lines(
                UserRecord("<timestamp>Monday</timestamp>\n<user_query>\nAdd a parser test\n</user_query>"),
                new
                {
                    role = "assistant",
                    message = new
                    {
                        content = new object[]
                        {
                            new { type = "text", text = "Adding it." },
                            new { type = "tool_use", name = "Write", input = new { file_path = "ParserTests.cs", contents = "..." } }
                        }
                    }
                },
                new { type = "turn_ended", status = "success" },
                UserRecord("<image_files>shot.png</image_files>\n<user_query>Keep this wrapper</user_query>"))
            + Jsonl.Line(new { role = "assistant", message = new { content = new[] { new { type = "text", text = "Done" } } } }),
            modifiedAt);
        var source = new CursorSessionSource(_root);
        var candidate = Assert.Single(await source.EnumerateCandidatesAsync(Scopes.For(_workspace, maxAge: TimeSpan.FromDays(3650))));

        var session = Assert.IsType<ImportedSession>(await source.LoadAsync(candidate));

        Assert.Equal("Add a parser test", session.Title);
        Assert.Collection(
            session.Turns,
            first =>
            {
                Assert.Equal("Add a parser test", first.UserText);
                Assert.Equal(
                    new[] { "Adding it.\n\n[external_agent_tool_call: Write]\nfile: ParserTests.cs\n[/external_agent_tool_call]" },
                    first.AgentTexts);
            },
            second =>
            {
                Assert.Equal("<image_files>shot.png</image_files>\n<user_query>Keep this wrapper</user_query>", second.UserText);
                Assert.Equal(new[] { "Done" }, second.AgentTexts);
            });
        Assert.All(session.Turns, turn =>
        {
            Assert.Equal(modifiedAt, turn.StartedAt);
            Assert.Equal(modifiedAt, turn.CompletedAt);
        });
    }

    [Fact]
    public async Task TitleSkipsLeadingAttachmentBlocks()
    {
        Jsonl.Write(
            Path.Combine(_root, "projects", _slug, "agent-transcripts", "composer-2", "composer-2.jsonl"),
            UserRecord("<manually_attached_skills>\nskill body\n</manually_attached_skills>\n<user_query>\nFix the flaky test\n</user_query>"),
            new { role = "assistant", message = new { content = new[] { new { type = "text", text = "Fixed" } } } });
        var source = new CursorSessionSource(_root);
        var candidate = Assert.Single(await source.EnumerateCandidatesAsync(Scopes.For(_workspace, maxAge: TimeSpan.FromDays(3650))));

        var session = Assert.IsType<ImportedSession>(await source.LoadAsync(candidate));

        Assert.Equal("Fix the flaky test", session.Title);
    }

    private void Transcript(string project, string composerId, bool nested)
    {
        var directory = Path.Combine(_root, "projects", project, "agent-transcripts");
        var path = nested
            ? Path.Combine(directory, composerId, $"{composerId}.jsonl")
            : Path.Combine(directory, $"{composerId}.jsonl");
        Jsonl.Write(path, UserRecord($"<user_query>{composerId}</user_query>"));
    }

    private static object UserRecord(string text) =>
        new { role = "user", message = new { content = new[] { new { type = "text", text } } } };
}
