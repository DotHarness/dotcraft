using System.Text.Json;

namespace DotCraft.Sessions;

public sealed partial class ThreadStore
{
    private const int ThreadRecoveryFormatVersion = 1;
    private static readonly TimeSpan RecoveryStagingRetention = TimeSpan.FromDays(1);
    private static readonly JsonSerializerOptions RecoveryJsonOptions = SessionJsonOptions.Default;

    internal async Task<ThreadRecoveryPackage> ExportRecoveryAsync(
        string threadId,
        string workspacePath,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        var normalizedWorkspace = NormalizeWorkspace(workspacePath);
        EnsureCraftPathMatchesWorkspace(normalizedWorkspace);
        CleanupStaleRecoveryStaging();

        using var writeLock = await ThreadRolloutWriteGate.AcquireAsync(_botPath, threadId, ct);
        await _rolloutStore.CloseThreadAsync(threadId, ct);
        var thread = await _rolloutStore.LoadThreadAsync(threadId, ct)
            ?? throw new KeyNotFoundException($"Thread '{threadId}' not found.");
        var terminalTurn = ValidateExportThread(thread, normalizedWorkspace);

        ThreadRecoverySnapshot snapshot;
        try
        {
            var modelCodec = new ModelHistoryCodec();
            var modelHistory = await LoadModelHistoryAsync(thread, excludedTurnId: null, ct);
            var encodedHistory = modelHistory.Select(message => modelCodec.Encode(message)).ToList();
            var contextWindowStore = new ResponsesContextWindowStore(StateDatabase);
            var contextWindow = contextWindowStore.GetOrCreate(thread.Id);
            var providerHistory = await LoadProviderHistoryAsync(thread, contextWindow.CurrentWindowId, ct);
            if (!string.Equals(
                    contextWindow.CurrentWindowId,
                    providerHistory.ContextWindowId,
                    StringComparison.Ordinal))
            {
                contextWindowStore.Reconcile(thread.Id, providerHistory.ContextWindowId);
            }

            snapshot = CreateRecoverySnapshot(
                thread,
                terminalTurn,
                encodedHistory,
                providerHistory);
            return await WriteRecoveryPackageFileAsync(
                thread,
                terminalTurn,
                snapshot,
                ct);
        }
        catch (ThreadRecoveryException)
        {
            throw;
        }
        catch (JsonException ex) when (ContainsUnsupportedSchema(ex))
        {
            throw RecoveryFailure(
                ThreadRecoveryErrorCodes.PackageIncompatible,
                "The current Session uses an unsupported recovery schema.",
                ex);
        }
        catch (NotSupportedException ex)
        {
            throw RecoveryFailure(
                ThreadRecoveryErrorCodes.PackageIncompatible,
                "The current Session uses an unsupported recovery schema.",
                ex);
        }
        catch (JsonException ex)
        {
            throw RecoveryFailure(
                ThreadRecoveryErrorCodes.PackageInvalid,
                "The current Session could not be encoded for recovery.",
                ex);
        }
        catch (InvalidDataException ex)
        {
            throw RecoveryFailure(
                ThreadRecoveryErrorCodes.PackageInvalid,
                "The current Session is not valid for recovery.",
                ex);
        }
    }

    private async Task<ThreadRecoveryPackage> WriteRecoveryPackageFileAsync(
        SessionThread thread,
        SessionTurn terminalTurn,
        ThreadRecoverySnapshot snapshot,
        CancellationToken ct)
    {
        var stagingDirectory = GetRecoveryStagingDirectory();
        Directory.CreateDirectory(stagingDirectory);
        var packagePath = Path.Combine(
            stagingDirectory,
            $"{Tools.ThreadArtifactPathResolver.GetCanonicalThreadSegment(thread.Id)}.json");
        var temporaryPath = packagePath + ".tmp";

        try
        {
            TryDeleteFile(temporaryPath);
            await WriteRecoverySnapshotAsync(temporaryPath, snapshot, ct);
            File.Move(temporaryPath, packagePath, overwrite: true);
            var packageInfo = new FileInfo(packagePath);
            return new ThreadRecoveryPackage(
                packagePath,
                thread.Id,
                terminalTurn.Id,
                ThreadRecoveryFormatVersion,
                packageInfo.Length,
                await ComputeSha256Async(packagePath, ct));
        }
        catch
        {
            TryDeleteFile(temporaryPath);
            TryDeleteFile(packagePath);
            throw;
        }
    }

    internal async Task<string> RestoreRecoveryAsync(
        string packagePath,
        string expectedThreadId,
        string workspacePath,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedThreadId);
        var normalizedWorkspace = NormalizeWorkspace(workspacePath);
        EnsureCraftPathMatchesWorkspace(normalizedWorkspace);
        var normalizedPackagePath = NormalizeRecoveryPackagePath(packagePath);
        CleanupStaleRecoveryStaging(exceptPath: normalizedPackagePath);

        var validationDirectory = Path.Combine(
            GetRecoveryStagingDirectory(),
            $".restore-{Guid.NewGuid():N}");
        Directory.CreateDirectory(validationDirectory);

        try
        {
            var validated = await ValidateRecoveryPackageAsync(
                normalizedPackagePath,
                expectedThreadId,
                normalizedWorkspace,
                validationDirectory,
                ct);

            using var writeLock = await ThreadRolloutWriteGate.AcquireAsync(_botPath, expectedThreadId, ct);
            await _rolloutStore.CloseThreadAsync(expectedThreadId, ct);
            if (_rolloutStore.ResolveExistingPath(expectedThreadId) != null)
            {
                throw RecoveryFailure(
                    ThreadRecoveryErrorCodes.TargetExists,
                    $"Thread '{expectedThreadId}' already exists.");
            }

            return await InstallValidatedRecoveryAsync(validated, ct);
        }
        finally
        {
            TryDeleteDirectory(validationDirectory);
        }
    }

    private async Task WriteRecoverySnapshotAsync(
        string targetPath,
        ThreadRecoverySnapshot snapshot,
        CancellationToken ct)
    {
        await using var output = new FileStream(
            targetPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await JsonSerializer.SerializeAsync(output, snapshot, RecoveryJsonOptions, ct);
    }

    private async Task<ValidatedRecoveryPackage> ValidateRecoveryPackageAsync(
        string packagePath,
        string expectedThreadId,
        string normalizedWorkspace,
        string validationDirectory,
        CancellationToken ct)
    {
        try
        {
            await using var input = new FileStream(
                packagePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var snapshot = await JsonSerializer.DeserializeAsync<ThreadRecoverySnapshot>(
                               input,
                               RecoveryJsonOptions,
                               ct)
                           ?? throw RecoveryFailure(
                               ThreadRecoveryErrorCodes.PackageInvalid,
                               "Recovery snapshot is empty.");

            ValidateRecoverySnapshot(
                snapshot,
                expectedThreadId,
                normalizedWorkspace);
            var (thread, rolloutPath) = await MaterializeRecoveryRolloutAsync(
                snapshot,
                validationDirectory,
                ct);

            return new ValidatedRecoveryPackage(
                thread,
                snapshot,
                rolloutPath);
        }
        catch (ThreadRecoveryException)
        {
            throw;
        }
        catch (JsonException ex) when (ContainsUnsupportedSchema(ex))
        {
            throw RecoveryFailure(
                ThreadRecoveryErrorCodes.PackageIncompatible,
                "Recovery snapshot uses an unsupported Session schema.",
                ex);
        }
        catch (NotSupportedException ex)
        {
            throw RecoveryFailure(
                ThreadRecoveryErrorCodes.PackageIncompatible,
                "Recovery snapshot uses an unsupported Session schema.",
                ex);
        }
        catch (JsonException ex)
        {
            throw RecoveryFailure(ThreadRecoveryErrorCodes.PackageInvalid, "Recovery snapshot is invalid JSON.", ex);
        }
        catch (InvalidDataException ex)
        {
            throw RecoveryFailure(ThreadRecoveryErrorCodes.PackageInvalid, "Recovery snapshot is invalid.", ex);
        }
        catch (IOException ex)
        {
            throw RecoveryFailure(ThreadRecoveryErrorCodes.PackageInvalid, "Recovery package could not be read.", ex);
        }
    }

    private async Task<string> InstallValidatedRecoveryAsync(
        ValidatedRecoveryPackage package,
        CancellationToken ct)
    {
        string? rolloutTemporaryPath = null;
        string? rolloutTargetPath = null;
        var rolloutInstalled = false;

        try
        {
            rolloutTargetPath = _rolloutStore.GetExpectedPath(package.Thread.Id, archived: false);
            Directory.CreateDirectory(Path.GetDirectoryName(rolloutTargetPath)!);
            rolloutTemporaryPath = rolloutTargetPath + $".restore-{Guid.NewGuid():N}.tmp";
            File.Copy(package.RolloutPath, rolloutTemporaryPath, overwrite: false);
            File.Move(rolloutTemporaryPath, rolloutTargetPath);
            rolloutInstalled = true;

            var rolloutLength = new FileInfo(rolloutTargetPath).Length;
            var result = new RolloutAppendResult(
                rolloutTargetPath,
                new RolloutWriteReceipt(
                    rolloutLength,
                    0,
                    new Dictionary<string, long>(StringComparer.Ordinal)));
            await TryUpdateThreadProjectionAsync(package.Thread, result, ct);
            new ResponsesContextWindowStore(StateDatabase).Reconcile(
                package.Thread.Id,
                package.Snapshot.ProviderHistory.ContextWindowId);
            return package.Thread.Id;
        }
        catch
        {
            TryDeleteFile(rolloutTemporaryPath);
            if (rolloutInstalled)
                TryDeleteFile(rolloutTargetPath);
            if (rolloutInstalled)
            {
                try { _metadataStore.DeleteThread(package.Thread.Id); }
                catch { /* Best-effort rollback of rebuildable projection state. */ }
            }
            throw;
        }
    }

    private static bool ContainsUnsupportedSchema(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current is NotSupportedException)
                return true;
        }
        return false;
    }

}
