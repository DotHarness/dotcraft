using System.Text.Json.Nodes;
using DotCraft.AppBinding;
using Xunit;

namespace DotCraft.Core.Tests.AppBinding;

public sealed class AppBindingServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dotcraft-appbinding-{Guid.NewGuid():N}");
    private string CraftPath => Path.Combine(_root, ".craft");

    [Fact]
    public void PrincipalCredential_IsReturnedOnce_AndNeverPersistedRaw()
    {
        var control = new AppBindingService();
        var start = control.StartConnection(CraftPath, "com.example.test", "user");
        var connected = control.Connect(CraftPath, new AppConnectionConnectCommand
        {
            ConnectionRequestId = start.ConnectionRequestId, RequestToken = start.RequestToken
        });

        var principal = control.Authenticate(CraftPath, "com.example.test", connected.Credential);
        Assert.Equal(connected.Principal.PrincipalId, principal.PrincipalId);
        var persisted = File.ReadAllText(Path.Combine(CraftPath, "app-bindings", "state.json"));
        Assert.DoesNotContain(connected.Credential, persisted, StringComparison.Ordinal);
        Assert.DoesNotContain(start.RequestToken, persisted, StringComparison.Ordinal);
    }

    [Fact]
    public void InitialSnapshot_IsApproved_ExpansionNeedsConfirmation_AndRestartIsOffline()
    {
        var control = new AppBindingService();
        var connection = control.StartConnection(CraftPath, "com.example.test", "user");
        var connected = control.Connect(CraftPath, new AppConnectionConnectCommand
        {
            ConnectionRequestId = connection.ConnectionRequestId, RequestToken = connection.RequestToken
        });
        var enable = control.Enable(CraftPath, "thread-1", "com.example.test", "user");
        var request = control.GetBindingRequest(CraftPath,
            new AppBindingRequestQuery { BindingRequestId = enable.BindingRequestId }, connected.Principal.PrincipalId);
        control.BeginActivation(CraftPath, connected.Principal.PrincipalId, request.BindingId, null,
            "https://example.test/mcp", enable.BindingRequestId);
        var first = control.CompleteSync(CraftPath, request.BindingId, [Tool("read", required: true)]);
        Assert.Equal(AppBindingStates.Active, first.State);

        var expanded = control.CompleteSync(CraftPath, request.BindingId,
            [Tool("read", required: true), Tool("write", required: false)]);
        Assert.Equal(AppBindingStates.NeedsConfirmation, expanded.State);
        Assert.NotEmpty(expanded.PendingChanges);

        var restarted = new AppBindingService().ListThreadBindings(CraftPath, "thread-1").Single();
        Assert.Equal(AppBindingStates.Offline, restarted.State);
        Assert.Equal(first.ApprovedCapabilityRevision, restarted.ApprovedCapabilityRevision);
    }

    [Theory]
    [InlineData("http://example.test/mcp")]
    [InlineData("file:///tmp/server")]
    public void RemoteInsecureOrNonHttpEndpoint_IsRejected(string endpoint) =>
        Assert.ThrowsAny<Exception>(() => AppBindingService.ValidateBindingEndpoint(endpoint));

    [Fact]
    public void CredentialRefresh_ImmediatelyInvalidatesPreviousCredential()
    {
        var control = new AppBindingService();
        var start = control.StartConnection(CraftPath, "com.example.test", "user");
        var connected = control.Connect(CraftPath, new AppConnectionConnectCommand
        {
            ConnectionRequestId = start.ConnectionRequestId, RequestToken = start.RequestToken
        });
        var refreshed = control.Refresh(CraftPath, connected.Principal.PrincipalId);

        Assert.ThrowsAny<Exception>(() =>
            control.Authenticate(CraftPath, "com.example.test", connected.Credential));
        Assert.Equal(connected.Principal.PrincipalId,
            control.Authenticate(CraftPath, "com.example.test", refreshed.Credential).PrincipalId);
    }

    [Fact]
    public void SurfacePublish_ReplacesLease_AndRevokeMakesItUnavailable()
    {
        var control = new AppBindingService();
        var start = control.StartConnection(CraftPath, "com.example.test", "user");
        var connected = control.Connect(CraftPath, new AppConnectionConnectCommand
        {
            ConnectionRequestId = start.ConnectionRequestId, RequestToken = start.RequestToken
        });

        var first = control.PublishSurface(CraftPath, connected.Principal.PrincipalId, new AppSurfacePublishCommand
        {
            SurfaceId = "board", Endpoint = "http://127.0.0.1:5100/dotcraft/board/api/v1", Bearer = "first"
        });
        var second = control.PublishSurface(CraftPath, connected.Principal.PrincipalId, new AppSurfacePublishCommand
        {
            SurfaceId = "board", Endpoint = "http://127.0.0.1:5200/dotcraft/board/api/v1", Bearer = "second"
        });

        var resolved = control.ResolveSurface(CraftPath, "com.example.test", "board");
        Assert.Equal(second.Endpoint, resolved.Endpoint);
        Assert.Equal("second", resolved.Bearer);
        Assert.True(second.ExpiresAt >= first.ExpiresAt);
        Assert.DoesNotContain("second", File.ReadAllText(Path.Combine(CraftPath, "app-bindings", "state.json")));

        control.RevokePrincipal(CraftPath, connected.Principal.PrincipalId, "user");
        Assert.ThrowsAny<Exception>(() => control.ResolveSurface(CraftPath, "com.example.test", "board"));
    }

    [Theory]
    [InlineData("https://example.test/api")]
    [InlineData("file:///tmp/app")]
    [InlineData("http://127.0.0.1:5000/api#fragment")]
    public void InvalidSurfaceEndpoint_IsRejected(string endpoint) =>
        Assert.ThrowsAny<Exception>(() => AppBindingService.ValidateSurfaceEndpoint(endpoint));

    [Fact]
    public void RemovingAnUnmodeledSchemaConstraint_IsTreatedAsExpansion()
    {
        var control = new AppBindingService();
        var connection = control.StartConnection(CraftPath, "com.example.test", "user");
        var principal = control.Connect(CraftPath, new AppConnectionConnectCommand
        {
            ConnectionRequestId = connection.ConnectionRequestId, RequestToken = connection.RequestToken
        }).Principal;
        var enable = control.Enable(CraftPath, "thread-1", "com.example.test", "user");
        control.BeginActivation(CraftPath, principal.PrincipalId, enable.BindingId, null,
            "https://example.test/mcp", enable.BindingRequestId);
        var approved = Tool("read", required: true);
        ((JsonObject)approved.InputSchema["properties"]!["value"]!)["maxLength"] = 10;
        control.CompleteSync(CraftPath, enable.BindingId, [approved]);

        var candidate = Tool("read", required: true);
        var result = control.CompleteSync(CraftPath, enable.BindingId, [candidate]);

        Assert.Equal(AppBindingStates.NeedsConfirmation, result.State);
        Assert.Contains(result.PendingChanges, change => change.Kind == "inputExpanded");
    }

    private static AppBindingToolCapability Tool(string name, bool required) => new()
    {
        Namespace = "example", Name = name, Visibility = ["model"],
        InputSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject { ["value"] = new JsonObject { ["type"] = "string" } },
            ["required"] = required ? new JsonArray("value") : new JsonArray(),
            ["additionalProperties"] = false
        },
        Annotations = new JsonObject { ["requiresApproval"] = true }
    };

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
