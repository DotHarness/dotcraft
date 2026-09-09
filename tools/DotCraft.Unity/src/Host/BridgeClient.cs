using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis.CSharp;

namespace DotCraft.Unity;

internal sealed record PreparedExecution(string ExecutionId, string AssemblyPath, string Generation);

internal static class BridgeClient
{
    public static async Task<JsonObject> Call(
        string connectionPath,
        string command,
        string? assembly = null,
        JsonObject? args = null,
        string? id = null,
        string? expectedGeneration = null,
        string? executionId = null,
        int? waitMs = null,
        bool terminate = false,
        CancellationToken cancellationToken = default)
    {
        var connection = ReadConnection(connectionPath, expectedGeneration);
        using var process = System.Diagnostics.Process.GetProcessById(connection["pid"]!.GetValue<int>());
        if (process.StartTime.ToUniversalTime() != connection["startUtc"]!.GetValue<DateTime>())
            throw new InvalidOperationException("Target process changed.");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMilliseconds((waitMs ?? 0) + 15_000));
        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync("127.0.0.1", connection["port"]!.GetValue<int>(), deadline.Token);
            var request = new JsonObject {
                ["protocol"] = 1,
                ["token"] = connection["token"]!.GetValue<string>(),
                ["generation"] = connection["generation"]!.GetValue<string>(),
                ["id"] = id ?? Guid.NewGuid().ToString("N"),
                ["command"] = command,
                ["assembly"] = assembly,
                ["args"] = args?.DeepClone(),
                ["executionId"] = executionId,
                ["waitMs"] = waitMs,
                ["terminate"] = terminate
            };
            var data = Encoding.UTF8.GetBytes(request.ToJsonString());
            var stream = client.GetStream();
            await stream.WriteAsync(BitConverter.GetBytes(data.Length), deadline.Token);
            await stream.WriteAsync(data, deadline.Token);
            var header = new byte[4];
            await stream.ReadExactlyAsync(header, deadline.Token);
            int length = BitConverter.ToInt32(header);
            if (length < 1 || length > 4 * 1024 * 1024) throw new InvalidDataException("Invalid response size.");
            var response = new byte[length];
            await stream.ReadExactlyAsync(response, deadline.Token);
            var result = JsonNode.Parse(response)!.AsObject();
            if (result["generation"]?.GetValue<string>() != connection["generation"]!.GetValue<string>())
                throw new InvalidDataException("Unity script domain changed; reconnect before executing again.");
            if (command == "metadata" && result["state"]?.GetValue<string>() == "completed"
                && !string.Equals(Path.GetFullPath(result["result"]!["project"]!.GetValue<string>()),
                    Path.GetFullPath(connection["project"]!.GetValue<string>()), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Unity project identity changed.");
            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Unity bridge request timed out.");
        }
    }

    public static async Task<PreparedExecution> Prepare(string connectionPath, string code, string cacheRoot)
    {
        var metadata = await Call(connectionPath, "metadata");
        if (metadata["state"]!.GetValue<string>() != "completed")
            throw new InvalidOperationException(metadata.ToJsonString());
        var bridge = metadata["result"]!["bridge"]!.GetValue<string>();
        var references = metadata["result"]!["references"]!.AsArray().Select(n => n!.GetValue<string>())
            .Where(p => !Path.GetFileName(p).StartsWith("Snippet_", StringComparison.Ordinal)
                && (!Path.GetFileName(p).StartsWith("Attach_", StringComparison.Ordinal)
                    || string.Equals(p, bridge, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        var directory = Path.Combine(cacheRoot, "snippets");
        Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".cs");
        var unit = CSharpSyntaxTree.ParseText(code).GetCompilationUnitRoot();
        var usings = string.Join("\n", unit.Usings.Select(u => u.ToFullString()));
        var body = unit.Usings.Count == 0 ? code : code[unit.Usings.Last().FullSpan.End..];
        File.WriteAllText(source,
            "using System; using System.Linq; using System.Threading; using System.Threading.Tasks; using UnityEditor; using UnityEngine; using Newtonsoft.Json.Linq;\n"
            + usings
            + "\nnamespace DotCraft.Unity { public static class Snippet { public static async Task<object> Run(JObject Args, UnityExecutionContext ctx, CancellationToken cancellationToken) {\n"
            + body + "\n} } }");
        try
        {
            var generation = metadata["generation"]!.GetValue<string>();
            var assembly = TargetCompiler.Compile(source, references, Path.Combine(cacheRoot, "cache"), "Snippet_", generation);
            return new PreparedExecution("unity_" + Guid.NewGuid().ToString("N"), Path.GetFullPath(assembly), generation);
        }
        finally { File.Delete(source); }
    }

    public static Task<JsonObject> Start(
        string connectionPath,
        PreparedExecution execution,
        JsonObject? args,
        CancellationToken cancellationToken = default) =>
        Call(connectionPath, "execute_start", execution.AssemblyPath, args,
            expectedGeneration: execution.Generation, executionId: execution.ExecutionId,
            cancellationToken: cancellationToken);

    public static Task<JsonObject> Wait(
        string connectionPath,
        string executionId,
        string generation,
        int waitMs,
        bool terminate,
        CancellationToken cancellationToken = default) =>
        Call(connectionPath, "execute_wait", expectedGeneration: generation, executionId: executionId,
            waitMs: waitMs, terminate: terminate, cancellationToken: cancellationToken);

    public static string ReadGeneration(string connectionPath) =>
        ReadConnection(connectionPath, null)["generation"]!.GetValue<string>();

    private static JsonObject ReadConnection(string connectionPath, string? expectedGeneration)
    {
        var connection = JsonNode.Parse(File.ReadAllText(connectionPath))!.AsObject();
        if (connection["protocol"]?.GetValue<int>() != 1) throw new InvalidDataException("Unsupported Unity bridge protocol.");
        if (expectedGeneration != null && connection["generation"]?.GetValue<string>() != expectedGeneration)
            throw new InvalidDataException("Unity script domain changed during execution.");
        return connection;
    }
}
