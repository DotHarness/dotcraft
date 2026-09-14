using System.Text.Json;
using DotCraft.CLI;
using DotCraft.Hub;
using DotCraft.Auth.OpenAI;
using Xunit;

namespace DotCraft.Tests.CLI;

public sealed class SetupOAuthCatalogTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ModelCatalog_UsesHostCredentials(bool authenticated, bool draft)
    {
        var root = Path.Combine(Path.GetTempPath(), "dotcraft-catalog", Guid.NewGuid().ToString("N"));
        var paths = HubPaths.Resolve(root);
        Directory.CreateDirectory(paths.CraftHomePath);
        try
        {
            await File.WriteAllTextAsync(paths.GlobalConfigPath,
                """{"Providers":{"subscription":{"Protocol":"openai-responses","AuthMethod":"chatgptOAuth"}}}""");
            if (authenticated)
            {
                new OpenAITokenStore(paths.CraftHomePath).Save(new AuthDotJson
                {
                    Tokens = new OpenAITokenSet { AccessToken = "test-token", IdToken = "header.e30.signature", AccountId = "test-account" }
                });
            }
            JsonElement result = default;
            // An expired token without a refresh token exercises the offline catalog without network access.
            var reader = new StringReader("""{"id":"subscription","protocol":"openai-responses","authMethod":"chatgptOAuth"}""");
            var exitCode = await ModelCatalogCliRunner.RunAsync(
                new CommandLineArgs { Mode = CommandLineArgs.RunMode.ModelCatalog, SetupProviderId = "subscription", ModelCatalogReadStdin = draft },
                CancellationToken.None, paths, reader, value =>
                {
                    result = JsonSerializer.SerializeToElement(value);
                    return Task.CompletedTask;
                });
            Assert.Equal(0, exitCode);
            Assert.True(result.GetProperty("kind").GetString() == (authenticated ? "success" : "auth-required"), result.ToString());
            if (authenticated) Assert.NotEmpty(result.GetProperty("models").EnumerateArray());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Setup_PersistsAuthenticatedAccountOnFinalSubmission()
    {
        var root = Path.Combine(Path.GetTempPath(), "dotcraft-setup-oauth", Guid.NewGuid().ToString("N"));
        var paths = HubPaths.Resolve(root);
        Directory.CreateDirectory(paths.CraftHomePath);
        try
        {
            var payload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(
                """{"https://api.openai.com/auth":{"chatgpt_account_id":"account-test","chatgpt_plan_type":"plus"}}"""));
            new OpenAITokenStore(paths.CraftHomePath).Save(new AuthDotJson
            {
                Tokens = new OpenAITokenSet { AccessToken = "test-token", IdToken = $"header.{payload}.signature" }
            });
            Assert.False(File.Exists(paths.GlobalConfigPath));
            var exitCode = InitHelper.RunSetup(Path.Combine(root, "workspace", ".craft"), new WorkspaceSetupRequest
            {
                ApiKey = "",
                EndPoint = "",
                Model = "account-model",
                ProviderMode = WorkspaceSetupProviderMode.Create,
                Provider = new WorkspaceSetupProviderDraft
                {
                    Id = "subscription", Protocol = "openai-responses", AuthMethod = "chatgptOAuth"
                }
            }, paths.GlobalConfigPath);
            Assert.Equal(0, exitCode);
            using var config = JsonDocument.Parse(File.ReadAllText(paths.GlobalConfigPath));
            var provider = config.RootElement.GetProperty("Providers").GetProperty("subscription");
            Assert.Equal("account-test", provider.GetProperty("ChatGptAccountId").GetString());
            Assert.Equal("plus", provider.GetProperty("ChatGptPlanType").GetString());
            Assert.DoesNotContain("test-token", File.ReadAllText(paths.GlobalConfigPath));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

}
