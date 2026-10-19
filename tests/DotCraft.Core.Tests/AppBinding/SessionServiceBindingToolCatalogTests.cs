using DotCraft.Agents;
using DotCraft.AppBinding;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Persistence;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using DotCraft.Tests;
using Xunit;

namespace DotCraft.Core.Tests.AppBinding;

public sealed class SessionServiceBindingToolCatalogTests
{
    [Fact]
    public async Task ThreadSnapshot_PublishesApprovedBindingWithoutAnyMcpConnection()
    {
        var root = Path.Combine(Path.GetTempPath(), $"binding-session-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var config = AppConfigTestFactory.CreateOpenAI();
            using var database = new WorkspaceStateDatabase(root);
            await using var factory = new AgentFactory(root, root, config, new MemoryStore(root), new SkillsLoader(root),
                new AutoApproveApprovalService(), null, chatClientRegistry: TestModelProviderRegistry.Create());
            await using var store = new ThreadStore(root, database);
            var control = new AppBindingService();
            var service = new SessionService(factory, null, new SessionPersistenceService(store), new SessionGate(),
                mcpToolCatalogContributors: [new AppBindingToolCatalogContributor(control)]);
            var thread = await service.CreateThreadAsync(new SessionIdentity { ChannelName = "test", UserId = "user", WorkspacePath = root });
            var request = control.StartConnection(root, "com.dotharness.oratorio", "user");
            var principal = control.Connect(root, new AppConnectionConnectCommand
            {
                ConnectionRequestId = request.ConnectionRequestId, RequestToken = request.RequestToken
            });
            var enabled = control.Enable(root, thread.Id, "com.dotharness.oratorio", "user");
            control.BeginActivation(root, principal.Principal.PrincipalId, enabled.BindingId, null,
                "http://127.0.0.1:9000/mcp", enabled.BindingRequestId);
            control.CompleteSync(root, enabled.BindingId, [new AppBindingToolCapability
            {
                SourceToolName = "ListBoardItems", Description = "List board items", Visibility = ["model"],
                InputSchema = new System.Text.Json.Nodes.JsonObject { ["type"] = "object" }
            }]);
            control.MarkUnavailable(root, enabled.BindingId, "runtimeRestarted");
            service = new SessionService(factory, null, new SessionPersistenceService(store), new SessionGate(),
                mcpToolCatalogContributors: [new AppBindingToolCatalogContributor(new AppBindingService())]);
            var snapshot = await service.GetEffectiveToolSnapshotAsync(thread.Id);
            var tool = Assert.Single(snapshot.Registrations.Values, registration => registration.Definition.Id.SourceToolId.Value == "ListBoardItems");
            Assert.Equal("mcp__com_dotharness_oratorio", tool.Definition.Name.Namespace);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
