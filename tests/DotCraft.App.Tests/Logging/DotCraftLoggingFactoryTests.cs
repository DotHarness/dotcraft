using DotCraft.Configuration;
using DotCraft.Logging;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DotCraft.Tests.Logging;

public sealed class DotCraftLoggingFactoryTests
{
    [Fact]
    public void WorkspaceFactory_WritesAndFlushesOperationalLog()
    {
        var root = CreateTempRoot();
        try
        {
            var factory = DotCraftLoggingFactory.CreateWorkspace(NewConfig(), root, reservesStdout: false);
            var logger = factory.CreateLogger("DotCraft.Tests.Component");

            try
            {
                ThrowTestFailure();
            }
            catch (InvalidOperationException ex)
            {
                logger.LogCritical(ex, "Host failed for {WorkspaceId}", "workspace-1");
            }

            factory.Dispose();

            var file = Assert.Single(Directory.GetFiles(Path.Combine(root, "logs"), "dotcraft-*.log"));
            Assert.DoesNotContain("-pid", Path.GetFileName(file), StringComparison.OrdinalIgnoreCase);
            Assert.Matches(@"^dotcraft-\d{4}-\d{2}-\d{2}_000\.log$", Path.GetFileName(file));
            var content = ReadOnlyLog(root, "dotcraft-*.log");
            Assert.Contains("[CRI]", content);
            Assert.Contains("[pid:", content);
            Assert.Contains("Tests.Component", content);
            Assert.Contains("Host failed for workspace-1", content);
            Assert.Contains(nameof(InvalidOperationException), content);
            Assert.Contains(nameof(ThrowTestFailure), content);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void WorkspaceFactory_RespectsMinimumLevel()
    {
        var root = CreateTempRoot();
        try
        {
            var config = NewConfig();
            config.MinLevel = "Warning";
            var factory = DotCraftLoggingFactory.CreateWorkspace(config, root, reservesStdout: false);
            var logger = factory.CreateLogger("DotCraft.Tests.Filter");

            logger.LogInformation("suppressed-message");
            logger.LogError("recorded-message");
            factory.Dispose();

            var content = ReadOnlyLog(root, "dotcraft-*.log");
            Assert.DoesNotContain("suppressed-message", content);
            Assert.Contains("recorded-message", content);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void WorkspaceFactory_SuppressesFrameworkInformation_ButKeepsWarningsAndApplicationInformation()
    {
        var root = CreateTempRoot();
        try
        {
            var factory = DotCraftLoggingFactory.CreateWorkspace(NewConfig(), root, reservesStdout: false);
            var frameworkCategories = new[]
            {
                "Microsoft.AspNetCore.Hosting.Diagnostics",
                "Microsoft.AspNetCore.Routing.EndpointMiddleware",
                "Microsoft.AspNetCore.Server.Kestrel.Connections",
                "Microsoft.Hosting.Lifetime"
            };
            foreach (var category in frameworkCategories)
                factory.CreateLogger(category).LogInformation("suppressed-framework-information");

            factory.CreateLogger("Microsoft.AspNetCore.Server.Kestrel.Connections")
                .LogWarning("retained-framework-warning");
            factory.CreateLogger("DotCraft.Tests.Application").LogInformation("safe-message");
            factory.Dispose();

            var content = ReadOnlyLog(root, "dotcraft-*.log");
            Assert.DoesNotContain("suppressed-framework-information", content);
            Assert.Contains("retained-framework-warning", content);
            Assert.Contains("safe-message", content);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void WorkspaceFactory_IncludesActiveScopes()
    {
        var root = CreateTempRoot();
        try
        {
            var factory = DotCraftLoggingFactory.CreateWorkspace(NewConfig(), root, reservesStdout: false);
            var logger = factory.CreateLogger("DotCraft.Tests.Scopes");

            using (logger.BeginScope(new Dictionary<string, object?> { ["WorkspaceId"] = "workspace-1" }))
                logger.LogWarning("scoped-message");
            factory.Dispose();

            var content = ReadOnlyLog(root, "dotcraft-*.log");
            Assert.Contains("WorkspaceId", content);
            Assert.Contains("workspace-1", content);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void WorkspaceFactory_SetupFailure_FallsBackWithoutThrowing()
    {
        var root = CreateTempRoot();
        try
        {
            var config = NewConfig();
            config.Directory = "invalid\0directory";

            using var factory = DotCraftLoggingFactory.CreateWorkspace(config, root, reservesStdout: false);
            factory.CreateLogger("DotCraft.Tests.Fallback").LogCritical("logging-remains-non-fatal");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void WorkspaceFactory_Disabled_DoesNotCreateLogDirectory()
    {
        var root = CreateTempRoot();
        try
        {
            var config = NewConfig();
            config.Enabled = false;

            using var factory = DotCraftLoggingFactory.CreateWorkspace(config, root, reservesStdout: false);
            factory.CreateLogger("DotCraft.Tests.Disabled").LogError("not-persisted");

            Assert.False(Directory.Exists(Path.Combine(root, "logs")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void WorkspaceFactory_PurgesExpiredRolledFiles()
    {
        var root = CreateTempRoot();
        try
        {
            var logs = Directory.CreateDirectory(Path.Combine(root, "logs")).FullName;
            var expired = Path.Combine(logs, $"dotcraft-{DateTime.Today.AddDays(-8):yyyy-MM-dd}_001.log");
            var retained = Path.Combine(logs, $"dotcraft-{DateTime.Today.AddDays(-7):yyyy-MM-dd}.log");
            File.WriteAllText(expired, "expired");
            File.WriteAllText(retained, "retained");

            using var factory = DotCraftLoggingFactory.CreateWorkspace(NewConfig(), root, reservesStdout: false);

            Assert.False(File.Exists(expired));
            Assert.True(File.Exists(retained));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void HubFactory_UsesGlobalHubFilePrefix()
    {
        var root = CreateTempRoot();
        try
        {
            var factory = DotCraftLoggingFactory.CreateHub(NewConfig(), root);
            factory.CreateLogger("DotCraft.Hub.Tests").LogInformation("hub-started");
            factory.Dispose();

            var content = ReadOnlyLog(root, "dotcraft-hub-*.log");
            Assert.Contains("hub-started", content);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("url=https://localhost/ws?token=secret-value&x=1", "secret-value")]
    [InlineData("Authorization: Bearer abc.def.ghi", "abc.def.ghi")]
    [InlineData("{\"apiKey\":\"top-secret\"}", "top-secret")]
    public void LogValueRedactor_RemovesCommonSecrets(string input, string secret)
    {
        var redacted = LogValueRedactor.Redact(input);

        Assert.DoesNotContain(secret, redacted);
        Assert.Contains("[REDACTED]", redacted);
    }

    private static AppConfig.LoggingConfig NewConfig() => new()
    {
        Enabled = true,
        Console = false,
        Directory = "logs",
        MinLevel = "Information",
        RetentionDays = 7
    };

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "DotCraftLoggingTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string ReadOnlyLog(string root, string pattern)
    {
        var files = Directory.GetFiles(Path.Combine(root, "logs"), pattern);
        var file = Assert.Single(files);
        return File.ReadAllText(file);
    }

    private static void ThrowTestFailure() => throw new InvalidOperationException("test failure");
}
