using DotCraft.Mcp;
using Xunit;

namespace DotCraft.Core.Tests.Mcp;

public sealed class McpProtocolClientFactoryTests
{
    [Fact]
    public void ResolveDotNetHost_ReusesAbsoluteDotNetProcessPathAndFallsBackOtherwise()
    {
        Assert.Equal("/opt/dotnet/dotnet", McpProtocolClientFactory.ResolveDotNetHost("/opt/dotnet/dotnet"));
        Assert.Equal(@"C:\Program Files\dotnet\dotnet.exe", McpProtocolClientFactory.ResolveDotNetHost(@"C:\Program Files\dotnet\dotnet.exe"));
        Assert.Equal("dotnet", McpProtocolClientFactory.ResolveDotNetHost("/opt/dotcraft/dotcraft"));
        Assert.Equal("dotnet", McpProtocolClientFactory.ResolveDotNetHost(string.Empty));
    }
}
