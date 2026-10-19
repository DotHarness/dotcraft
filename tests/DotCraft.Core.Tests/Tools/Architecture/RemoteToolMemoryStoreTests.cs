using DotCraft.Memory;
using System.Text.Json.Nodes;
using DotCraft.GeneratedTools.Core;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Tools.Architecture;

public sealed partial class RemoteToolHostCoreTests
{
    [Fact]
    public async Task Memory_store_file_calls_run_locally_while_other_calls_follow_the_route()
    {
        var workspace = Directory.CreateTempSubdirectory("dotcraft-remote-memory-").FullName;
        try
        {
            var dataPath = Path.Combine(workspace, ".craft");
            var store = Path.Combine(dataPath, "memory");
            Directory.CreateDirectory(store);
            var stores = new ThreadMemoryStores(new MemoryStore(dataPath), []);
            var client = new FakeRemoteClient();
            client.SetRoute("thread-1");
            var registrations = await new WriteFileSource(workspace).GetRegistrationsAsync(new ToolPlanningContext(
                "thread-1", "turn-1", workspace, dataPath, "agent", null, [], 1, memoryStores: stores));
            var snapshot = new EffectiveToolSnapshotBuilder().Build(RemoteToolRegistrationRouter.Wrap(registrations, client, stores), 1);

            async Task<ToolExecutionResult> WriteAsync(string path, string? target = null)
            {
                var arguments = new JsonObject { ["path"] = path, ["content"] = "A fact." };
                if (target is not null)
                    arguments["target"] = target;
                return await new ToolDispatcher().DispatchAsync(snapshot, new ToolName(null, nameof(FileTools.WriteFile)), arguments,
                    new ToolInvocationRequest("thread-1", "turn-1", $"call-{path}", ToolInvocationAudience.Model, WorkspacePath: workspace));
            }

            var saved = await WriteAsync(".craft/memory/fact.md");
            var savedDespiteTarget = await WriteAsync(Path.Combine(store, "other.md"), target: "remote");
            var routed = await WriteAsync("notes.md");

            Assert.True(saved.Success);
            Assert.Equal("local", saved.Meta!.Value.GetProperty("executionTarget").GetString());
            Assert.True(File.Exists(Path.Combine(store, "fact.md")));
            Assert.True(savedDespiteTarget.Success);
            Assert.True(File.Exists(Path.Combine(store, "other.md")));
            Assert.Equal("remote", routed.Content);
            Assert.False(File.Exists(Path.Combine(workspace, "notes.md")));
            Assert.Equal(1, client.RemoteCalls);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    private sealed class WriteFileSource(string workspace) : AIFunctionToolSource
    {
        public override string SourceId => "core-native";

        protected override IEnumerable<AIFunction> CreateFunctions(ToolPlanningContext context)
        {
            yield return GeneratedToolFunctions.FileTools_WriteFile(
                new FileTools(workspace, memoryStores: context.MemoryStores));
        }
    }
}
