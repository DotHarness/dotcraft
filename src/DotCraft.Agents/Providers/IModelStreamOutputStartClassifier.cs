using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

/// <summary>
/// Identifies the first streamed update produced by generation, so request timing excludes events that only
/// acknowledge the request. Without one, every update counts as output.
/// </summary>
public interface IModelStreamOutputStartClassifier
{
    bool IsOutputStart(ChatResponseUpdate update);
}
