using System.CommandLine;
using DotCraft.ModelService;

namespace DotCraft.CLI;

public static partial class DotCraftCommandLine
{
    private static Command CreateModelServiceCommand()
    {
        var group = new Command("model-service", "Run and manage a model service.");
        var state = new Option<string>("--state") { Description = "Service state directory.", DefaultValueFactory = _ => Path.GetFullPath("model-service-state"), Recursive = true };
        group.Options.Add(state);
        var plugins = new Option<string[]>("--plugin")
        {
            Recursive = true,
            Arity = ArgumentArity.OneOrMore,
            Description = "Allow an installed service plugin by id. Repeat for each plugin and dependency."
        };
        group.Options.Add(plugins);

        var listen = new Option<string>("--listen") { DefaultValueFactory = _ => "http://127.0.0.1:8090" };
        var serve = new Command("serve", "Start the model HTTP service.") { listen };
        serve.SetAction((parse, cancellationToken) => ModelServiceCliRunner.ServeAsync(
            parse.GetRequiredValue(state), parse.GetValue(plugins) ?? [], parse.GetRequiredValue(listen), cancellationToken));
        group.Subcommands.Add(serve);

        var check = new Command("check", "Validate provider and client configuration.");
        check.SetAction((parse, ct) => ModelServiceCliRunner.CheckAsync(
            parse.GetRequiredValue(state), parse.GetValue(plugins) ?? [], parse.InvocationConfiguration.Output, ct));
        group.Subcommands.Add(check);

        var clientGroup = new Command("client", "Manage inference client access.");
        var name = new Option<string>("--name") { Required = true };
        var providers = new Option<string[]>("--provider") { Required = true, AllowMultipleArgumentsPerToken = true };
        var output = new Option<string>("--output") { Required = true, Description = "New file receiving the client token." };
        var create = new Command("create", "Create a client credential.") { name, providers, output };
        create.SetAction((parse, ct) => ModelServiceCliRunner.CreateClientAsync(
            parse.GetRequiredValue(state), parse.GetValue(plugins) ?? [], parse.GetRequiredValue(name),
            parse.GetRequiredValue(providers), parse.GetRequiredValue(output), parse.InvocationConfiguration.Output, ct));
        clientGroup.Subcommands.Add(create);
        var id = new Option<string>("--id") { Required = true };
        var revoke = new Command("revoke", "Revoke a client credential.") { id };
        revoke.SetAction(parse => new ModelServiceClientStore(parse.GetRequiredValue(state)).Revoke(parse.GetRequiredValue(id)) ? 0 : 1);
        clientGroup.Subcommands.Add(revoke);
        group.Subcommands.Add(clientGroup);

        group.Subcommands.Add(CreateModelServiceAuthCommand(state, plugins));
        group.Subcommands.Add(CreateModelServicePluginCommand(state, plugins));
        return group;
    }

    private static Command CreateModelServiceAuthCommand(Option<string> state, Option<string[]> plugins)
    {
        var auth = new Command("auth", "Manage service provider authorization.");
        var provider = new Option<string?>("--provider") { Recursive = true, Description = "Configured provider instance. Omit for ChatGPT." };
        auth.Options.Add(provider);
        var method = StringOption("--method", "Authentication method id.");
        var noBrowser = Flag("--no-browser", "Print the authorization URL without opening a browser.");
        var login = new Command("login", "Sign in to an upstream provider.") { method, noBrowser };
        login.SetAction((parse, ct) => ModelServiceCliRunner.LoginAsync(
            parse.GetRequiredValue(state), parse.GetValue(plugins) ?? [], parse.GetValue(provider), parse.GetValue(method),
            parse.GetValue(noBrowser), Console.In, parse.InvocationConfiguration.Output, ct));
        var status = new Command("status", "Show upstream sign-in status and available account usage.");
        status.SetAction((parse, ct) => ModelServiceCliRunner.StatusAsync(
            parse.GetRequiredValue(state), parse.GetValue(plugins) ?? [], parse.GetValue(provider), parse.InvocationConfiguration.Output, ct));
        var logout = new Command("logout", "Revoke and remove upstream credentials.");
        logout.SetAction((parse, ct) => ModelServiceCliRunner.LogoutAsync(
            parse.GetRequiredValue(state), parse.GetValue(plugins) ?? [], parse.GetValue(provider), ct));
        auth.Subcommands.Add(login);
        auth.Subcommands.Add(status);
        auth.Subcommands.Add(logout);
        return auth;
    }

    private static Command CreateModelServicePluginCommand(Option<string> state, Option<string[]> plugins)
    {
        var group = new Command("plugin", "Manage trust for model service plugins.");
        var trustId = new Argument<string>("id");
        var trust = new Command("trust", "Trust the installed fingerprint of an allowed plugin.") { trustId };
        trust.SetAction((parse, ct) => ModelServiceCliRunner.TrustPluginAsync(
            parse.GetRequiredValue(state), parse.GetValue(plugins) ?? [], parse.GetRequiredValue(trustId),
            parse.InvocationConfiguration.Output, ct));
        var revokeId = new Argument<string>("id");
        var revoke = new Command("revoke", "Remove every trust grant for a plugin from the selected service state.") { revokeId };
        revoke.SetAction(parse => ModelServiceCliRunner.RevokePluginTrust(
            parse.GetRequiredValue(state), parse.GetRequiredValue(revokeId), parse.InvocationConfiguration.Output));
        group.Subcommands.Add(trust);
        group.Subcommands.Add(revoke);
        return group;
    }
}
