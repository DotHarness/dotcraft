using Xunit;

namespace DotCraft.Tests.Runtime.Plugins;

/// <summary>A fact that needs the built sample bundles. Without them it is reported as skipped, never as passed.</summary>
public sealed class SampleBundlesFactAttribute : FactAttribute
{
    /// <summary>The variable <c>sdk/dotnet/samples/DotNetPluginSample/verify.ps1</c> points at the built bundles with.</summary>
    public const string BundlesVariable = "DOTCRAFT_SAMPLE_BUNDLES";

    public SampleBundlesFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(BundlesVariable)))
        {
            Skip = $"{BundlesVariable} is not set. Run sdk/dotnet/samples/DotNetPluginSample/verify.ps1 to build the bundles and run this suite.";
        }
    }

    /// <summary>Gets the bundles directory. Only valid inside a fact this attribute did not skip.</summary>
    public static string BundlesRoot =>
        Environment.GetEnvironmentVariable(BundlesVariable)
        ?? throw new InvalidOperationException($"{BundlesVariable} is not set.");
}
