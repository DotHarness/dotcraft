using DotCraft.Agents;
using DotCraft.AppServer;
using DotCraft.Configuration;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class ModelCatalogSpeedTests
{
    [Fact]
    public void ProviderReportedSpeedModesReplaceMergedCatalogFastSelectors()
    {
        var items = ProviderContractMapper.BuildModelCatalogItems(
            new AppConfig(),
            providerId: null,
            ModelProviderProtocols.OpenAIResponses,
            endpoint: null,
            [
                new ModelCatalogEntry { Id = "account-ultrafast", SpeedModes = ["standard", "fast", "ultrafast"] },
                new ModelCatalogEntry { Id = "gpt-5.4", SpeedModes = ["standard"] },
                new ModelCatalogEntry { Id = "gpt-5.5" }
            ],
            includeUltra: false);

        Assert.Equal(["standard", "fast", "ultrafast"], items[0].Speed.Value!.SupportedModes.Value);
        Assert.Null(items[1].Speed.Value);
        Assert.Equal(["standard", "fast"], items[2].Speed.Value!.SupportedModes.Value);
    }
}
