using DotCraft.Tools;
using ModelContextProtocol.Protocol;

namespace DotCraft.Mcp;

public interface IMcpElicitationHandler
{
    ValueTask<ElicitResult> HandleAsync(
        ToolInvocationContext context,
        string serverName,
        ElicitRequestParams request,
        CancellationToken cancellationToken);
}
