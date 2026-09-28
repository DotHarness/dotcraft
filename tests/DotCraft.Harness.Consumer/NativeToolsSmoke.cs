using System.Text.Json.Nodes;
using DotCraft.Configuration;
using DotCraft.Tools;
using DotCraft.Tools.BackgroundTerminals;
using static SmokeAssertions;

internal static class NativeToolsSmoke
{
    public static async Task RunAsync(string workspace)
    {
        var pending = new Queue<System.Reflection.Assembly>();
        var loaded = new HashSet<string>();
        pending.Enqueue(System.Reflection.Assembly.Load("DotCraft.Harness"));
        while (pending.TryDequeue(out var assembly))
        {
            if (!loaded.Add(assembly.FullName!)) continue;
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                var dependency = System.Reflection.Assembly.Load(reference);
                if (reference.Name!.StartsWith("DotCraft.", StringComparison.Ordinal)) pending.Enqueue(dependency);
            }
        }
        Directory.CreateDirectory(workspace);
        var config = new AppConfig();
        config.Tools.Lsp.Enabled = false;
        await using var terminals = new BackgroundTerminalService(Path.Combine(workspace, ".craft"), config.Tools.Shell.Background);
        var source = new WorkspaceExecutionToolSource(config, terminals);
        var planning = new ToolPlanningContext("native-package-test", "turn", workspace,
            Path.Combine(workspace, ".craft"), "agent", null, [], 1);
        var snapshot = await new EffectiveToolSnapshotBuilder().BuildAsync([source], planning);
        var recorder = new Recorder();
        var dispatcher = new ToolDispatcher(recorder: recorder);
        async Task<ToolExecutionResult> Invoke(string name, JsonObject arguments, bool success = true)
        {
            var id = Guid.NewGuid().ToString("N");
            var result = await dispatcher.DispatchAsync(snapshot, new ToolName(null, name), arguments,
                new(planning.ThreadId, planning.TurnId, id, ToolInvocationAudience.Model));
            Ensure(result.Success == success, $"{name} result status is wrong: {result.Content} {result.Error?.Message}");
            Ensure(recorder.Started.Contains(id) && recorder.Completed.TryGetValue(id, out var recorded)
                && recorded.Success == success, name + " result and execution record disagree.");
            return result;
        }
        var write = await Invoke("WriteFile", new() { ["path"] = "native.txt", ["content"] = "before\n" });
        Ensure(write.StructuredContent?.GetProperty("changes")[0].GetProperty("diff").GetString()?.Contains("+before") == true,
            "WriteFile did not produce a real diff from the packaged runtime.");
        var edit = await Invoke("EditFile", new() { ["path"] = "native.txt", ["oldText"] = "before", ["newText"] = "after" });
        Ensure(await File.ReadAllTextAsync(Path.Combine(workspace, "native.txt")) == "after\n", "File content is incorrect.");
        Ensure(edit.StructuredContent?.GetProperty("changes")[0].GetProperty("diff").GetString()?.Contains("+after") == true,
            "EditFile did not produce a real diff.");
        await Invoke("EditFile", new() { ["path"] = "missing.txt", ["oldText"] = "x", ["newText"] = "y" }, false);
        foreach (var shell in OperatingSystem.IsWindows() ? new[] { "pwsh", "powershell" } : new[] { "pwsh" })
        {
            var result = await Invoke("Exec", new() { ["command"] = "Write-Output '包测试-output'; exit 0", ["shell"] = shell });
            Ensure(result.Content?.Contains("包测试-output") == true, "PowerShell output was lost or corrupted.");
            var nonzero = await Invoke("Exec", new() { ["command"] = "Write-Output 'failed-output'; exit 7", ["shell"] = shell });
            Ensure(nonzero.Content?.Contains("Exit code: 7") == true, "PowerShell exit code was lost.");
        }
        Console.WriteLine("Packaged native file and PowerShell tools passed, including recorded failures.");
    }

    private sealed class Recorder : IToolInvocationRecorder
    {
        public HashSet<string> Started { get; } = [];
        public Dictionary<string, ToolExecutionResult> Completed { get; } = [];
        public ValueTask RecordStartedAsync(ToolInvocationContext context, ToolRegistration registration,
            JsonObject arguments, CancellationToken cancellationToken = default)
        {
            Ensure(Started.Add(context.CallId), "Duplicate started record.");
            return ValueTask.CompletedTask;
        }
        public ValueTask RecordTerminalAsync(ToolInvocationContext context, ToolRegistration registration,
            ToolExecutionResult result, TimeSpan duration, CancellationToken cancellationToken = default)
        {
            Ensure(Completed.TryAdd(context.CallId, result), "Duplicate completed record.");
            return ValueTask.CompletedTask;
        }
    }
}
