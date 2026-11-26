using DotCraft.Agents;
using DotCraft.Configuration;

namespace DotCraft.ModelService;

internal static class ModelServiceBackendRouting
{
    public static (EffectiveModelRuntime Runtime, Uri Target, string Operation) Resolve(
        IModelProvider backend, IProviderNativeHttpRouter router, EffectiveModelRuntime selected)
    {
        if (string.IsNullOrWhiteSpace(selected.Model))
            throw new ArgumentException("A model is required.");

        var runtime = backend.GetService(typeof(IProviderModelRuntimeResolver)) is IProviderModelRuntimeResolver resolver
            ? resolver.ResolveRuntime(selected) : selected;
        var route = router.ResolveRoute(runtime);
        var operation = route.Protocol switch
        {
            ModelProviderProtocols.Anthropic => "v1/messages",
            ModelProviderProtocols.OpenAIResponses => "responses",
            ModelProviderProtocols.OpenAIChatCompletions => "chat/completions",
            _ => throw new ArgumentException("The model does not use a supported native protocol.")
        };
        var configured = selected.ConfiguredProtocol ?? selected.Protocol;
        if ((!string.IsNullOrWhiteSpace(configured) && configured != "auto" && route.Protocol != configured)
            || route.Model != selected.Model || runtime.Model != selected.Model
            || !Uri.TryCreate(route.EndPoint, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme is not ("http" or "https") || endpoint.Host.Length == 0
            || endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0)
            throw new ArgumentException("The model does not resolve to an allowed native HTTP route.");

        var target = new Uri(new Uri(endpoint.AbsoluteUri.TrimEnd('/') + "/"), operation);
        return (runtime with { Protocol = route.Protocol, EndPoint = endpoint.AbsoluteUri }, target, operation);
    }
}
