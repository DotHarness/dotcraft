using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Security.AccessControl;
using DotCraft.Agents;

namespace DotCraft.Configuration;

public sealed class ProviderCredentialStore : IProviderCredentialStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ProviderCredentialStore(string root, string backendId, string providerId)
    {
        var identity = string.Join('\n', backendId, providerId.ToLowerInvariant());
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
        _path = Path.Combine(root, "provider-credentials", key + ".json");
    }

    public async Task<JsonElement?> ReadAsync(CancellationToken cancellationToken)
    {
        await using var held = await AcquireAsync(cancellationToken).ConfigureAwait(false);
        return Read().Credential?.Clone();
    }

    public async Task<JsonElement?> ModifyAsync(
        Func<JsonElement?, CancellationToken, Task<JsonElement?>> update, CancellationToken cancellationToken)
    {
        await using var held = await AcquireAsync(cancellationToken).ConfigureAwait(false);
        var before = Read();
        var credential = await update(before.Credential?.Clone(), cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var next = new StoredCredential(before.Revision + 1, credential?.Clone());
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = CreateOwnerOnlyFile(temporary))
                await JsonSerializer.SerializeAsync(stream, next, cancellationToken: cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
        return credential?.Clone();
    }

    public async Task DeleteAsync(CancellationToken cancellationToken) =>
        await ModifyAsync((_, _) => Task.FromResult<JsonElement?>(null), cancellationToken).ConfigureAwait(false);

    private StoredCredential Read() => File.Exists(_path)
        ? JsonSerializer.Deserialize<StoredCredential>(File.ReadAllText(_path))
            ?? throw new InvalidDataException("The provider credential record is invalid.")
        : new StoredCredential(0, null);

    private async Task<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var file = new FileStream(_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                    return new LockLease(file, _gate);
                }
                catch (IOException)
                {
                    await Task.Delay(25, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch
        {
            _gate.Release();
            throw;
        }
    }

    private static FileStream CreateOwnerOnlyFile(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new FileStream(path, new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
            });
        }
        return CreateWindowsOwnerOnlyFile(path);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static FileStream CreateWindowsOwnerOnlyFile(string path)
    {
        var owner = System.Security.Principal.WindowsIdentity.GetCurrent().User
            ?? throw new UnauthorizedAccessException("The current user cannot be determined.");
        var security = new FileSecurity();
        security.SetAccessRuleProtection(true, false);
        security.SetOwner(owner);
        security.AddAccessRule(new FileSystemAccessRule(
            owner, FileSystemRights.FullControl, AccessControlType.Allow));
        return new FileInfo(path).Create(
            FileMode.CreateNew, FileSystemRights.Write, FileShare.None, 4096, FileOptions.None, security);
    }

    private sealed record StoredCredential(long Revision, JsonElement? Credential);

    private sealed class LockLease(FileStream file, SemaphoreSlim gate) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await file.DisposeAsync().ConfigureAwait(false);
            gate.Release();
        }
    }
}
