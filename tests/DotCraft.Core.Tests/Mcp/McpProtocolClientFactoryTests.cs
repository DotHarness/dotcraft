using DotCraft.Mcp;
using Xunit;

namespace DotCraft.Core.Tests.Mcp;

public sealed class McpProtocolClientFactoryTests
{
    [Fact]
    public void ResolveDotNetHost_ReusesAbsoluteDotNetProcessPathAndFallsBackOtherwise()
    {
        var dotnet = OperatingSystem.IsWindows()
            ? @"C:\Program Files\dotnet\dotnet.exe"
            : "/opt/dotnet/dotnet";
        var dotcraft = OperatingSystem.IsWindows()
            ? @"C:\Program Files\dotcraft\dotcraft.exe"
            : "/opt/dotcraft/dotcraft";

        Assert.Equal(dotnet, McpProtocolClientFactory.ResolveDotNetHost(dotnet));
        Assert.Equal("dotnet", McpProtocolClientFactory.ResolveDotNetHost(dotcraft));
        Assert.Equal("dotnet", McpProtocolClientFactory.ResolveDotNetHost(string.Empty));
    }
}
