using DotCraft.Contributions;
using DotCraft.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DotCraft.Runtime;

internal static class DotNetExtensionRuntimeRegistration
{
    internal static void AddDotNetExtensionRuntimeManager(this IServiceCollection services,
        Func<IServiceProvider, DotNetExtensionRuntimeManager> create)
    {
        services.TryAddSingleton<ContributionRegistry>();
        services.TryAddSingleton<IContributionView>(sp => sp.GetRequiredService<ContributionRegistry>());
        services.AddSingleton(create);
        services.AddSingleton<IDotNetExtensionRuntimeCoordinator>(sp => sp.GetRequiredService<DotNetExtensionRuntimeManager>());
    }
}
