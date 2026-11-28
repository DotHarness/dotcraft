using DotCraft.Acp;
using DotHarness.Acp.V1;
using Xunit;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.App.Tests.Acp;

public sealed class AcpBridgeHandlerModelConfigTests
{
    [Fact]
    public void BuildConfigOptions_AddsModelSelectorFromSuccessfulCatalog()
    {
        var options = AcpConfigOptions.Build(
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

        var selectOptions = options.OfType<SessionConfigOption.Select>().Select(c => c.Value).ToList();

        var mode = Assert.Single(selectOptions, o => o.Id.Value == "mode");
        Assert.Equal("plan", mode.CurrentValue.Value);

        var model = Assert.Single(selectOptions, o => o.Id.Value == "model");
        Assert.True(model.Category.HasValue);
        Assert.Equal("model", model.Category.Value?.Value);
        Assert.Equal("gpt-current", model.CurrentValue.Value);

        var flatOptions = Assert.IsType<SessionConfigSelectOptions.Ungrouped>(model.Options).Value;
        Assert.Equal(
            [AcpConfigOptions.DefaultModelValue, "gpt-alpha", "gpt-beta", "gpt-current"],
            flatOptions.Select(o => o.Value.Value).ToArray());
        Assert.Equal("Default", flatOptions[0].Name);
    }

    [Fact]
    public void BuildConfigOptions_OmitsModelSelectorWhenCatalogFails()
    {
        var options = AcpConfigOptions.Build(
            currentMode: "agent",
            currentModel: "gpt-current",
            modelList: new Contract.ModelListResult
            {
                Success = false,
                ErrorCode = "EndpointNotSupported",
                ErrorMessage = "Endpoint does not support model listing."
            });

        var selectOptions = options.OfType<SessionConfigOption.Select>().Select(c => c.Value).ToList();
        Assert.Contains(selectOptions, o => o.Id.Value == "mode");
        Assert.DoesNotContain(selectOptions, o => o.Id.Value == "model");
    }
}
