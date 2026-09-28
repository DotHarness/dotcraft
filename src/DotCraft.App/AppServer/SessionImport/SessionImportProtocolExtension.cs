using DotCraft.AppServer;
using DotCraft.Protocol;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.SessionImport;

public sealed class SessionImportProtocolExtension(SessionImportService imports, DotCraft.Mcp.McpClientManager? mcp = null) : IAppServerContractExtension
{
    public IReadOnlyCollection<string> Methods { get; } =
    [
        "import/detect", "import/run", "import/settings/get", "import/settings/set", "import/history/list"
    ];

    public IReadOnlyCollection<IRpcMethodDescriptor> ContractMethods { get; } =
    [
        Contract.AppServerRpc.ImportDetect,
        Contract.AppServerRpc.ImportRun,
        Contract.AppServerRpc.ImportSettingsGet,
        Contract.AppServerRpc.ImportSettingsSet,
        Contract.AppServerRpc.ImportHistoryList
    ];

    public void ContributeCapabilities(AppServerCapabilityBuilder builder) =>
        builder.SetExtension("agentImport", new Contract.AgentImportCapabilities
        {
            Version = 1,
            Sources = imports.SupportedSources
        });

    public async Task<object?> HandleContractAsync(
        IRpcMethodDescriptor descriptor,
        object parameters,
        AppServerIncomingMessage message,
        AppServerExtensionContext context)
    {
        try
        {
            return descriptor.Name switch
            {
                "import/detect" => await DetectAsync((Contract.ImportDetectParams)parameters, context.CancellationToken),
                "import/run" => Run((Contract.ImportRunParams)parameters),
                "import/settings/get" => new Contract.ImportSettingsResult { Settings = imports.GetSettings() },
                "import/settings/set" => UpdateSettings((Contract.ImportSettingsSetParams)parameters),
                "import/history/list" => await ReadHistoryAsync(context.CancellationToken),
                _ => throw AppServerErrors.MethodNotFound(message.Method ?? descriptor.Name)
            };
        }
        catch (SessionImportException ex)
        {
            throw AppServerErrors.SessionImport(ex.Code, ex.Message);
        }
        catch (ArgumentException ex)
        {
            throw AppServerErrors.InvalidParams(ex.Message);
        }
    }

    private async Task<Contract.ImportDetectResult> DetectAsync(Contract.ImportDetectParams p, CancellationToken ct) =>
        new() { Sources = await imports.DetectAsync(p.Sources.IsSet ? p.Sources.Value : null, ct) };

    private async Task<Contract.ImportHistoryResult> ReadHistoryAsync(CancellationToken ct)
    {
        var statuses = mcp == null ? [] : await mcp.ListStatusesAsync(ct);
        return new Contract.ImportHistoryResult
        {
            Imports = imports.ReadHistory(),
            Attention = imports.ReadAttention(statuses.Where(s => s.Enabled && (s.StartupState == "error" || s.AuthStatus == "notLoggedIn"))
                .Select(s => s.Origin.Kind + "\n" + s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase))
        };
    }

    private Contract.ImportRunResult Run(Contract.ImportRunParams p) =>
        new()
        {
            ImportId = imports.Run(
                p.Sources ?? throw new ArgumentException("sources is required."),
                p.Selection ?? throw new ArgumentException("selection is required."),
                p.Items ?? throw new ArgumentException("items is required."))
        };

    private Contract.ImportSettingsResult UpdateSettings(Contract.ImportSettingsSetParams p) =>
        new()
        {
            Settings = imports.UpdateSettings(
                p.SyncEnabled.IsSet ? p.SyncEnabled.Value : null,
                p.Sources.IsSet ? p.Sources.Value ?? throw new ArgumentException("sources must be an array.") : null,
                p.Selection.IsSet ? p.Selection.Value ?? throw new ArgumentException("selection is required.") : null)
        };
}
