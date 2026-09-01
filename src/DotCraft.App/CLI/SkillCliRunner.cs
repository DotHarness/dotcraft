using System.Text.Json;
using DotCraft.Skills;

namespace DotCraft.CLI;

internal static class SkillCliRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static Task<int> VerifyAsync(
        string craftPath,
        string candidatePath,
        string? skillName,
        bool json,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default) => ExecuteAsync(
            json,
            output,
            error,
            async () =>
            {
                var installer = new SkillInstallService(new SkillsLoader(craftPath));
                var result = await installer.VerifyAsync(
                    new SkillInstallVerifyRequest(candidatePath, skillName),
                    cancellationToken).ConfigureAwait(false);
                await WriteResultAsync(json, output, result).ConfigureAwait(false);
                return result.IsValid ? 0 : 1;
            });

    public static Task<int> InstallAsync(
        string craftPath,
        string candidatePath,
        string? skillName,
        string? source,
        bool overwrite,
        bool json,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default) => ExecuteAsync(
            json,
            output,
            error,
            async () =>
            {
                if (!Directory.Exists(craftPath))
                    throw new InvalidOperationException($"DotCraft workspace not found: {craftPath}");

                var installer = new SkillInstallService(new SkillsLoader(craftPath));
                var result = await installer.InstallAsync(
                    new SkillInstallRequest(candidatePath, skillName, overwrite, source),
                    cancellationToken).ConfigureAwait(false);
                await WriteResultAsync(json, output, result).ConfigureAwait(false);
                return result.Success ? 0 : 1;
            });

    private static async Task<int> ExecuteAsync(
        bool json,
        TextWriter output,
        TextWriter error,
        Func<Task<int>> action)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (json)
            {
                await output.WriteLineAsync(JsonSerializer.Serialize(
                    new { success = false, errors = new[] { ex.Message } },
                    JsonOptions)).ConfigureAwait(false);
            }
            else
            {
                await error.WriteLineAsync(ex.Message).ConfigureAwait(false);
            }
            return 1;
        }
    }

    private static async Task WriteResultAsync(bool json, TextWriter output, object result)
    {
        if (json)
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
}
