using DotCraft.Modules;
using Microsoft.Extensions.Logging;

namespace DotCraft.Configuration;

/// <summary>
/// Provides unified configuration validation by delegating to each module's own validator.
/// Modules register their validation logic via <see cref="Modules.IDotCraftModule.ValidateConfig"/>.
/// </summary>
public sealed class ConfigValidator(ModuleRegistry moduleRegistry)
{
    /// <summary>
    /// Validates all module configurations.
    /// </summary>
    /// <param name="config">The application configuration.</param>
    /// <returns>Dictionary of module name to validation errors.</returns>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> ValidateAll(AppConfig config)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>();

        foreach (var module in moduleRegistry.Modules)
        {
            if (!module.IsEnabled(config)) continue;
            var errors = module.ValidateConfig(config);
            if (errors.Count > 0)
                result[module.Name] = errors;
        }

        return result;
    }

    /// <summary>
    /// Validates all configurations and logs warnings for invalid settings.
    /// </summary>
    /// <param name="config">The application configuration.</param>
    /// <returns>True if all configurations are valid.</returns>
    public bool ValidateAndLogErrors(AppConfig config, ILogger? logger = null)
    {
        var validationResults = ValidateAll(config);
        if (validationResults.Count == 0)
            return true;

        foreach (var (section, errors) in validationResults)
        {
            foreach (var error in errors)
            {
                logger?.LogWarning("Configuration warning in {Section}: {ValidationError}", section, error);
            }
        }

        return false;
    }
}
