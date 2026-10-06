namespace DotCraft.Configuration;

public sealed class ConfigFieldDescriptor
{
    public required string KeyPath { get; init; }

    public required Type ValueType { get; init; }

    public object? DefaultValue { get; init; }

    public bool Sensitive { get; init; }

    public int? Min { get; init; }

    public int? Max { get; init; }

    public IReadOnlyList<string>? Options { get; init; }

    public ReloadBehavior Reload { get; init; }

    public string? SubsystemKey { get; init; }

    public required Func<AppConfig, object?> Get { get; init; }

    public required Action<AppConfig, object?> Set { get; init; }
}

public interface IConfigDescriptorRegistry
{
    IReadOnlyList<ConfigFieldDescriptor> Fields { get; }
}
