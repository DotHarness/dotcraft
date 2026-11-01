using System.Diagnostics;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.ModelService;

namespace DotCraft.CLI;

internal static partial class ModelServiceCliRunner
{
    private static OpenRouterProvider OpenRouter(string directory, string providerId)
    {
        var config = FileModelServiceAccess.LoadConfiguration(directory);
        var runtime = ModelProviderResolver.ResolveProvider(config, providerId);
        if (runtime.BackendId != OpenRouterBackend.Id)
            throw new ArgumentException("The selected provider is not an OpenRouter backend.");
        return new OpenRouterProvider(new ModelBackendContext(runtime,
            new ProviderCredentialStore(directory, OpenRouterBackend.Id, providerId), CancellationToken.None));
    }

    private static async Task<int> LoginOpenRouterAsync(string directory, string providerId, bool noBrowser, TextWriter output, CancellationToken ct)
    {
        var provider = OpenRouter(directory, providerId);
        var status = await provider.LoginAsync(OpenRouterBackend.OAuth, new Interaction(output, noBrowser), ct);
        await output.WriteLineAsync(status.IsAuthenticated ? "Signed in." : "Sign-in failed.");
        return status.IsAuthenticated ? 0 : 1;
    }

    public static Task LogoutOpenRouterAsync(string directory, string providerId, CancellationToken ct) => OpenRouter(directory, providerId).LogoutAsync(ct);

    private sealed class Interaction(TextWriter output, bool noBrowser) : IProviderLoginInteraction
    {
        public async ValueTask NotifyAsync(ProviderLoginEvent notification, CancellationToken ct)
        {
            await output.WriteLineAsync(notification.Url ?? notification.Message);
            if (!noBrowser && notification.Url is { } url)
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        public async Task<string> PromptAsync(ProviderLoginPrompt prompt, CancellationToken ct)
        {
            await output.WriteLineAsync(prompt.Message);
            return await Console.In.ReadLineAsync(ct) ?? throw new OperationCanceledException("Authorization input ended.", ct);
        }
    }
}
