namespace DotCraft.Agents;

/// <summary>A content-free diagnostic event emitted by model runtime infrastructure.</summary>
public sealed record ModelRuntimeDiagnostic(
    string Name,
    IReadOnlyDictionary<string, object?> Properties);

/// <summary>Receives structured provider diagnostics without assigning runtime identity.</summary>
public interface IModelRuntimeDiagnostics
{
    void Record(ModelRuntimeDiagnostic diagnostic);
}
