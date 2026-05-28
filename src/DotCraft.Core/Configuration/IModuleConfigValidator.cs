namespace DotCraft.Configuration;

/// <summary>
/// Validates module-specific configuration.
/// Modules can implement this interface to define their own validation rules.
/// </summary>
/// <typeparam name="TConfig">The configuration type to validate.</typeparam>
public interface IModuleConfigValidator<in TConfig> where TConfig : class
{
    /// <summary>
    /// Validates the configuration.
    /// </summary>
    /// <param name="config">The configuration to validate.</param>
    /// <returns>List of validation errors, empty if valid.</returns>
    IReadOnlyList<string> Validate(TConfig config);
}
