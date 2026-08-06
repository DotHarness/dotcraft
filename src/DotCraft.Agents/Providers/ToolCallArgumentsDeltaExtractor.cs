namespace DotCraft.Agents;

/// <summary>A provider-native tool-call arguments fragment projected into a neutral shape.</summary>
public readonly record struct ProviderToolCallArgumentsDelta(
    int ToolCallIndex,
    string? ToolName,
    string? CallId,
    string ArgumentsDelta);

/// <summary>Extracts tool-call arguments fragments from a provider SDK streaming object.</summary>
public interface IToolCallArgumentsDeltaExtractor
{
    IEnumerable<ProviderToolCallArgumentsDelta> Extract(object? rawRepresentation);
}
