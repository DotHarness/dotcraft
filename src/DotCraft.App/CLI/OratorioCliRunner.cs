using DotCraft.Oratorio.Hosting;
using Microsoft.Extensions.Hosting;

namespace DotCraft.CLI;

internal static class OratorioCliRunner
{
    internal static bool IsListenUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https"
        && string.IsNullOrEmpty(uri.UserInfo)
        && uri.AbsolutePath == "/"
        && string.IsNullOrEmpty(uri.Query)
        && string.IsNullOrEmpty(uri.Fragment);

    public static async Task<int> ServeAsync(
        string? stateDirectory,
        string? listenUrl,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        try
        {
            var options = new OratorioHostOptions
            {
                StateRoot = stateDirectory ?? Environment.GetEnvironmentVariable("DOTCRAFT_MANAGED_SERVICE_STATE_ROOT"),
                ListenUrl = listenUrl ?? Environment.GetEnvironmentVariable("DOTCRAFT_MANAGED_SERVICE_URL"),
                ServiceId = Environment.GetEnvironmentVariable("DOTCRAFT_MANAGED_SERVICE_ID"),
                ServiceToken = Environment.GetEnvironmentVariable("DOTCRAFT_MANAGED_SERVICE_TOKEN")
            };
            if (!string.IsNullOrWhiteSpace(options.ListenUrl) && !IsListenUrl(options.ListenUrl))
                throw new ArgumentException("The Oratorio service address must be an absolute HTTP or HTTPS address.");
            var builder = OratorioApplication.CreateBuilder(options);
            await using var app = await OratorioApplication.BuildAsync(builder, cancellationToken: cancellationToken);
            await app.StartAsync(cancellationToken);
            await app.WaitForShutdownAsync(cancellationToken);
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception exception)
        {
            await error.WriteLineAsync($"Oratorio could not start: {exception.Message}");
            return 1;
        }
    }
}
