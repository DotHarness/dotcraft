namespace DotCraft.Protocol.AppServer;

/// <summary>
/// Interface for the automations request handler, allowing <see cref="AppServerRequestHandler"/>
/// in DotCraft.Core to dispatch <c>automation/*</c> methods without referencing DotCraft.Automations.
/// </summary>
public interface IAutomationsRequestHandler
{
    Task<object?> HandleTaskListAsync(AppServerIncomingMessage msg, CancellationToken ct);
    Task<object?> HandleTaskReadAsync(AppServerIncomingMessage msg, CancellationToken ct);
    Task<object?> HandleTaskCreateAsync(AppServerIncomingMessage msg, CancellationToken ct);
    Task<object?> HandleTaskRunAsync(AppServerIncomingMessage msg, CancellationToken ct);
    Task<object?> HandleTaskDeleteAsync(AppServerIncomingMessage msg, CancellationToken ct);
    Task<object?> HandleTaskUpdateBindingAsync(AppServerIncomingMessage msg, CancellationToken ct);
    Task<object?> HandleTemplateListAsync(AppServerIncomingMessage msg, CancellationToken ct);
    Task<object?> HandleTemplateSaveAsync(AppServerIncomingMessage msg, CancellationToken ct);
    Task<object?> HandleTemplateDeleteAsync(AppServerIncomingMessage msg, CancellationToken ct);
}
