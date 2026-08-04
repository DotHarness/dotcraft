using System.Reflection;
using DotCraft.Sdk.DynamicTools;
using Xunit;

namespace DotCraft.Sdk.Tests;

public sealed class PublicApiShapeTests
{
    [Fact]
    public void Public_api_has_only_the_new_namespaces_and_type_names()
    {
        Assembly assembly = typeof(DotCraftClient).Assembly;
        Type[] publicTypes = assembly.GetExportedTypes();

        Assert.DoesNotContain(publicTypes, type => type.Namespace is not null &&
            (type.Namespace.StartsWith("DotCraft.Sdk.AppServer", StringComparison.Ordinal) ||
             type.Namespace.StartsWith("DotCraft.Sdk.Tools", StringComparison.Ordinal)));

        string[] removedNames =
        [
            "DotCraftLocalClientOptions",
            "DotCraftSdkException",
            "InitializationError",
            "AppServerProtocolException",
            "TurnInProgressError",
            "ThreadNotFoundError",
            "ThreadNotActiveError",
            "TurnFailedError",
            "TurnCancelledError",
            "RunDisconnectedError",
            "ApprovalTimeoutError",
            "WireRequestTimeoutException",
            "WireReconnectQueueFullException"
        ];
        Assert.DoesNotContain(publicTypes, type => removedNames.Contains(type.Name, StringComparer.Ordinal));

        Assert.Equal("DotCraft.Sdk", typeof(DotCraftClient).Namespace);
        Assert.Equal("DotCraft.Sdk.DynamicTools", typeof(DynamicToolRegistry).Namespace);
        Assert.NotNull(assembly.GetType("DotCraft.Sdk.DotCraftLocalOptions"));
        Assert.NotNull(assembly.GetType("DotCraft.Sdk.DotCraftRemoteOptions"));
    }
}
