using DotCraft.Auth.OpenAI;
using DotCraft.ModelService;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DotCraft.CLI;

internal static class ModelServiceCliRunner
{
    public static async Task<int> ServeAsync(string stateDirectory, string listenUrl, CancellationToken cancellationToken)
    {
        FileModelServiceAccess.LoadConfiguration(stateDirectory);
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddSingleton<IModelServiceAccess>(new FileModelServiceAccess(stateDirectory));
        builder.Services.AddDotCraftModelService();
        await using var app = builder.Build();
        app.Urls.Add(listenUrl);
        app.MapDotCraftModelService();
        await app.StartAsync(cancellationToken);
        await app.WaitForShutdownAsync(cancellationToken);
        return 0;
    }

    public static int CreateClient(string directory, string name, string[] providers, string outputPath, TextWriter output)
    {
        var config = FileModelServiceAccess.LoadConfiguration(directory);
        foreach (var id in providers)
            if (!config.Providers.ContainsKey(id))
                throw new ArgumentException($"Unknown provider '{id}'.");
        using var file = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(outputPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var created = new ModelServiceClientStore(directory).Create(name, providers);
        using var writer = new StreamWriter(file);
        writer.Write(created.Token);
        output.WriteLine($"Created client {created.Client.Id}; token saved to {Path.GetFullPath(outputPath)}.");
        return 0;
    }

    public static OpenAIAuthManager Authentication(string directory) =>
        new(new OpenAITokenStore(Path.Combine(directory, "credentials")));

    public static async Task<int> LoginAsync(string directory, bool noBrowser, TextWriter output, CancellationToken cancellationToken)
    {
        var status = await Authentication(directory).LoginAsync(!noBrowser, url => output.WriteLine(url), cancellationToken);
        output.WriteLine(status.LoggedIn ? "Signed in." : "Sign-in failed.");
        return status.LoggedIn ? 0 : 1;
    }
}
