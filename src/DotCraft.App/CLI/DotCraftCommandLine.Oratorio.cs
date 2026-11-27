using System.CommandLine;

namespace DotCraft.CLI;

public static partial class DotCraftCommandLine
{
    private static Command CreateOratorioCommand()
    {
        var group = new Command("oratorio", "Run the Oratorio task orchestration service.");
        var state = StringOption("--state", "Directory for Oratorio configuration, credentials and database.");
        var listen = StringOption("--listen", "HTTP address to listen on. Defaults to http://127.0.0.1:5087.");
        listen.Validators.Add(result =>
        {
            var value = result.GetValueOrDefault<string?>();
            if (value is not null && !OratorioCliRunner.IsListenUrl(value))
                result.AddError("--listen must be an absolute HTTP or HTTPS address without a path, query or credentials.");
        });
        state.Validators.Add(result =>
        {
            if (result.GetValueOrDefault<string?>() is { } value && string.IsNullOrWhiteSpace(value))
                result.AddError("--state must be a non-empty directory path.");
        });
        var serve = new Command("serve", "Start the independent Oratorio HTTP service.") { state, listen };
        serve.SetAction((parse, cancellationToken) => OratorioCliRunner.ServeAsync(
            parse.GetValue(state),
            parse.GetValue(listen),
            parse.InvocationConfiguration.Error,
            cancellationToken));
        group.Subcommands.Add(serve);

        var url = StringOption("--url", "Optional Oratorio HTTP base URL. Defaults to ORATORIO_URL or the Hub-managed Oratorio service.");
        var token = StringOption("--token", "Optional bearer token for an explicit Oratorio HTTP service. Defaults to ORATORIO_SERVICE_TOKEN when --url is omitted.");
        var mcp = new Command("mcp", "Run the Oratorio stdio MCP bridge.") { url, token };
        mcp.SetAction((parse, cancellationToken) => OratorioCliRunner.McpAsync(
            parse.GetValue(url),
            parse.GetValue(token),
            parse.InvocationConfiguration.Error,
            cancellationToken));
        group.Subcommands.Add(mcp);
        return group;
    }
}
