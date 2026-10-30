using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using DotCraft.Oratorio.Data;
using DotCraft.Oratorio.Integrations;
using DotCraft.Oratorio.GitLab;
using DotCraft.Oratorio.GitHub;
using DotCraft.Oratorio.Realtime;
using DotCraft.Oratorio.Services;
using DotCraft.Oratorio.Sources;
using ModelContextProtocol.Protocol;

namespace DotCraft.Oratorio.Hosting;

public static partial class OratorioApplication
{
    private static void RegisterServices(WebApplicationBuilder builder, string serverConfigurationOverlayPath)
    {
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        });

        builder.Services.AddCors(options =>
        {
            options.AddPolicy("DesktopRenderer", policy =>
            {
                policy.SetIsOriginAllowed(IsDesktopRendererOrigin)
                    .AllowAnyHeader()
                    .AllowAnyMethod();
            });
        });

        builder.Services.AddSingleton<IClock, SystemClock>();
        builder.Services.Configure<SettingsWriteOptions>(builder.Configuration.GetSection("Oratorio:Settings"));
        builder.Services.Configure<GitHubOptions>(builder.Configuration.GetSection("Oratorio:GitHub"));
        builder.Services.Configure<GitLabOptions>(builder.Configuration.GetSection("Oratorio:GitLab"));
        builder.Services.Configure<DotCraftOptions>(builder.Configuration.GetSection("Oratorio:DotCraft"));
        builder.Services.AddSingleton<IPostConfigureOptions<DotCraftOptions>, DotCraftOptionsPostConfigure>();
        builder.Services.Configure<OratorioAutomationOptions>(builder.Configuration.GetSection("Oratorio:Automation"));
        builder.Services.PostConfigure<OratorioAutomationOptions>(options =>
        {
            if (File.Exists(serverConfigurationOverlayPath))
            {
                return;
            }

            if (options.AutoDispatchAllowLabels.Length == 0)
            {
                options.AutoDispatchAllowLabels = ["oratorio:auto"];
            }

            if (options.AutoDispatchBlockLabels.Length == 0)
            {
                options.AutoDispatchBlockLabels = ["blocked", "on hold", "needs-design", "needs-human"];
            }
        });
        builder.Services.AddHttpClient("GitHub");
        builder.Services.AddHttpClient("GitLab");
        builder.Services.AddHttpClient("DotCraftHub");
        builder.Services.AddSingleton<IConfigurationSecretProtector, ConfigurationSecretProtector>();
        builder.Services.AddSingleton<IGitHubCredentialResolver, GitHubCredentialResolver>();
        builder.Services.AddSingleton<IGitHubInstallationResolver, GitHubInstallationResolver>();
        builder.Services.AddSingleton<IGitHubTokenProvider, GitHubTokenProvider>();
        builder.Services.AddSingleton<IGitHubApiClient, GitHubApiClient>();
        builder.Services.AddSingleton<GitHubAppIdentity>();
        builder.Services.AddSingleton<GitHubSyncCoordinator>();
        builder.Services.AddSingleton<IGitLabCredentialResolver, GitLabCredentialResolver>();
        builder.Services.AddSingleton<IGitLabApiClient, GitLabApiClient>();
        builder.Services.AddSingleton<GitLabTokenIdentity>();
        builder.Services.AddSingleton<GitLabSyncCoordinator>();
        builder.Services.AddScoped<ISourceProvider, GitHubSourceProvider>();
        builder.Services.AddScoped<ISourceProvider, GitLabSourceProvider>();
        builder.Services.AddScoped<SourceProviderRegistry>();
        builder.Services.AddScoped<SourceProviderService>();
        builder.Services.AddScoped<SourceSyncSchedulerService>();
        builder.Services.AddSingleton<IDotCraftWorkspaceResolver, DotCraftWorkspaceResolver>();
        builder.Services.AddSingleton<IDotCraftAppServerEndpointResolver, DotCraftAppServerEndpointResolver>();
        builder.Services.AddSingleton<IDotCraftAppServerProcessManager, DotCraftAppServerProcessManager>();
        builder.Services.AddSingleton<IDotCraftAppServerClientFactory, DotCraftAppServerClientFactory>();
        builder.Services.AddSingleton<IGitTransportCredentialProvider, GitTransportCredentialProvider>();
        builder.Services.AddSingleton<IWorktreeManager, WorktreeManager>();
        builder.Services.AddSingleton<IGitDeliveryClient, GitDeliveryClient>();
        builder.Services.AddSingleton<DotCraftStatusService>();
        builder.Services.AddSingleton<WorkspaceInventoryService>();
        builder.Services.AddScoped<OratorioService>();
        builder.Services.AddScoped<GitHubSourceService>();
        builder.Services.AddSingleton<GitHubCommentCommandParser>();
        builder.Services.AddScoped<GitHubCommentCommandIntakeService>();
        builder.Services.AddScoped<GitHubCommentCommandProcessor>();
        builder.Services.AddScoped<GitHubCommentScanService>();
        builder.Services.AddScoped<GitLabSourceService>();
        builder.Services.AddScoped<GitHubWriteService>();
        builder.Services.AddScoped<GitLabWriteService>();
        builder.Services.AddScoped<IReviewLocalDiffProvider, GitReviewLocalDiffProvider>();
        builder.Services.AddScoped<IReviewDiffProvider, ReviewDiffProvider>();
        builder.Services.AddScoped<ReviewDraftService>();
        builder.Services.AddScoped<ReviewFindingResolutionService>();
        builder.Services.AddScoped<ImplementationDraftService>();
        builder.Services.AddScoped<FollowUpDraftService>();
        builder.Services.AddScoped<DiscussionTurnService>();
        builder.Services.AddScoped<InlineQuestionContext>();
        builder.Services.AddScoped<DiscussionTurnDelivery>();
        builder.Services.AddScoped<DiscussionWorkspacePreparer>();
        builder.Services.AddScoped<IssueDiscussionContext>();
        builder.Services.AddSingleton<DiscussionSessionRunner>();
        builder.Services.AddSingleton<DiscussionThreadSessions>();
        builder.Services.AddScoped<ConversationThreadLocator>();
        builder.Services.AddSingleton<ConversationThreadStarter>();
        builder.Services.AddScoped<SettingsDiagnosticsService>();
        builder.Services.AddScoped<ServerConfigurationService>();
        builder.Services.AddScoped<SourceSetupService>();
        builder.Services.AddScoped<TaskShortIdAllocator>();
        builder.Services.AddScoped<TaskBoardPlacementService>();
        builder.Services.AddScoped<AppServerPromptBuilder>();
        builder.Services.AddScoped<ThreadStateTracker>();
        builder.Services.AddSingleton<OratorioDynamicToolCatalog>();
        builder.Services.AddScoped<AutoReviewDispatchService>();
        builder.Services.AddScoped<ImplementationAutoDispatchService>();
        builder.Services.AddScoped<ImplementationFollowUpDispatchService>();
        builder.Services.AddSingleton<OratorioAppBindingService>();
        builder.Services.AddSingleton<OratorioBindingMcpRuntime>();
        builder.Services.AddSingleton<OratorioBindingMcpHandlers>();
        builder.Services.AddSingleton<OratorioBoardSurfaceRuntime>();
        builder.Services.AddSingleton<BoardEventHub>();
        builder.Services.AddSingleton<DrawerStateService>();
        builder.Services.AddSingleton<IAppServerRunCoordinator, AppServerRunCoordinator>();
        if (builder.Environment.IsEnvironment("Testing"))
        {
            builder.Services.AddHostedService<MockRunWorker>();
        }
        builder.Services.AddHostedService<GitHubSyncWorker>();
        builder.Services.AddHostedService<GitHubCommentCommandWorker>();
        builder.Services.AddHostedService<GitHubCommentScanWorker>();
        builder.Services.AddHostedService<GitLabSyncWorker>();
        builder.Services.AddHostedService<SourceSyncSchedulerWorker>();
        builder.Services.AddHostedService<AppServerRunWorker>();
        builder.Services.AddHostedService<DiscussionTurnWorker>();
        builder.Services.AddHostedService<ImplementationAutoDispatchWorker>();
        builder.Services.AddHostedService<AutoReviewDispatchWorker>();
        builder.Services.AddHostedService<ImplementationFollowUpDispatchWorker>();
        builder.Services.AddHostedService<WorktreeCleanupWorker>();

        var databasePath = ResolveDatabasePath(builder);
        builder.Services.AddDbContext<OratorioDbContext>(options => options.UseSqlite($"Data Source={databasePath}"));
        builder.Services.AddSingleton(new OratorioDotCraftBindingStore(
            Path.Combine(Path.GetDirectoryName(databasePath)!, "dotcraft-binding.json")));
        builder.Services.AddHostedService<OratorioAppBindingReannounceWorker>();
        builder.Services.AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation { Name = "oratorio.board", Version = "1" };
                options.ServerInstructions = OratorioBindingMcpCatalog.ServerInstructions;
            })
            .WithHttpTransport(options => options.Stateless = false)
            .WithListToolsHandler((request, ct) =>
                request.Services!.GetRequiredService<OratorioBindingMcpHandlers>().ListToolsAsync(request, ct))
            .WithCallToolHandler((request, ct) =>
                request.Services!.GetRequiredService<OratorioBindingMcpHandlers>().CallToolAsync(request, ct))
            .WithListResourcesHandler((request, ct) =>
                request.Services!.GetRequiredService<OratorioBindingMcpHandlers>().ListResourcesAsync(request, ct))
            .WithListResourceTemplatesHandler((request, ct) =>
                request.Services!.GetRequiredService<OratorioBindingMcpHandlers>().ListResourceTemplatesAsync(request, ct))
            .WithReadResourceHandler((request, ct) =>
                request.Services!.GetRequiredService<OratorioBindingMcpHandlers>().ReadResourceAsync(request, ct));
    }
}