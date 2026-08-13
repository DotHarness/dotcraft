using DotCraft.Configuration;
using DotCraft.OpenSandbox;
using DotCraft.Tools.Sandbox;
using global::OpenSandbox.Config;
using global::OpenSandbox.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DotCraft.Tests.OpenSandbox;

public sealed class OpenSandboxMappingTests
{
    [Fact]
    public void AddOpenSandboxProvider_RegistersFixedProvider()
    {
        var services = new ServiceCollection();

        services.AddOpenSandboxProvider(new AppConfig.SandboxConfig());

        using var provider = services.BuildServiceProvider();
        Assert.IsType<OpenSandboxProvider>(provider.GetRequiredService<ISandboxProvider>());
    }

    [Fact]
    public void CreateOptions_MapsConnectionResourcesAndContainerSettings()
    {
        var config = new AppConfig.SandboxConfig
        {
            Domain = "sandbox.example:443",
            ApiKey = "test-api-key",
            UseHttps = true,
            Image = "example/sandbox:test",
            TimeoutSeconds = 321,
            Cpu = "2",
            Memory = "1Gi"
        };

        var options = OpenSandboxProvider.CreateOptions(config);

        Assert.NotNull(options.ConnectionConfig);
        Assert.Equal("sandbox.example:443", options.ConnectionConfig!.Domain);
        Assert.Equal("test-api-key", options.ConnectionConfig.ApiKey);
        Assert.Equal(ConnectionProtocol.Https, options.ConnectionConfig.Protocol);
        Assert.Equal(30, options.ConnectionConfig.RequestTimeoutSeconds);
        Assert.Equal("example/sandbox:test", options.Image);
        Assert.Equal(321, options.TimeoutSeconds);
        Assert.NotNull(options.Resource);
        Assert.Equal("2", options.Resource!["cpu"]);
        Assert.Equal("1Gi", options.Resource["memory"]);
    }

    [Fact]
    public void CreateNetworkPolicy_MapsDenyAndCustomEgress()
    {
        var deny = OpenSandboxProvider.CreateNetworkPolicy(new AppConfig.SandboxConfig
        {
            NetworkPolicy = "deny"
        });
        var custom = OpenSandboxProvider.CreateNetworkPolicy(new AppConfig.SandboxConfig
        {
            NetworkPolicy = "custom",
            AllowedEgressDomains = ["api.example.com", "packages.example.com"]
        });

        Assert.Equal(NetworkRuleAction.Deny, deny!.DefaultAction);
        Assert.Equal(NetworkRuleAction.Deny, custom!.DefaultAction);
        Assert.NotNull(custom.Egress);
        Assert.Equal(
            ["api.example.com", "packages.example.com"],
            custom.Egress!.Select(rule => rule.Target).ToArray());
    }

    [Fact]
    public void MapExecution_PreservesOutputAndFailureDetails()
    {
        var execution = new Execution
        {
            Error = new ExecutionError
            {
                Name = "exit",
                Value = "7",
                Timestamp = 0,
                Traceback = []
            }
        };
        execution.Logs.Stdout.Add(new OutputMessage { Text = "out", Timestamp = 0 });
        execution.Logs.Stderr.Add(new OutputMessage { Text = "err", Timestamp = 0 });

        var result = OpenSandboxInstance.MapExecution(execution);

        Assert.Equal("out", Assert.Single(result.Stdout).Text);
        Assert.Equal("err", Assert.Single(result.Stderr).Text);
        Assert.Equal(new SandboxCommandError("exit", "7"), result.Error);
    }

    [Fact]
    public void ExceptionMapper_PreservesProviderCodeAndMessage()
    {
        var sdkException = new global::OpenSandbox.Core.SandboxException(
            "request failed",
            new InvalidOperationException("transport"),
            new global::OpenSandbox.Core.SandboxError("sandbox_unavailable", "backend unavailable"));

        var exception = OpenSandboxExceptionMapper.Map(sdkException);

        Assert.Equal("sandbox_unavailable", exception.Code);
        Assert.Equal("backend unavailable", exception.Message);
        Assert.Same(sdkException, exception.InnerException);
    }

    [Fact]
    public void FileEntryMapping_PreservesPathsDataAndModes()
    {
        var directories = OpenSandboxInstance.MapDirectoryEntries(
            [new SandboxDirectoryEntry("/workspace/src", 755)]);
        var writes = OpenSandboxInstance.MapWriteEntries(
            [new SandboxWriteEntry("/workspace/src/app.cs", "content", 644)]);

        var directory = Assert.Single(directories);
        Assert.Equal("/workspace/src", directory.Path);
        Assert.Equal(755, directory.Mode);
        var write = Assert.Single(writes);
        Assert.Equal("/workspace/src/app.cs", write.Path);
        Assert.Equal("content", write.Data);
        Assert.Equal(644, write.Mode);
    }
}
