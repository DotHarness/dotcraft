using DotCraft.Tools;

namespace DotCraft.Mcp;

public interface IMcpToolRuntimeMetadata
{
    string ServerName { get; }
    long Generation { get; }
}

public static class McpToolRouting
{
    public static string ServerName(ToolRegistration registration) =>
        registration.Binding.Runtime is IMcpToolRuntimeMetadata runtime
            ? runtime.ServerName : registration.Definition.Id.SourceId;

    public static long? Generation(ToolRegistration registration) =>
        registration.Binding.Runtime is IMcpToolRuntimeMetadata runtime
            ? runtime.Generation
            : registration.Definition.Provenance.Origin == "binding" ? null : registration.Binding.Revision;
}
