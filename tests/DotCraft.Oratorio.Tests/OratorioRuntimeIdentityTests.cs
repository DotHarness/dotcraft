using DotCraft.Oratorio.Integrations;
using DotCraft.Oratorio.Api;

namespace DotCraft.Oratorio.Tests;

public sealed class OratorioRuntimeIdentityTests
{
    [Theory]
    [InlineData("/workspace", "remote:cloud:prod:/workspace")]
    [InlineData("/workspace/project", "remote:host-1:stack-1:/workspace/project")]
    public void ValidateRuntimeIdentityAcceptsBoundRemoteWorkspace(string workspace, string identity)
    {
        OratorioAppBindingService.ValidateRuntimeIdentity(workspace, identity);
    }

    [Theory]
    [InlineData("/workspace", "remote:cloud:prod:/workspace/other")]
    [InlineData("/workspace", "remote::prod:/workspace")]
    [InlineData("/workspace", "remote:cloud::/workspace")]
    [InlineData("/workspace", "remote:cloud:prod")]
    public void ValidateRuntimeIdentityRejectsMalformedOrMismatchedRemoteWorkspace(string workspace, string identity)
    {
        Assert.Throws<OratorioApiException>(() =>
            OratorioAppBindingService.ValidateRuntimeIdentity(workspace, identity));
    }
}
