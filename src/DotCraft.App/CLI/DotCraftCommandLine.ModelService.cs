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

        var listen = new Option<string>("--listen") { DefaultValueFactory = _ => "http://127.0.0.1:8090" };
        var serve = new Command("serve", "Start the model HTTP service.") { listen };
        serve.SetAction((parse, cancellationToken) => ModelServiceCliRunner.ServeAsync(
            parse.GetRequiredValue(state), parse.GetRequiredValue(listen), cancellationToken));
        group.Subcommands.Add(serve);

        var check = new Command("check", "Validate provider and client configuration.");
        check.SetAction(parse =>
        {
            var directory = parse.GetRequiredValue(state);
            var config = FileModelServiceAccess.LoadConfiguration(directory);
            var clients = new ModelServiceClientStore(directory).Load();
            foreach (var client in clients)
                foreach (var id in client.Providers)
                    if (!config.Providers.ContainsKey(id))
                        throw new InvalidOperationException($"Client '{client.Name}' grants unknown provider '{id}'.");
            parse.InvocationConfiguration.Output.WriteLine($"Valid: {config.Providers.Count} providers, {clients.Count} clients.");
            return 0;
        });
        group.Subcommands.Add(check);

        var clientGroup = new Command("client", "Manage inference client access.");
        var name = new Option<string>("--name") { Required = true };
        var providers = new Option<string[]>("--provider") { Required = true, AllowMultipleArgumentsPerToken = true };
        var output = new Option<string>("--output") { Required = true, Description = "New file receiving the client token." };
        var create = new Command("create", "Create a client credential.") { name, providers, output };
        create.SetAction(parse => ModelServiceCliRunner.CreateClient(
            parse.GetRequiredValue(state), parse.GetRequiredValue(name), parse.GetRequiredValue(providers),
            parse.GetRequiredValue(output), parse.InvocationConfiguration.Output));
        clientGroup.Subcommands.Add(create);
        var id = new Option<string>("--id") { Required = true };
        var revoke = new Command("revoke", "Revoke a client credential.") { id };
        revoke.SetAction(parse => new ModelServiceClientStore(parse.GetRequiredValue(state)).Revoke(parse.GetRequiredValue(id)) ? 0 : 1);
        clientGroup.Subcommands.Add(revoke);
        group.Subcommands.Add(clientGroup);

        var auth = new Command("auth", "Manage the service ChatGPT subscription.");
        var noBrowser = Flag("--no-browser", "Print the authorization URL without opening a browser.");
        var login = new Command("login", "Sign in to ChatGPT for this service.") { noBrowser };
        login.SetAction((parse, cancellationToken) => ModelServiceCliRunner.LoginAsync(
            parse.GetRequiredValue(state), parse.GetValue(noBrowser), parse.InvocationConfiguration.Output, cancellationToken));
        var logout = new Command("logout", "Revoke and remove this service's subscription credentials.");
        logout.SetAction(async (parse, cancellationToken) =>
        {
            await ModelServiceCliRunner.Authentication(parse.GetRequiredValue(state)).LogoutAsync(cancellationToken);
            return 0;
        });
        auth.Subcommands.Add(login);
        auth.Subcommands.Add(logout);
        group.Subcommands.Add(auth);
        return group;
    }
}
