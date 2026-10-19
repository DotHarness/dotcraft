using System.Text.Json;
using DotCraft.Configuration;

namespace DotCraft.Agents;

public sealed record ProviderAuthenticationMethod(string Id, string DisplayName, string Kind);

public sealed record ModelBackendDescriptor(
    string Id,
    string DisplayName,
    string DefaultProtocol,
    IReadOnlyList<string> Protocols,
    IReadOnlyList<ProviderAuthenticationMethod> AuthenticationMethods,
    ModelProviderCapabilities Capabilities);

public interface IModelBackend
{
    ModelBackendDescriptor Descriptor { get; }
    CancellationToken Stopping => CancellationToken.None;
    IModelProvider CreateProvider(ModelBackendContext context);
}

public sealed record ModelBackendContext(
    EffectiveModelRuntime Runtime,
    IProviderCredentialStore Credentials,
    CancellationToken Stopping);

public interface IModelRuntimeResolver
{
    EffectiveModelRuntime Normalize(EffectiveModelRuntime runtime);
    IModelProvider Resolve(EffectiveModelRuntime runtime);
}

/// <summary>A host-bound partition; refresh must run inside ModifyAsync to serialize rotated credentials.</summary>
public interface IProviderCredentialStore
{
    Task<JsonElement?> ReadAsync(CancellationToken cancellationToken);
    Task<JsonElement?> ModifyAsync(
        Func<JsonElement?, CancellationToken, Task<JsonElement?>> update,
        CancellationToken cancellationToken);
    Task DeleteAsync(CancellationToken cancellationToken);
}

public sealed record ProviderLoginPrompt(
    string Kind,
    string Message,
    IReadOnlyList<ProviderLoginChoice>? Choices = null);

public sealed record ProviderLoginChoice(string Id, string Label);

public sealed record ProviderLoginEvent(
    string Kind,
    string? Message = null,
    string? Url = null,
    string? UserCode = null);

public interface IProviderLoginInteraction
{
    ValueTask NotifyAsync(ProviderLoginEvent notification, CancellationToken cancellationToken);
    Task<string> PromptAsync(ProviderLoginPrompt prompt, CancellationToken cancellationToken);
}

public interface IProviderAuthenticator
{
    Task<ProviderAuthenticationStatus> GetStatusAsync(CancellationToken cancellationToken);
    Task<ProviderAuthenticationStatus> LoginAsync(
        string method,
        IProviderLoginInteraction interaction,
        CancellationToken cancellationToken);
    Task LogoutAsync(CancellationToken cancellationToken);
}

public sealed class ModelBackendUnavailableException(string backendId)
    : InvalidOperationException($"Model backend '{backendId}' is not available.")
{
    public const string ErrorCode = "ModelBackendUnavailable";
    public string BackendId { get; } = backendId;
}
