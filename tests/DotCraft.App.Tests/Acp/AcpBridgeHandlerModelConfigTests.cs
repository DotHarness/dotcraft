using DotCraft.Acp;
using DotCraft.Protocol.AppServer;

using Contract = DotCraft.Protocol.AppServer;
using Xunit;

namespace DotCraft.App.Tests.Acp;

public sealed class AcpBridgeHandlerModelConfigTests
{
    [Fact]
    public void ConfigOptionsUpdate_UsesStableAcpName()
    {
        Assert.Equal("config_option_update", AcpUpdateKind.ConfigOptionsUpdate);
    }

    [Fact]
    public void BuildConfigOptions_AddsModelSelectorFromSuccessfulCatalog()
    {
        var options = AcpBridgeHandler.BuildConfigOptions(
            currentMode: "plan",
            currentModel: "gpt-current",
            modelList: new Contract.ModelListResult
            {
                Success = true,
                Models = new DotCraft.Protocol.Optional<IReadOnlyList<Contract.ModelCatalogItem>>(
                [
                    new Contract.ModelCatalogItem { Id = "gpt-beta" },
                    new Contract.ModelCatalogItem { Id = "gpt-alpha" },
                    new Contract.ModelCatalogItem { Id = "GPT-BETA" }
                ])
            });

        var mode = Assert.Single(options, o => o.Id == "mode");
        Assert.Equal("plan", mode.CurrentValue);

        var model = Assert.Single(options, o => o.Id == "model");
        Assert.Equal("model", model.Category);
        Assert.Equal("gpt-current", model.CurrentValue);
        Assert.Equal(
            [AcpBridgeHandler.DefaultModelValue, "gpt-alpha", "gpt-beta", "gpt-current"],
            model.Options.Select(o => o.Value).ToArray());
        Assert.Equal("Default", model.Options[0].Name);
    }

    [Fact]
    public void BuildConfigOptions_OmitsModelSelectorWhenCatalogFails()
    {
        var options = AcpBridgeHandler.BuildConfigOptions(
            currentMode: "agent",
            currentModel: "gpt-current",
            modelList: new Contract.ModelListResult
            {
                Success = false,
                ErrorCode = "EndpointNotSupported",
                ErrorMessage = "Endpoint does not support model listing."
            });

        Assert.Contains(options, o => o.Id == "mode");
        Assert.DoesNotContain(options, o => o.Id == "model");
    }
}
