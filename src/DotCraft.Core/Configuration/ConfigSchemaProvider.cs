namespace DotCraft.Configuration;

/// <summary>
/// Supplies the Dashboard/AppServer configuration schema for the current host.
/// </summary>
public interface IConfigSchemaProvider
{
    /// <summary>
    /// Returns the complete configuration schema.
    /// </summary>
    IReadOnlyList<ConfigSchemaSection> GetConfigSchema();
}
