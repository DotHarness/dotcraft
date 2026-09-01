using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotCraft.CLI;

internal sealed record StackProcessResult(int ExitCode, string StandardOutput, string StandardError);

internal interface IStackProcessRunner
{
    Task<StackProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken ct);
}

internal sealed record StackCommandOptions
{
    public string Directory { get; init; } = Path.GetFullPath(".");
    public bool DryRun { get; init; }
    public bool NoStart { get; init; }
    public string? Version { get; init; }
    public string? Provider { get; init; }
    public string? Model { get; init; }
    public string? ApiKey { get; init; }
    public string? Project { get; init; }
    public string? Workspace { get; init; }
    public string? Tail { get; init; }
    public string? Service { get; init; }
    public string? PublicHost { get; init; }
}

internal sealed class StackProcessRunner : IStackProcessRunner
{
    public async Task<StackProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken ct)
    {
        var start = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {fileName}.");
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        return new StackProcessResult(process.ExitCode, await stdout, await stderr);
    }
}

internal static class StackCliRunner
{
    private const string ComposeFile = "docker-compose.yml";
    private const string WebhookComposeFile = "docker-compose.webhook.yml";
    private const string CaddyFile = "Caddyfile";

    public static Task<int> InitAsync(
        StackCommandOptions options,
        TextWriter output,
        TextWriter error,
        CancellationToken ct,
        IStackProcessRunner? processRunner = null) => ExecuteAsync(
            output, error, processRunner, runner => runner.InitAsync(options, ct));

    public static Task<int> AddProjectAsync(StackCommandOptions options, TextWriter output, TextWriter error) =>
        ExecuteAsync(output, error, null, runner => runner.AddProjectAsync(options));

    public static Task<int> DoctorAsync(
        StackCommandOptions options,
        TextWriter output,
        TextWriter error,
        CancellationToken ct,
        IStackProcessRunner? processRunner = null) => ExecuteAsync(
            output, error, processRunner, runner => runner.DoctorAsync(options, ct));

    public static Task<int> StatusAsync(
        StackCommandOptions options,
        TextWriter output,
        TextWriter error,
        CancellationToken ct,
        IStackProcessRunner? processRunner = null) => ExecuteAsync(
            output, error, processRunner, runner => runner.ComposeAsync(options, ["ps", "--all"], ct));

    public static Task<int> LogsAsync(
        StackCommandOptions options,
        TextWriter output,
        TextWriter error,
        CancellationToken ct,
        IStackProcessRunner? processRunner = null) => ExecuteAsync(
            output, error, processRunner, runner => runner.LogsAsync(options, ct));

    public static Task<int> RestartAsync(
        StackCommandOptions options,
        TextWriter output,
        TextWriter error,
        CancellationToken ct,
        IStackProcessRunner? processRunner = null) => ExecuteAsync(
            output, error, processRunner, runner => runner.MutatingComposeAsync(options, ["restart"], ct));

    public static Task<int> UpgradeAsync(
        StackCommandOptions options,
        TextWriter output,
        TextWriter error,
        CancellationToken ct,
        IStackProcessRunner? processRunner = null) => ExecuteAsync(
            output, error, processRunner, runner => runner.UpgradeAsync(options, ct));

    public static Task<int> EnableWebhookAsync(
        StackCommandOptions options,
        TextWriter output,
        TextWriter error,
        CancellationToken ct,
        IStackProcessRunner? processRunner = null) => ExecuteAsync(
            output, error, processRunner, runner => runner.EnableWebhookAsync(options, ct));

    public static Task<int> WebhookStatusAsync(
        StackCommandOptions options,
        TextWriter output,
        TextWriter error,
        CancellationToken ct,
        IStackProcessRunner? processRunner = null) => ExecuteAsync(
            output, error, processRunner, runner => runner.WebhookStatusAsync(options, ct));

    public static Task<int> DisableWebhookAsync(
        StackCommandOptions options,
        TextWriter output,
        TextWriter error,
        CancellationToken ct,
        IStackProcessRunner? processRunner = null) => ExecuteAsync(
            output, error, processRunner, runner => runner.DisableWebhookAsync(options, ct));

    private static async Task<int> ExecuteAsync(
        TextWriter output,
        TextWriter error,
        IStackProcessRunner? processRunner,
        Func<Runner, Task<int>> action)
    {
        try
        {
            return await action(new Runner(output, error, processRunner ?? new StackProcessRunner())).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await error.WriteLineAsync("Stack operation cancelled.").ConfigureAwait(false);
            return 130;
        }
        catch (Exception ex)
        {
            await error.WriteLineAsync($"error: {Redact(ex.Message)}").ConfigureAwait(false);
            return 1;
        }
    }

    private sealed class Runner(TextWriter output, TextWriter error, IStackProcessRunner processes)
    {
        internal async Task<int> InitAsync(StackCommandOptions options, CancellationToken ct)
        {
            var root = options.Directory;
            var planned = new[] { ComposeFile, ".env", "state/dotcraft", "state/oratorio/config.json", "workspace", "secrets" };
            if (options.DryRun)
            {
                await output.WriteLineAsync($"Would initialize DotCraft Stack at {root}.");
                foreach (var item in planned) await output.WriteLineAsync($"Would create {item}.");
                if (!options.NoStart) await output.WriteLineAsync("Would run docker compose up -d.");
                return 0;
            }

            if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
                throw new InvalidOperationException($"Deployment directory is not empty: {root}");

            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.Combine(root, "workspace"));
            Directory.CreateDirectory(Path.Combine(root, "secrets"));
            Directory.CreateDirectory(Path.Combine(root, "state", "dotcraft"));
            Directory.CreateDirectory(Path.Combine(root, "state", "oratorio"));
            WriteAtomic(Path.Combine(root, ComposeFile), ReadAsset(ComposeFile));

            var appServerToken = GenerateToken();
            var oratorioToken = GenerateToken();
            var env = BuildEnvironment(options, appServerToken, oratorioToken);
            WriteAtomic(Path.Combine(root, ".env"), env);
            WriteAtomic(Path.Combine(root, "state", "oratorio", "config.json"), BuildInitialConfiguration());
            WriteAtomic(Path.Combine(root, ".gitignore"), ".env\nsecrets/\nstate/\nworkspace/.craft/\n");

            await output.WriteLineAsync($"Initialized DotCraft Stack at {root}.");
            await output.WriteLineAsync($"AppServer token (shown once): {appServerToken}");
            await output.WriteLineAsync($"Oratorio service token (shown once): {oratorioToken}");
            if (!options.NoStart)
                return await RunComposeAsync(root, ["up", "-d", "--remove-orphans"], ct);
            return 0;
        }

        internal async Task<int> AddProjectAsync(StackCommandOptions options)
        {
            var provider = Require(options.Provider, "--provider").ToLowerInvariant();
            if (provider is not ("github" or "gitlab"))
                throw new ArgumentException("--provider must be github or gitlab.");
            var project = NormalizeProject(provider, Require(options.Project, "--project"));
            var workspace = NormalizeWorkspace(Require(options.Workspace, "--workspace"));
            var path = Path.Combine(options.Directory, "state", "oratorio", "config.json");
            if (!File.Exists(path)) throw new FileNotFoundException("Stack Oratorio configuration was not found.", path);

            if (options.DryRun)
            {
                await output.WriteLineAsync($"Would bind {project} to {workspace} in {path}.");
                return 0;
            }

            var root = JsonNode.Parse(await File.ReadAllTextAsync(path))?.AsObject()
                       ?? throw new InvalidDataException("Oratorio configuration is invalid.");
            var oratorio = RequireObject(root, "Oratorio");
            var dotcraft = RequireObject(oratorio, "DotCraft");
            var routes = RequireArray(dotcraft, "RepositoryWorkspaceRoutes");
            for (var i = routes.Count - 1; i >= 0; i--)
            {
                if (routes[i] is JsonObject route && string.Equals(route["Project"]?.GetValue<string>(), project, StringComparison.OrdinalIgnoreCase))
                    routes.RemoveAt(i);
            }
            routes.Add(new JsonObject { ["Project"] = project, ["WorkspacePath"] = workspace });

            var source = RequireObject(oratorio, provider == "github" ? "GitHub" : "GitLab");
            if (provider == "gitlab") source["Enabled"] = true;
            var collection = RequireArray(source, provider == "github" ? "Repositories" : "Projects");
            var sourceProject = project[(project.LastIndexOf(':') + 1)..];
            if (!collection.Any(value => string.Equals(value?.GetValue<string>(), sourceProject, StringComparison.OrdinalIgnoreCase)))
                collection.Add(sourceProject);

            WriteAtomic(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
            await output.WriteLineAsync($"Bound {project} to {workspace}.");
            return 0;
        }

        internal async Task<int> DoctorAsync(StackCommandOptions options, CancellationToken ct)
        {
            var failures = 0;
            foreach (var file in new[] { ComposeFile, ".env", Path.Combine("state", "oratorio", "config.json") })
            {
                var ok = File.Exists(Path.Combine(options.Directory, file));
                await output.WriteLineAsync($"[{(ok ? "ok" : "fail")}] {file}");
                if (!ok) failures++;
            }
            if (failures > 0) return 1;

            var env = await File.ReadAllTextAsync(Path.Combine(options.Directory, ".env"), ct);
            foreach (var name in new[] { "APPSERVER_TOKEN", "ORATORIO_SERVICE_TOKEN" })
            {
                var ok = ReadEnv(env, name) is { Length: > 0 };
                await output.WriteLineAsync($"[{(ok ? "ok" : "fail")}] {name}");
                if (!ok) failures++;
            }

            failures += await ProbeAsync("docker", ["--version"], options.Directory, "Docker", ct) ? 0 : 1;
            failures += await ProbeAsync("docker", ["compose", "version"], options.Directory, "Docker Compose", ct) ? 0 : 1;
            if (failures == 0)
                failures += await RunComposeAsync(options.Directory, ["config", "--quiet"], ct, quietSuccess: true) == 0 ? 0 : 1;
            return failures == 0 ? 0 : 1;
        }

        internal async Task<int> LogsAsync(StackCommandOptions options, CancellationToken ct)
        {
            var composeArgs = new List<string> { "logs", "--no-color", "--tail", options.Tail ?? "200" };
            var service = options.Service;
            if (!string.IsNullOrWhiteSpace(service)) composeArgs.Add(ValidateService(service));
            return await RunComposeAsync(options.Directory, composeArgs, ct);
        }

        internal async Task<int> MutatingComposeAsync(
            StackCommandOptions options,
            IReadOnlyList<string> composeArgs,
            CancellationToken ct)
        {
            if (options.DryRun)
            {
                await output.WriteLineAsync($"Would run docker compose {string.Join(' ', composeArgs)} in {options.Directory}.");
                return 0;
            }
            return await RunComposeAsync(options.Directory, composeArgs, ct);
        }

        internal async Task<int> UpgradeAsync(StackCommandOptions options, CancellationToken ct)
        {
            if (options.DryRun)
            {
                await output.WriteLineAsync($"Would pull and recreate the stack in {options.Directory}.");
                return 0;
            }
            var pull = await RunComposeAsync(options.Directory, ["pull"], ct);
            return pull == 0 ? await RunComposeAsync(options.Directory, ["up", "-d", "--remove-orphans"], ct) : pull;
        }

        internal async Task<int> WebhookStatusAsync(StackCommandOptions options, CancellationToken ct)
        {
            var enabled = File.Exists(Path.Combine(options.Directory, WebhookComposeFile))
                && File.Exists(Path.Combine(options.Directory, CaddyFile));
            await output.WriteLineAsync(enabled ? "Webhook ingress: enabled" : "Webhook ingress: disabled");
            return enabled
                ? await RunComposeAsync(options.Directory, ["ps", "webhook-gateway"], ct)
                : 0;
        }

        internal async Task<int> EnableWebhookAsync(StackCommandOptions options, CancellationToken ct)
        {
            var host = ValidatePublicHost(Require(options.PublicHost, "--public-host"));
            if (options.DryRun)
            {
                await output.WriteLineAsync($"Would enable GitHub webhook ingress at https://{host}/api/v1/sources/github/webhook.");
                return 0;
            }

            WriteAtomic(Path.Combine(options.Directory, WebhookComposeFile), ReadAsset(WebhookComposeFile));
            WriteAtomic(Path.Combine(options.Directory, CaddyFile), ReadAsset(CaddyFile));
            var envPath = Path.Combine(options.Directory, ".env");
            var env = await File.ReadAllTextAsync(envPath, ct);
            env = UpsertEnv(env, "ORATORIO_WEBHOOK_PUBLIC_HOST", host);
            var existing = ReadEnv(env, "ORATORIO_GITHUB_WEBHOOK_SECRET");
            var secret = existing is { Length: > 0 } ? existing : GenerateToken();
            env = UpsertEnv(env, "ORATORIO_GITHUB_WEBHOOK_SECRET", secret);
            WriteAtomic(envPath, env);
            if (existing is not { Length: > 0 }) await output.WriteLineAsync($"Webhook secret (shown once): {secret}");
            return await RunComposeAsync(options.Directory, ["up", "-d", "--force-recreate", "oratorio", "webhook-gateway"], ct);
        }

        internal async Task<int> DisableWebhookAsync(StackCommandOptions options, CancellationToken ct)
        {
            var overlay = Path.Combine(options.Directory, WebhookComposeFile);
            var caddy = Path.Combine(options.Directory, CaddyFile);
            if (options.DryRun)
            {
                await output.WriteLineAsync("Would stop and remove the webhook gateway while preserving stack state and secrets.");
                return 0;
            }
            if (File.Exists(overlay))
            {
                var result = await RunComposeAsync(options.Directory, ["stop", "webhook-gateway"], ct);
                if (result != 0) return result;
                result = await RunComposeAsync(options.Directory, ["rm", "-f", "webhook-gateway"], ct);
                if (result != 0) return result;
            }
            if (File.Exists(overlay)) File.Delete(overlay);
            if (File.Exists(caddy)) File.Delete(caddy);
            await output.WriteLineAsync("Webhook ingress is disabled; state, secrets, and certificate volumes were preserved.");
            return 0;
        }

        internal Task<int> ComposeAsync(
            StackCommandOptions options,
            IReadOnlyList<string> composeArgs,
            CancellationToken ct) => RunComposeAsync(options.Directory, composeArgs, ct);

        private async Task<int> RunComposeAsync(string directory, IReadOnlyList<string> command, CancellationToken ct, bool quietSuccess = false)
        {
            RequireDeployment(directory);
            var arguments = new List<string> { "compose", "-f", ComposeFile };
            if (File.Exists(Path.Combine(directory, WebhookComposeFile)))
                arguments.AddRange(["-f", WebhookComposeFile]);
            arguments.AddRange(command);
            var result = await processes.RunAsync("docker", arguments, directory, ct);
            if (!quietSuccess && !string.IsNullOrWhiteSpace(result.StandardOutput))
                await output.WriteLineAsync(Redact(result.StandardOutput.TrimEnd()));
            if (result.ExitCode != 0)
                await error.WriteLineAsync(Redact(string.IsNullOrWhiteSpace(result.StandardError) ? "Docker Compose failed." : result.StandardError.TrimEnd()));
            return result.ExitCode;
        }

        private async Task<bool> ProbeAsync(string file, IReadOnlyList<string> args, string directory, string label, CancellationToken ct)
        {
            try
            {
                var result = await processes.RunAsync(file, args, directory, ct);
                var ok = result.ExitCode == 0;
                await output.WriteLineAsync($"[{(ok ? "ok" : "fail")}] {label}");
                return ok;
            }
            catch
            {
                await output.WriteLineAsync($"[fail] {label}");
                return false;
            }
        }

    }

    private static string BuildEnvironment(StackCommandOptions options, string appServerToken, string oratorioToken) =>
        $"APPSERVER_TOKEN={appServerToken}\n" +
        $"ORATORIO_SERVICE_TOKEN={oratorioToken}\n" +
        "APPSERVER_PORT=9100\nORATORIO_PORT=5087\nDASHBOARD_PORT=8080\n" +
        $"DOTCRAFT_VERSION={options.Version ?? "latest"}\n" +
        "DOTCRAFT_WORKSPACE_DIR=./workspace\nDOTCRAFT_STACK_STATE_DIR=./state\n" +
        $"DOTCRAFT_PROVIDER={options.Provider ?? "openai"}\n" +
        $"DOTCRAFT_MODEL={options.Model ?? "gpt-5.6"}\n" +
        $"DOTCRAFT_API_KEY={options.ApiKey ?? string.Empty}\n";

    private static string BuildInitialConfiguration() =>
        new JsonObject
        {
            ["Oratorio"] = new JsonObject
            {
                ["Settings"] = new JsonObject { ["Writable"] = true },
                ["DotCraft"] = new JsonObject
                {
                    ["AppServerUrl"] = "ws://dotcraft:9100/ws",
                    ["HubDiscoveryEnabled"] = false,
                    ["ManagedWorktreesEnabled"] = true,
                    ["WorktreeRoot"] = "/workspace/.craft/oratorio/worktrees",
                    ["RepositoryWorkspaceRoutes"] = new JsonArray()
                },
                ["GitHub"] = new JsonObject { ["Repositories"] = new JsonArray() },
                ["GitLab"] = new JsonObject { ["Enabled"] = false, ["Projects"] = new JsonArray() }
            }
        }.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;

    private static string NormalizeProject(string provider, string value)
    {
        var raw = value.Trim().Replace('\\', '/').Trim('/');
        var prefix = provider + ":";
        if (raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) raw = raw[prefix.Length..];
        var defaultHost = provider == "github" ? "github.com/" : "gitlab.com/";
        if (!raw.Contains('/')) throw new ArgumentException("--project must include an owner/group and repository name.");
        if (!raw.Contains('.', StringComparison.Ordinal)) raw = defaultHost + raw;
        if (raw.Split('/').Any(segment => string.IsNullOrWhiteSpace(segment) || segment == ".."))
            throw new ArgumentException("--project is invalid.");
        return $"{provider}:{raw}".ToLowerInvariant();
    }

    private static string NormalizeWorkspace(string value)
    {
        var workspace = value.Trim().Replace('\\', '/').TrimEnd('/');
        if (!workspace.StartsWith("/workspace/", StringComparison.Ordinal) || workspace.Split('/').Contains(".."))
            throw new ArgumentException("--workspace must be an absolute path below /workspace.");
        return workspace;
    }

    private static string ValidateService(string value) =>
        value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.')
            ? value
            : throw new ArgumentException("--service is invalid.");

    private static string ValidatePublicHost(string value)
    {
        var host = value.Trim().ToLowerInvariant();
        if (host.Length is < 1 or > 253 || host.Any(ch => !(char.IsAsciiLetterOrDigit(ch) || ch is '.' or '-' or ':')))
            throw new ArgumentException("--public-host is invalid.");
        return host;
    }

    private static JsonObject RequireObject(JsonObject parent, string name) =>
        parent[name] as JsonObject
        ?? throw new InvalidDataException($"Oratorio configuration is missing object '{name}'.");

    private static JsonArray RequireArray(JsonObject parent, string name) =>
        parent[name] as JsonArray
        ?? throw new InvalidDataException($"Oratorio configuration is missing array '{name}'.");

    private static void RequireDeployment(string directory)
    {
        if (!File.Exists(Path.Combine(directory, ComposeFile)))
            throw new FileNotFoundException("docker-compose.yml was not found in the stack directory.");
    }

    private static string ReadAsset(string fileName)
    {
        var assembly = typeof(StackCliRunner).Assembly;
        var resource = assembly.GetManifestResourceNames().SingleOrDefault(name => name.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
                       ?? throw new InvalidOperationException($"Embedded stack asset '{fileName}' was not found.");
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static void WriteAtomic(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(temporary, content, new UTF8Encoding(false));
        File.Move(temporary, path, true);
    }

    private static string GenerateToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string? ReadEnv(string content, string name) => content
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
        .Select(line => line.Split('=', 2))
        .Where(parts => parts.Length == 2 && parts[0].Trim().Equals(name, StringComparison.Ordinal))
        .Select(parts => parts[1].Trim())
        .FirstOrDefault();

    private static string UpsertEnv(string content, string name, string value)
    {
        var lines = content.Replace("\r\n", "\n").Split('\n').ToList();
        var index = lines.FindIndex(line => line.StartsWith(name + "=", StringComparison.Ordinal));
        if (index >= 0) lines[index] = $"{name}={value}";
        else lines.Add($"{name}={value}");
        return string.Join('\n', lines).TrimEnd() + "\n";
    }

    private static string Require(string? value, string option) => value is { Length: > 0 }
        ? value
        : throw new ArgumentException($"{option} is required.");

    private static string Redact(string value) => System.Text.RegularExpressions.Regex.Replace(
        value,
        @"(?im)(TOKEN|SECRET|API_KEY)=([^\s]+)",
        "$1=[redacted]");
}
