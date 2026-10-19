using System.Text.Json;
using DotCraft.AppBinding;
using DotCraft.Configuration;
using DotCraft.Mcp;
using DotCraft.Security;
using DotCraft.Tools;
using Xunit;

namespace DotCraft.Tests.Mcp;

public sealed partial class McpStreamableHttpSessionRecoveryIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnifiedCatalog_RoutesOriginalToolNameToExactServer(bool bindingOwned)
    {
        const string rawName = "read-card.status";
        var root = Path.Combine(Path.GetTempPath(), $"mcp-binding-raw-{Guid.NewGuid():N}");
        var craft = Path.Combine(root, ".craft");
        await using var manager = new McpClientManager();
        await using var server = MockStreamableHttpMcpServer.Start(new MockStreamableHttpMcpServer.Options { ToolName = rawName });
        try
        {
            var control = new AppBindingService();
            var contributor = new AppBindingToolCatalogContributor(control);
            string? bindingId = null;
            if (bindingOwned)
            {
                var request = control.StartConnection(craft, "com.example.board", "user");
                var principal = control.Connect(craft, new AppConnectionConnectCommand
                {
                    ConnectionRequestId = request.ConnectionRequestId, RequestToken = request.RequestToken
                });
                var enabled = control.Enable(craft, "thread-1", "com.example.board", "user");
                bindingId = enabled.BindingId;
                control.BeginActivation(craft, principal.Principal.PrincipalId, bindingId, null, server.Endpoint, enabled.BindingRequestId);
                control.CompleteSync(craft, bindingId, [new AppBindingToolCapability
                {
                    SourceToolName = rawName, Description = "Raw tool", Visibility = ["model"],
                    InputSchema = new System.Text.Json.Nodes.JsonObject { ["type"] = "object" }
                }]);
            }
            await manager.ConnectAsync([new McpServerConfig
            {
                Name = "route-with-suffix~2", Transport = "streamableHttp", Url = server.Endpoint,
                Origin = bindingId is null ? McpServerOrigin.Workspace() : McpServerOrigin.Binding(bindingId, "com.example.board")
            }]);
            await manager.WaitForStartupCompletionAsync();
            Assert.Equal("ready", Assert.Single(await manager.ListStatusesAsync()).StartupState);
            var config = new AppConfig();
            config.Tools.DeferredLoading.Strategy = AppConfig.DeferredLoadingStrategy.Off;
            var context = new ToolPlanningContext("thread-1", null, root, craft, "agent", null, null, 1);
            var registrations = await new McpToolSource(manager, config, [contributor]).GetRegistrationsAsync(context);
            var registration = Assert.Single(registrations);
            Assert.Equal(rawName, registration.Definition.Id.SourceToolId.Value);
            Assert.NotEqual(rawName, registration.Definition.Name.Name);
            if (!bindingOwned)
                Assert.Equal(McpToolNaming.CanonicalToolName("route-with-suffix~2", rawName), registration.Definition.Name);
            Assert.Equal("route-with-suffix~2", McpToolRouting.ServerName(registration));
            var approval = new CommonToolApprovalEvaluator();
            approval.Bind(new AutoApproveApprovalService());
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var result = await new ToolDispatcher(approvalEvaluator: approval).DispatchAsync(new EffectiveToolSnapshotBuilder().Build(registrations, 1),
                registration.Definition.Name, [], new ToolInvocationRequest("thread-1", "turn-1", "call-1", ToolInvocationAudience.Model), timeout.Token);
            Assert.True(result.Success, $"{result.Error?.Message}; requests={server.Requests.Count}; calls={server.ToolCallRequests.Count}; server={server.ServerException}");
            using var remoteCall = JsonDocument.Parse(Assert.Single(server.ToolCallRequests).Body);
            Assert.Equal(rawName, remoteCall.RootElement.GetProperty("params").GetProperty("name").GetString());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
