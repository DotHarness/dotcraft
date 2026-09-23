using DotCraft.AppServer;
using DotCraft.Protocol;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.SessionImport;

public sealed class SessionImportProtocolExtension(SessionImportService imports) : IAppServerContractExtension
{
    public IReadOnlyCollection<string> Methods { get; } =
    [
        "import/sessions/detect", "import/sessions/run", "import/settings/get", "import/settings/set"
    ];

    public IReadOnlyCollection<IRpcMethodDescriptor> ContractMethods { get; } =
    [
        Contract.AppServerRpc.ImportSessionsDetect,
        Contract.AppServerRpc.ImportSessionsRun,
        Contract.AppServerRpc.ImportSettingsGet,
        Contract.AppServerRpc.ImportSettingsSet
    ];

    public void ContributeCapabilities(AppServerCapabilityBuilder builder) =>
        builder.SetExtension("sessionImport", new Contract.SessionImportCapabilities
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
                "import/sessions/detect" => await DetectAsync((Contract.ImportSessionsDetectParams)parameters, context.CancellationToken),
                "import/sessions/run" => Run((Contract.ImportSessionsRunParams)parameters),
                "import/settings/get" => new Contract.ImportSettingsResult { Settings = imports.GetSettings() },
                "import/settings/set" => UpdateSettings((Contract.ImportSettingsSetParams)parameters),
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

    private async Task<Contract.ImportSessionsDetectResult> DetectAsync(Contract.ImportSessionsDetectParams p, CancellationToken ct) =>
        new() { Sources = await imports.DetectAsync(p.Sources.IsSet ? p.Sources.Value : null, ct) };

    private Contract.ImportSessionsRunResult Run(Contract.ImportSessionsRunParams p) =>
        new()
        {
            ImportId = imports.Run(
                p.Sources ?? throw new ArgumentException("sources is required."),
                p.SessionIds.IsSet ? p.SessionIds.Value : null)
        };

    private Contract.ImportSettingsResult UpdateSettings(Contract.ImportSettingsSetParams p) =>
        new()
        {
            Settings = imports.UpdateSettings(
                p.SyncEnabled.IsSet ? p.SyncEnabled.Value : null,
                p.Sources.IsSet ? p.Sources.Value ?? throw new ArgumentException("sources must be an array.") : null)
        };
}
