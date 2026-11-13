using DotCraft.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace DotCraft.CSharp;

public static class CSharpServiceCollectionExtensions
{
    public static IServiceCollection AddDotCraftCSharp(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<CSharpToolSource>();
        services.AddSingleton<IToolSource>(sp => sp.GetRequiredService<CSharpToolSource>());
        return services;
    }
}
