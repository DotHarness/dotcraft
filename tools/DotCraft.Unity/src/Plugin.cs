using System.Security.Cryptography;
using System.Runtime.Loader;
using DotCraft.Plugins;
using DotCraft.Tools;

namespace DotCraft.Unity;

/// <summary>Registers native Unity attach tools without changing host composition.</summary>
public sealed class Plugin : IDotCraftPlugin
{
    /// <inheritdoc />
    public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
            throw new PlatformNotSupportedException("Unity attach requires Windows x64.");
        // The host may carry the same NuGet compiler; pin these private assemblies to this bundle.
        var loadContext = AssemblyLoadContext.GetLoadContext(typeof(Plugin).Assembly)!;
        foreach (var name in new[] { "Microsoft.CodeAnalysis.dll", "Microsoft.CodeAnalysis.CSharp.dll" })
            loadContext.LoadFromAssemblyPath(Path.Combine(context.ContentRoot, "lib", name));
        var bundled = Path.Combine(context.ContentRoot, "native", "DotCraft.Unity.Native.dll");
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(bundled)));
        var native = Path.Combine(context.DataRoot, "native", hash, "DotCraft.Unity.Native.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(native)!);
        if (!File.Exists(native)) File.Copy(bundled, native);
        var generation = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(context.Plugin.GenerationId)))[..16];
        var root = Path.Combine(context.DataRoot, "g", generation);
        var service = new UnityAttachService(root, native);
        context.Lifetime.OwnAsync(service);
        context.Contributions.Add<IToolSource>(new UnityToolSource(service, context.WorkspaceRoot));
        return ValueTask.CompletedTask;
    }
}
