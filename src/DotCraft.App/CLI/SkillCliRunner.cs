using System.Text.Json;
using DotCraft.Skills;

namespace DotCraft.CLI;

/// <summary>
/// Runs non-interactive skill maintenance commands for agent-assisted workflows.
/// </summary>
public static class SkillCliRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    /// <summary>
    /// Executes the parsed skill command and returns a process exit code.
    /// </summary>
    public static async Task<int> RunAsync(
        string craftPath,
        CommandLineArgs args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(args.SkillCommand))
        {
            await WriteErrorAsync(args, output, error, "Missing skill command. Expected 'verify' or 'install'.").ConfigureAwait(false);
            return 1;
        }

        if (string.IsNullOrWhiteSpace(args.SkillCandidatePath))
        {
            await WriteErrorAsync(args, output, error, "Missing --candidate path.").ConfigureAwait(false);
            return 1;
        }

        var command = args.SkillCommand.Trim().ToLowerInvariant();
        if (command == "install" && !Directory.Exists(craftPath))
        {
            await WriteErrorAsync(args, output, error, $"DotCraft workspace not found: {craftPath}").ConfigureAwait(false);
            return 1;
        }

        var loader = new SkillsLoader(craftPath);
        var installer = new SkillInstallService(loader);

        try
        {
            switch (command)
            {
                case "verify":
                {
                    var result = await installer.VerifyAsync(
                        new SkillInstallVerifyRequest(args.SkillCandidatePath, args.SkillName),
                        cancellationToken).ConfigureAwait(false);
                    await WriteResultAsync(args, output, result).ConfigureAwait(false);
                    return result.IsValid ? 0 : 1;
                }
                case "install":
                {
                    var result = await installer.InstallAsync(
                        new SkillInstallRequest(
                            args.SkillCandidatePath,
                            args.SkillName,
                            args.SkillOverwrite,
                            args.SkillSource),
                        cancellationToken).ConfigureAwait(false);
                    await WriteResultAsync(args, output, result).ConfigureAwait(false);
                    return result.Success ? 0 : 1;
                }
                default:
                    await WriteErrorAsync(args, output, error, $"Unknown skill command '{args.SkillCommand}'. Expected 'verify' or 'install'.").ConfigureAwait(false);
                    return 1;
            }
        }
        catch (Exception ex)
        {
            await WriteErrorAsync(args, output, error, ex.Message).ConfigureAwait(false);
            return 1;
        }
    }

    private static async Task WriteResultAsync(CommandLineArgs args, TextWriter output, object result)
    {
        if (args.SkillJson)
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(result, JsonOptions)).ConfigureAwait(false);
            return;
        }

        switch (result)
        {
            case SkillInstallVerificationResult verification when verification.IsValid:
                await output.WriteLineAsync($"Skill candidate is valid: {verification.SkillName}").ConfigureAwait(false);
                break;
            case SkillInstallVerificationResult verification:
                await output.WriteLineAsync($"Skill candidate is invalid: {verification.CandidatePath}").ConfigureAwait(false);
                foreach (var item in verification.Errors)
                    await output.WriteLineAsync($"- {item}").ConfigureAwait(false);
                break;
            case SkillInstallResult install when install.Success:
                await output.WriteLineAsync($"Skill installed: {install.SkillName}").ConfigureAwait(false);
                await output.WriteLineAsync($"Target: {install.TargetDir}").ConfigureAwait(false);
                await output.WriteLineAsync($"Fingerprint: {install.SourceFingerprint}").ConfigureAwait(false);
                break;
            case SkillInstallResult install:
                await output.WriteLineAsync($"Skill install failed: {install.SkillName ?? install.CandidatePath}").ConfigureAwait(false);
                foreach (var item in install.Errors)
                    await output.WriteLineAsync($"- {item}").ConfigureAwait(false);
                break;
        }
    }

    private static async Task WriteErrorAsync(CommandLineArgs args, TextWriter output, TextWriter error, string message)
    {
        if (args.SkillJson)
        {
            var json = JsonSerializer.Serialize(new { success = false, errors = new[] { message } }, JsonOptions);
            await output.WriteLineAsync(json).ConfigureAwait(false);
            return;
        }

        await error.WriteLineAsync(message).ConfigureAwait(false);
    }
}
