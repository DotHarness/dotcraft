using DotHarness.Acp.V1;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.Acp;

internal static class AcpConfigOptions
{
    internal const string DefaultModelValue = "__dotcraft_default__";

    internal static SessionModeState BuildModeState(string? currentMode = "agent")
    {
        var normalized = string.IsNullOrWhiteSpace(currentMode) ? "agent" : currentMode.Trim().ToLowerInvariant();
        return new SessionModeState
        {
            CurrentModeId = new(normalized),
            AvailableModes =
            [
                new SessionMode
                {
                    Id = new("agent"),
                    Name = "Agent",
                    Description = "Full agent mode with all tools"
                },
                new SessionMode
                {
                    Id = new("plan"),
                    Name = "Plan",
                    Description = "Read-only planning mode"
                }
            ]
        };
    }

    internal static SessionConfigOption[] Build(
        string? currentMode = "agent",
        string? currentModel = null,
        Contract.ModelListResult? modelList = null)
    {
        var options = new List<SessionConfigOption>
        {
            new SessionConfigOption.Case1(new SessionConfigOptionCase1Payload
            {
                Id = new("mode"),
                Name = "Mode",
                Category = SessionConfigOptionCategory.Mode,
                CurrentValue = new(string.IsNullOrWhiteSpace(currentMode) ? "agent" : currentMode.Trim()),
                Options = new SessionConfigSelectOptions.Case1(
                [
                    new SessionConfigSelectOption
                    {
                        Value = new("agent"),
                        Name = "Agent",
                        Description = "Full agent mode with all tools"
                    },
                    new SessionConfigSelectOption
                    {
                        Value = new("plan"),
                        Name = "Plan",
                        Description = "Read-only planning mode"
                    }
                ])
            })
        };

        if (modelList?.Success is { IsSet: true, Value: true })
        {
            var models = modelList.Models.IsSet
                ? modelList.Models.Value ?? Array.Empty<Contract.ModelCatalogItem>()
                : Array.Empty<Contract.ModelCatalogItem>();
            var modelIds = models
                .Select(m => m.Id.IsSet ? m.Id.Value?.Trim() : null)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!)
                .ToList();

            var normalizedCurrentModel = NormalizeOptionalModel(currentModel);
            if (!string.IsNullOrWhiteSpace(normalizedCurrentModel)
                && modelIds.All(id => !string.Equals(id, normalizedCurrentModel, StringComparison.OrdinalIgnoreCase)))
            {
                modelIds.Add(normalizedCurrentModel);
            }

            modelIds = [.. modelIds
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)];

            var modelOptions = new List<SessionConfigSelectOption>
            {
                new() { Value = new(DefaultModelValue), Name = "Default" }
            };
            modelOptions.AddRange(modelIds.Select(id => new SessionConfigSelectOption
            {
                Value = new(id),
                Name = id
            }));

            options.Add(new SessionConfigOption.Case1(new SessionConfigOptionCase1Payload
            {
                Id = new("model"),
                Name = "Model",
                Category = SessionConfigOptionCategory.Model,
                CurrentValue = new(string.IsNullOrWhiteSpace(normalizedCurrentModel)
                    ? DefaultModelValue
                    : normalizedCurrentModel),
                Options = new SessionConfigSelectOptions.Case1([.. modelOptions])
            }));
        }

        return [.. options];
    }

    internal static bool IsModelOption(SessionConfigOption option) =>
        option is SessionConfigOption.Case1 select
        && (string.Equals(select.Value.Id.Value, "model", StringComparison.OrdinalIgnoreCase)
            || (select.Value.Category is { HasValue: true, Value: { } category }
                && string.Equals(category.Value, "model", StringComparison.OrdinalIgnoreCase)));

    internal static bool ContainsSelectValue(SessionConfigOption option, string value)
    {
        if (option is not SessionConfigOption.Case1 select)
            return false;

        return select.Value.Options switch
        {
            SessionConfigSelectOptions.Case1 flat =>
                flat.Value.Any(item => string.Equals(item.Value.Value, value, StringComparison.Ordinal)),
            SessionConfigSelectOptions.Case2 grouped =>
                grouped.Value.SelectMany(group => group.Options)
                    .Any(item => string.Equals(item.Value.Value, value, StringComparison.Ordinal)),
            _ => false
        };
    }

    internal static string? NormalizeOptionalModel(string? model)
    {
        var trimmed = model?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
