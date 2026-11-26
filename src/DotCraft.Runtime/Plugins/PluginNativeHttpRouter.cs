using DotCraft.Agents;
using DotCraft.Configuration;

namespace DotCraft.Runtime;

internal sealed class PluginNativeHttpRouter(
    IProviderNativeHttpRouter target, PluginInvocation invocation, string backendId) : IProviderNativeHttpRouter
{
    private readonly PluginTarget<IProviderNativeHttpRouter> _target = invocation.Capture(target);

    public ProviderHttpRoute ResolveRoute(EffectiveModelRuntime runtime)
    {
        try
        {
            using var lease = invocation.Enter();
            var route = _target.Value.ResolveRoute(runtime);
            return new ProviderHttpRoute(route.Protocol, route.EndPoint, route.Model);
        }
        catch (ArgumentException) { throw new ArgumentException("The backend does not route the selected model."); }
        catch (OperationCanceledException) { throw new OperationCanceledException(invocation.Stopping); }
        catch (Exception) { throw new ModelBackendUnavailableException(backendId); }
    }

    public async Task<IReadOnlyDictionary<string, string>> AuthenticateAsync(EffectiveModelRuntime runtime, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, invocation.Stopping);
        try
        {
            using var lease = invocation.Enter();
            linked.Token.ThrowIfCancellationRequested();
            var headers = await _target.Value.AuthenticateAsync(runtime, linked.Token).ConfigureAwait(false);
            return new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase);
        }
        catch (ProviderAuthenticationException exception)
        {
            throw new ProviderAuthenticationException(exception.Code, "The backend could not authenticate the upstream request.");
        }
        catch (OperationCanceledException) { throw new OperationCanceledException(linked.Token); }
        catch (Exception) { throw new ModelBackendUnavailableException(backendId); }
    }
}
