using Oratorio.Server.Domain;
using Oratorio.Server.Sources;

namespace Oratorio.Server.Services;

public sealed class OratorioAutomationOptions
{
    public bool AutoDispatchEnabled { get; set; }
    public string[] AutoDispatchAllowLabels { get; set; } = [];
    public string[] AutoDispatchBlockLabels { get; set; } = [];
    public DeliveryPolicy DeliveryPolicy { get; set; } = DeliveryPolicy.ManualDelivery;
    public int MaxImplementationTurns { get; set; } = 3;
    public string[] AutoReviewRepositories { get; set; } = [];
    public bool AutoReviewPublishEnabled { get; set; }
    public string[] AutoReviewPublishRepositories { get; set; } = [];
    public bool AutoFollowUpEnabled { get; set; }
    public string[] AutoFollowUpRepositories { get; set; } = [];
    public int MaxFollowUpRounds { get; set; } = 5;

    public int EffectiveMaxImplementationTurns => Math.Clamp(MaxImplementationTurns, 1, 10);

    public int EffectiveMaxFollowUpRounds => Math.Clamp(MaxFollowUpRounds, 1, 20);

    public bool CanAutoReviewRepository(string? repository) =>
        !string.IsNullOrWhiteSpace(repository) &&
        AutoReviewRepositories.Any(x => MatchesRepository(x, repository));

    public bool CanAutoPublishReviewForRepository(string? repository) =>
        AutoReviewPublishEnabled &&
        !string.IsNullOrWhiteSpace(repository) &&
        AutoReviewPublishRepositories.Any(x => MatchesRepository(x, repository));

    public bool CanAutoFollowUpRepository(string? repository) =>
        AutoFollowUpEnabled &&
        !string.IsNullOrWhiteSpace(repository) &&
        AutoFollowUpRepositories.Any(x => MatchesRepository(x, repository));

    private static bool MatchesRepository(string configured, string repository)
    {
        var left = configured.Trim();
        var right = repository.Trim();
        if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (SourceProjectKey.TryParse(left, out var leftKey) &&
            SourceProjectKey.TryParse(right, out var rightKey))
        {
            return string.Equals(leftKey.Key, rightKey.Key, StringComparison.OrdinalIgnoreCase);
        }

        if (SourceProjectKey.TryParse(left, out var configuredKey))
        {
            return configuredKey.Provider == "github" &&
                string.Equals(configuredKey.ProjectPath, SourceProjectKey.NormalizeGitHubRepository(right), StringComparison.OrdinalIgnoreCase);
        }

        if (SourceProjectKey.TryParse(right, out var repositoryKey))
        {
            return repositoryKey.Provider == "github" &&
                string.Equals(SourceProjectKey.NormalizeGitHubRepository(left), repositoryKey.ProjectPath, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
