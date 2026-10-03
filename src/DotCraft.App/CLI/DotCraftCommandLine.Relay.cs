using System.CommandLine;
using DotCraft.Relay;
using Microsoft.Extensions.Hosting;

namespace DotCraft.CLI;

public static partial class DotCraftCommandLine
{
    private const string RelayTokenVariable = "DOTCRAFT_RELAY_TOKEN";

    private static Command CreateRelayCommand()
    {
        var group = new Command("relay", "Run a relay that lets paired phones reach their computers from any network.");
        var listen = new Option<string>("--listen")
        {
            Description = "Address the relay listens on, behind a proxy that terminates public TLS.",
            DefaultValueFactory = _ => "http://127.0.0.1:47620"
        };
        var token = new Option<string?>("--token")
        {
            Description = $"Token computers register with. Defaults to the {RelayTokenVariable} environment variable."
        };
        var serve = new Command("serve", "Start the relay.") { listen, token };
        serve.SetAction(async (parse, cancellationToken) =>
        {
            var relayToken = parse.GetValue(token) ?? Environment.GetEnvironmentVariable(RelayTokenVariable);
            if (string.IsNullOrWhiteSpace(relayToken))
            {
                await parse.InvocationConfiguration.Error.WriteLineAsync($"Error: pass --token or set {RelayTokenVariable}.");
                return 1;
            }
            await using var app = MobileRelayServer.Build(parse.GetRequiredValue(listen), relayToken.Trim());
            await app.StartAsync(cancellationToken);
            await app.WaitForShutdownAsync(cancellationToken);
            return 0;
        });
        group.Subcommands.Add(serve);
        return group;
    }
}
