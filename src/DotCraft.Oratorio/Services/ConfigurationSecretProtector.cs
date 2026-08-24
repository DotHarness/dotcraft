using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace DotCraft.Oratorio.Services;

public interface IConfigurationSecretProtector
{
    bool IsProtected(string? value);
    string Protect(string value);
    string? Unprotect(string? value);
}

public sealed class ConfigurationSecretProtector(
    IWebHostEnvironment environment,
    IOptionsMonitor<SettingsWriteOptions> settingsOptions) : IConfigurationSecretProtector
{
    private const string Prefix = "enc:v1:";
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly object _keyLock = new();
    private byte[]? _key;

    public bool IsProtected(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.StartsWith(Prefix, StringComparison.Ordinal);

    public string Protect(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new ArgumentException("Secret value must not be empty.", nameof(value));
        }

        var key = GetOrCreateKey();
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plaintext = Encoding.UTF8.GetBytes(value);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using (var aes = new AesGcm(key, TagSize))
        {
            aes.Encrypt(nonce, plaintext, ciphertext, tag);
        }

        var payload = new byte[nonce.Length + ciphertext.Length + tag.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
        Buffer.BlockCopy(ciphertext, 0, payload, nonce.Length, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, payload, nonce.Length + ciphertext.Length, tag.Length);
        return Prefix + Convert.ToBase64String(payload);
    }

    public string? Unprotect(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!IsProtected(value))
        {
            return value;
        }

        var payload = Convert.FromBase64String(value[Prefix.Length..]);
        if (payload.Length <= NonceSize + TagSize)
        {
            throw new CryptographicException("Protected secret payload is invalid.");
        }

        var nonce = payload[..NonceSize];
        var tag = payload[^TagSize..];
        var ciphertext = payload[NonceSize..^TagSize];
        var plaintext = new byte[ciphertext.Length];
        using (var aes = new AesGcm(GetOrCreateKey(), TagSize))
        {
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
        }

        return Encoding.UTF8.GetString(plaintext);
    }

    private byte[] GetOrCreateKey()
    {
        if (_key is not null)
        {
            return _key;
        }

        lock (_keyLock)
        {
            if (_key is not null)
            {
                return _key;
            }

            var path = ResolveKeyPath();
            if (File.Exists(path))
            {
                var existing = Convert.FromBase64String(File.ReadAllText(path).Trim());
                if (existing.Length != KeySize)
                {
                    throw new CryptographicException("Oratorio secret key length is invalid.");
                }

                _key = existing;
                return _key;
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _key = RandomNumberGenerator.GetBytes(KeySize);
            File.WriteAllText(path, Convert.ToBase64String(_key), Encoding.UTF8);
            return _key;
        }
    }

    private string ResolveKeyPath()
    {
        var configured = settingsOptions.CurrentValue.SecretKeyPath;

        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        var stateRoot = OratorioStatePaths.ResolveDefaultStateRoot(
            environment.ContentRootPath,
            Environment.GetEnvironmentVariable("DOTCRAFT_MANAGED_SERVICE_STATE_ROOT"));
        return Path.Combine(stateRoot, "secrets.key");
    }
}
