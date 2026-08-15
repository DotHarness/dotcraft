using DotCraft.Workspaces;
using DotCraft.Configuration;
using DotCraft.DashBoard;
using DotCraft.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace DotCraft.CLI;

internal static class DashboardCliHost
{
    public static async Task<int> RunAsync(CommandLineArgs args)
    {
        var (workspacePath, craftPath) = ResolveWorkspace(args.DashboardWorkspacePath);
        if (!Directory.Exists(craftPath))
        {
            await Console.Error.WriteLineAsync($"DotCraft workspace not found: {craftPath}");
            return 1;
        }

        var stateDbPath = Path.Combine(craftPath, "state.db");
        if (!File.Exists(stateDbPath))
        {
            await Console.Error.WriteLineAsync($"DotCraft workspace state database not found: {stateDbPath}");
            return 1;
        }

        var configPath = Path.Combine(craftPath, "config.json");
        var config = AppConfig.LoadWithGlobalFallback(configPath);
        args.ApplyTo(config);

        var stores = DashBoardReadOnlyStoreLoader.Load(craftPath);
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        var app = builder.Build();

        var paths = new WorkspacePaths
        {
            WorkspacePath = workspacePath,
            CraftPath = craftPath
        };

        app.MapDashBoardAuth(config);
        app.UseDashBoardAuth(config);
        app.MapDashBoard(
            stores.TraceStore,
            paths,
            stores.TokenUsageStore,
            refreshTraceFromDiskBeforeRead: true,
            runtimeOptions: DashBoardRuntimeOptions.ReadOnlyViewer());

        var baseUrl = $"http://{config.DashBoard.Host}:{config.DashBoard.Port}";
        var dashboardUrl = $"{baseUrl}/dashboard";
        AnsiConsole.MarkupLine(
            $"[green]DashBoard read-only viewer started at[/] [link={dashboardUrl}]{dashboardUrl}[/]");
        AnsiConsole.MarkupLine(
            $"[grey]Workspace:[/] {Markup.Escape(workspacePath)}");

        await app.RunAsync(baseUrl);
        return 0;
    }

    private static (string WorkspacePath, string CraftPath) ResolveWorkspace(string? requestedPath)
    {
        var input = string.IsNullOrWhiteSpace(requestedPath)
            ? Directory.GetCurrentDirectory()
            : requestedPath.Trim();
        var fullPath = Path.GetFullPath(input);
        if (string.Equals(Path.GetFileName(fullPath), ".craft", StringComparison.OrdinalIgnoreCase))
        {
            var workspace = Directory.GetParent(fullPath)?.FullName ?? fullPath;
            return (workspace, fullPath);
        }

        return (fullPath, Path.Combine(fullPath, ".craft"));
    }
}
