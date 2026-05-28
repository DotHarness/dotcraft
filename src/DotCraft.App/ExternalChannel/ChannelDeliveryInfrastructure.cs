using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using DotCraft.Security;

namespace DotCraft.ExternalChannel;

internal sealed class ChannelMediaArtifact
{
    public string Id { get; init; } = string.Empty;

    public string Kind { get; init; } = string.Empty;

    public string MediaType { get; init; } = "application/octet-stream";

    public long ByteLength { get; init; }

    public string? FileName { get; init; }

    public string SourceKind { get; init; } = string.Empty;

    public string? ResolvedPath { get; init; }

    public string? Url { get; init; }

    public string? Sha256 { get; init; }

    public bool OwnsResolvedPath { get; init; }
}

internal sealed class ChannelMediaResolutionResult
{
    public required ChannelMediaArtifact Artifact { get; init; }

    public bool CleanupOnCompletion { get; init; }
}

internal interface IChannelMediaArtifactStore
{
    Task<ChannelMediaArtifact?> GetAsync(string artifactId, CancellationToken cancellationToken = default);

    Task RegisterAsync(ChannelMediaArtifact artifact, CancellationToken cancellationToken = default);

    Task DeleteAsync(string artifactId, CancellationToken cancellationToken = default);
}

internal interface IChannelMediaResolver
{
    Task<ChannelMediaResolutionResult> ResolveAsync(
        ChannelOutboundMessage message,
        CancellationToken cancellationToken = default);
}

internal interface IChannelMessageDispatcher
{
    Task<ExtChannelSendResult> DeliverAsync(
        IAppServerTransport transport,
        AppServerConnection connection,
        string channelName,
        string target,
        ChannelOutboundMessage message,
        object? metadata,
        CancellationToken cancellationToken = default);
}

internal sealed record ExternalChannelDeliveryDependencies(
    IChannelMediaArtifactStore ArtifactStore,
    IChannelMediaResolver MediaResolver,
    IChannelMessageDispatcher MessageDispatcher);

internal sealed class FileSystemChannelMediaArtifactStore(string rootPath) : IChannelMediaArtifactStore
{
    private readonly ConcurrentDictionary<string, ChannelMediaArtifact> _artifacts = new(StringComparer.Ordinal);

    public Task<ChannelMediaArtifact?> GetAsync(string artifactId, CancellationToken cancellationToken = default)
    {
        _artifacts.TryGetValue(artifactId, out var artifact);
        return Task.FromResult(artifact);
    }

    public Task RegisterAsync(ChannelMediaArtifact artifact, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(rootPath);
        _artifacts[artifact.Id] = artifact;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string artifactId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_artifacts.TryRemove(artifactId, out var artifact))
            return Task.CompletedTask;

        if (artifact.OwnsResolvedPath && !string.IsNullOrWhiteSpace(artifact.ResolvedPath) && File.Exists(artifact.ResolvedPath))
        {
            try
            {
                File.Delete(artifact.ResolvedPath);
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }

        return Task.CompletedTask;
    }
}

internal sealed class ChannelMediaResolver(
    IChannelMediaArtifactStore artifactStore,
    string tempRootPath,
    FileAccessGuard fileAccessGuard) : IChannelMediaResolver
{
    public async Task<ChannelMediaResolutionResult> ResolveAsync(
        ChannelOutboundMessage message,
        CancellationToken cancellationToken = default)
    {
        ValidateMessageShape(message);

        var source = message.Source ?? throw new InvalidOperationException("Message source is required for non-text delivery.");
        ValidateSourceShape(source);
        var kind = source.Kind.Trim();

        return kind switch
        {
            "artifactId" => await ResolveArtifactIdAsync(source, cancellationToken),
            "hostPath" => await ResolveHostPathAsync(message, source, cancellationToken),
            "dataBase64" => await ResolveBase64Async(message, source, cancellationToken),
            "url" => ResolveUrl(message, source),
            _ => throw new InvalidOperationException($"Unsupported media source kind '{kind}'.")
        };
    }

    private async Task<ChannelMediaResolutionResult> ResolveArtifactIdAsync(ChannelMediaSource source, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(source.ArtifactId))
            throw new InvalidOperationException("artifactId source requires artifactId.");

        var artifact = await artifactStore.GetAsync(source.ArtifactId, cancellationToken)
            ?? throw new FileNotFoundException($"Channel media artifact '{source.ArtifactId}' was not found.");

        return new ChannelMediaResolutionResult { Artifact = artifact };
    }

    private async Task<ChannelMediaResolutionResult> ResolveHostPathAsync(
        ChannelOutboundMessage message,
        ChannelMediaSource source,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(source.HostPath))
            throw new InvalidOperationException("hostPath source requires hostPath.");

        var fullPath = fileAccessGuard.ResolvePath(source.HostPath);
        var validationError = await fileAccessGuard.ValidatePathAsync(
            fullPath,
            "read-for-delivery",
            source.HostPath,
            cancellationToken);
        if (validationError != null)
            throw new InvalidOperationException(validationError);

        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Media file '{fullPath}' was not found.");

        var fileInfo = new FileInfo(fullPath);
        var artifact = new ChannelMediaArtifact
        {
            Id = CreateArtifactId(),
            Kind = message.Kind,
            MediaType = ResolveMediaType(message, fullPath),
            ByteLength = fileInfo.Length,
            FileName = ResolveFileName(message, fullPath),
            SourceKind = "hostPath",
            ResolvedPath = fullPath,
            Sha256 = await ComputeSha256Async(fullPath, cancellationToken)
        };
        await artifactStore.RegisterAsync(artifact, cancellationToken);
        return new ChannelMediaResolutionResult { Artifact = artifact };
    }

    private async Task<ChannelMediaResolutionResult> ResolveBase64Async(
        ChannelOutboundMessage message,
        ChannelMediaSource source,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(source.DataBase64))
            throw new InvalidOperationException("dataBase64 source requires dataBase64.");

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(source.DataBase64);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("dataBase64 source did not contain valid base64.", ex);
        }

        Directory.CreateDirectory(tempRootPath);
        var artifactId = CreateArtifactId();
        var extension = ResolveExtension(message);
        var tempPath = Path.Combine(tempRootPath, $"{artifactId}{extension}");
        var registered = false;
        try
        {
            await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken);

            var artifact = new ChannelMediaArtifact
            {
                Id = artifactId,
                Kind = message.Kind,
                MediaType = ResolveMediaType(message, tempPath),
                ByteLength = bytes.LongLength,
                FileName = ResolveFileName(message, tempPath),
                SourceKind = "dataBase64",
                ResolvedPath = tempPath,
                Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                OwnsResolvedPath = true
            };
            await artifactStore.RegisterAsync(artifact, cancellationToken);
            registered = true;
            return new ChannelMediaResolutionResult
            {
                Artifact = artifact,
                CleanupOnCompletion = true
            };
        }
        finally
        {
            if (!registered && File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                    // Best-effort cleanup only.
                }
            }
        }
    }

    private static ChannelMediaResolutionResult ResolveUrl(ChannelOutboundMessage message, ChannelMediaSource source)
    {
        if (string.IsNullOrWhiteSpace(source.Url))
            throw new InvalidOperationException("url source requires url.");
        if (!Uri.TryCreate(source.Url, UriKind.Absolute, out var uri))
            throw new InvalidOperationException("url source requires an absolute URL.");
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("url source only supports http/https URLs.");
        }

        var artifact = new ChannelMediaArtifact
        {
            Id = CreateArtifactId(),
            Kind = message.Kind,
            MediaType = ResolveMediaType(message, source.Url),
            FileName = ResolveFileName(message, source.Url),
            SourceKind = "url",
            Url = source.Url
        };
        return new ChannelMediaResolutionResult { Artifact = artifact };
    }

    private static void ValidateMessageShape(ChannelOutboundMessage message)
    {
        var kind = message.Kind?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(kind))
            throw new InvalidOperationException("Message kind is required.");

        if (string.Equals(kind, "text", StringComparison.OrdinalIgnoreCase))
        {
            if (message.Source != null)
                throw new InvalidOperationException("Text delivery must not include a media source.");
            return;
        }

        if (message.Source == null)
            throw new InvalidOperationException($"Message source is required for '{kind}' delivery.");
    }

    private static void ValidateSourceShape(ChannelMediaSource source)
    {
        var kind = source.Kind?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(kind))
            throw new InvalidOperationException("Media source kind is required.");

        var populated = 0;
        if (!string.IsNullOrWhiteSpace(source.HostPath)) populated++;
        if (!string.IsNullOrWhiteSpace(source.Url)) populated++;
        if (!string.IsNullOrWhiteSpace(source.DataBase64)) populated++;
        if (!string.IsNullOrWhiteSpace(source.ArtifactId)) populated++;

        if (populated != 1)
            throw new InvalidOperationException("Media source must specify exactly one concrete source field.");

        var matchesKind = kind switch
        {
            "hostPath" => !string.IsNullOrWhiteSpace(source.HostPath),
            "url" => !string.IsNullOrWhiteSpace(source.Url),
            "dataBase64" => !string.IsNullOrWhiteSpace(source.DataBase64),
            "artifactId" => !string.IsNullOrWhiteSpace(source.ArtifactId),
            _ => false
        };

        if (!matchesKind)
            throw new InvalidOperationException($"Media source kind '{kind}' does not match the populated source field.");
    }

    private static string ResolveMediaType(ChannelOutboundMessage message, string pathOrUrl)
    {
        if (!string.IsNullOrWhiteSpace(message.MediaType))
            return message.MediaType;

        var extension = Path.GetExtension(pathOrUrl).ToLowerInvariant();
        return extension switch
        {
            ".pdf" => "application/pdf",
            ".txt" => "text/plain",
            ".ogg" => "audio/ogg",
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".mp4" => "video/mp4",
            _ => "application/octet-stream"
        };
    }

    private static string? ResolveFileName(ChannelOutboundMessage message, string pathOrUrl)
    {
        if (!string.IsNullOrWhiteSpace(message.FileName))
            return message.FileName;

        var candidate = Path.GetFileName(pathOrUrl);
        return string.IsNullOrWhiteSpace(candidate) ? null : candidate;
    }

    private static string ResolveExtension(ChannelOutboundMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.FileName))
            return Path.GetExtension(message.FileName);

        return message.MediaType?.ToLowerInvariant() switch
        {
            "application/pdf" => ".pdf",
            "text/plain" => ".txt",
            "audio/ogg" => ".ogg",
            "audio/mpeg" => ".mp3",
            "audio/wav" => ".wav",
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/gif" => ".gif",
            "video/mp4" => ".mp4",
            _ => string.Empty
        };
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string CreateArtifactId() => $"artifact_{Guid.NewGuid():N}";
}

internal sealed class ExternalChannelMessageDispatcher(
    IChannelMediaResolver mediaResolver,
    IChannelMediaArtifactStore artifactStore)
    : IChannelMessageDispatcher
{
    public async Task<ExtChannelSendResult> DeliverAsync(
        IAppServerTransport transport,
        AppServerConnection connection,
        string channelName,
        string target,
        ChannelOutboundMessage message,
        object? metadata,
        CancellationToken cancellationToken = default)
    {
        if (!connection.SupportsStructuredDelivery)
        {
            return Failure(
                "UnsupportedDeliveryKind",
                $"Channel '{channelName}' does not advertise structured delivery for '{message.Kind}'.");
        }

        if (string.Equals(message.Kind, "text", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var response = await transport.SendClientRequestAsync(
                    AppServerMethods.ExtChannelSend,
                    new ExtChannelSendParams
                    {
                        Target = target,
                        Message = message,
                        Metadata = metadata
                    },
                    cancellationToken,
                    TimeSpan.FromSeconds(10));
                return ParseResult(response);
            }
            catch (Exception ex)
            {
                return Failure("AdapterDeliveryFailed", ex.Message);
            }
        }

        var constraints = GetConstraints(connection.DeliveryCapabilities, message.Kind);
        if (constraints == null)
        {
            return Failure(
                "UnsupportedDeliveryKind",
                $"Channel '{channelName}' does not support '{message.Kind}' delivery.");
        }

        if (!string.IsNullOrWhiteSpace(message.Caption) && constraints.SupportsCaption != true)
            return Failure("UnsupportedDeliveryKind", $"Channel '{channelName}' does not support captions for '{message.Kind}'.");

        ChannelMediaResolutionResult? resolution = null;
        try
        {
            resolution = await mediaResolver.ResolveAsync(message, cancellationToken);
            var preparedSource = await PrepareSourceForAdapterAsync(message, constraints, resolution, cancellationToken);
            if (preparedSource.Error != null)
                return preparedSource.Error;

            var validationError = ValidateArtifactAgainstConstraints(resolution.Artifact, constraints);
            if (validationError != null)
                return validationError;

            var request = new ExtChannelSendParams
            {
                Target = target,
                Message = new ChannelOutboundMessage
                {
                    Kind = message.Kind,
                    Text = message.Text,
                    Caption = message.Caption,
                    FileName = message.FileName ?? resolution.Artifact.FileName,
                    MediaType = message.MediaType ?? resolution.Artifact.MediaType,
                    Source = preparedSource.Source
                },
                Metadata = metadata
            };

            var response = await transport.SendClientRequestAsync(
                AppServerMethods.ExtChannelSend,
                request,
                cancellationToken,
                TimeSpan.FromSeconds(10));

            return ParseResult(response);
        }
        catch (FileNotFoundException ex)
        {
            return Failure("MediaArtifactNotFound", ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Failure("MediaResolutionFailed", ex.Message);
        }
        catch (Exception ex)
        {
            return Failure("AdapterDeliveryFailed", ex.Message);
        }
        finally
        {
            if (resolution?.CleanupOnCompletion == true)
                await artifactStore.DeleteAsync(resolution.Artifact.Id, cancellationToken);
        }
    }

    private static ChannelMediaConstraints? GetConstraints(ChannelDeliveryCapabilities? capabilities, string kind)
    {
        var media = capabilities?.Media;
        return kind.ToLowerInvariant() switch
        {
            "file" => media?.File,
            "audio" => media?.Audio,
            "image" => media?.Image,
            "video" => media?.Video,
            _ => null
        };
    }

    private static async Task<(ChannelMediaSource? Source, ExtChannelSendResult? Error)> PrepareSourceForAdapterAsync(
        ChannelOutboundMessage message,
        ChannelMediaConstraints constraints,
        ChannelMediaResolutionResult resolution,
        CancellationToken cancellationToken)
    {
        var sourceKind = resolution.Artifact.SourceKind;
        if (string.Equals(sourceKind, "url", StringComparison.OrdinalIgnoreCase))
        {
            if (constraints.SupportsUrl == true)
            {
                return (new ChannelMediaSource
                {
                    Kind = "url",
                    Url = resolution.Artifact.Url
                }, null);
            }

            return (null, Failure("UnsupportedMediaSource", "Adapter does not support URL media sources."));
        }

        if (constraints.SupportsHostPath == true && !string.IsNullOrWhiteSpace(resolution.Artifact.ResolvedPath))
        {
            return (new ChannelMediaSource
            {
                Kind = "hostPath",
                HostPath = resolution.Artifact.ResolvedPath
            }, null);
        }

        if (constraints.SupportsBase64 == true && !string.IsNullOrWhiteSpace(resolution.Artifact.ResolvedPath))
        {
            var bytes = await File.ReadAllBytesAsync(resolution.Artifact.ResolvedPath, cancellationToken);
            return (new ChannelMediaSource
            {
                Kind = "dataBase64",
                DataBase64 = Convert.ToBase64String(bytes)
            }, null);
        }

        return (null, Failure("UnsupportedMediaSource", $"Adapter does not support source kind '{sourceKind}' for '{message.Kind}'."));
    }

    private static ExtChannelSendResult? ValidateArtifactAgainstConstraints(ChannelMediaArtifact artifact, ChannelMediaConstraints constraints)
    {
        if (string.Equals(artifact.SourceKind, "url", StringComparison.OrdinalIgnoreCase)
            && constraints.MaxBytes is > 0)
        {
            return Failure(
                "MediaResolutionFailed",
                "Remote URL media cannot be validated against maxBytes.");
        }

        if (constraints.MaxBytes is > 0 && artifact.ByteLength > constraints.MaxBytes.Value)
        {
            return Failure("MediaTooLarge", $"Media exceeds maxBytes limit ({constraints.MaxBytes.Value}).");
        }

        if (constraints.AllowedMimeTypes is { Count: > 0 } mimeTypes &&
            !mimeTypes.Contains(artifact.MediaType, StringComparer.OrdinalIgnoreCase))
        {
            return Failure("MediaTypeNotAllowed", $"Media type '{artifact.MediaType}' is not allowed.");
        }

        var extension = artifact.FileName != null ? Path.GetExtension(artifact.FileName) : null;
        if (!string.IsNullOrWhiteSpace(extension) &&
            constraints.AllowedExtensions is { Count: > 0 } extensions &&
            !extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return Failure("MediaTypeNotAllowed", $"Media extension '{extension}' is not allowed.");
        }

        return null;
    }

    private static ExtChannelSendResult ParseResult(AppServerIncomingMessage response)
    {
        if (response.Result is not { } result || result.ValueKind != JsonValueKind.Object)
            return Failure("AdapterProtocolViolation", "Adapter returned an invalid response payload.");

        var parsed = System.Text.Json.JsonSerializer.Deserialize<ExtChannelSendResult>(
            result.GetRawText(),
            SessionWireJsonOptions.Default);

        if (parsed == null)
            return Failure("AdapterProtocolViolation", "Adapter returned an empty response payload.");

        if (!parsed.Delivered && string.IsNullOrWhiteSpace(parsed.ErrorCode))
            parsed.ErrorCode = "AdapterDeliveryFailed";
        if (!parsed.Delivered &&
            string.IsNullOrWhiteSpace(parsed.ErrorMessage) &&
            result.TryGetProperty("error", out var legacyError) &&
            legacyError.ValueKind == JsonValueKind.String)
        {
            parsed.ErrorMessage = legacyError.GetString();
        }

        return parsed;
    }

    private static ExtChannelSendResult Failure(string errorCode, string errorMessage) =>
        new()
        {
            Delivered = false,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage
        };
}
