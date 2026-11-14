using System.Text.Json;
using DotCraft.Hub;
using DotCraft.Sdk.Hub;
using HubClient = DotCraft.Sdk.Hub.HubClient;
using HubClientException = DotCraft.Sdk.Hub.HubClientException;

namespace DotCraft.CLI;

internal static class CodexRemoteControlCliRunner
{
    internal static async Task<int> RunAsync(
        string action, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var paths = HubPaths.ForCurrentUser();
        var client = new HubClient(new DotCraftHubClientOptions
        {
            Executable = HubLauncher.CurrentBinary(),
            HubLockPath = paths.LockFilePath,
            StartHubProcess = executable =>
            {
                HubLauncher.StartLauncher(executable, paths);
                return null;
            }
        });
        try
        {
            if (action == "pair")
            {
                var pairing = await client.CreateCodexRemotePairingAsync(cancellationToken);
                await output.WriteLineAsync($"codexRemoteControl.pairing: {pairing.PairingUrl}");
                if (pairing.ManualCode is { } code)
                    await output.WriteLineAsync($"codexRemoteControl.manualCode: {code}");
                await output.WriteLineAsync($"codexRemoteControl.pairingExpires: {pairing.ExpiresAt:O}");
                return 0;
            }

            var status = action switch
            {
                "enable" => await client.EnableCodexRemoteAsync(cancellationToken),
                "disable" => await client.DisableCodexRemoteAsync(cancellationToken),
                _ => await client.GetCodexRemoteAsync(cancellationToken)
            };
            await output.WriteLineAsync($"codexRemoteControl.{status.State}: {status.HostName}");
            if (status.FailureMessage is { } failure)
                await error.WriteLineAsync($"{status.FailureCode}: {failure}");
            return status.State == "failed" ? 1 : 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception exception) when (exception is HubClientException or HttpRequestException or IOException or JsonException)
        {
            var code = (exception as HubClientException)?.Code ?? "codexRemoteControl.failed";
            await error.WriteLineAsync($"{code}: {exception.Message}");
            return 1;
        }
    }
}
