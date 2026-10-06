using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Configuration;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

internal sealed class ConfigRequestHandler(ConfigurationService? configuration) : IAppServerDomainHandler
{
    public void RegisterMethods(AppServerMethodTable table)
    {
        table.Map(Contract.AppServerRpc.ConfigRead, HandleReadAsync);
        table.Map(Contract.AppServerRpc.ConfigValueWrite, HandleValueWriteAsync);
        table.Map(Contract.AppServerRpc.ConfigBatchWrite, HandleBatchWriteAsync);
    }

    private Task<object?> HandleReadAsync(
        AppServerTypedRequest<Contract.ConfigReadParams> request,
        CancellationToken ct)
    {
        var service = Require(Contract.AppServerMethodNames.ConfigRead);
        var result = service.Read(request.Params.IncludeLayers.IsSet && request.Params.IncludeLayers.Value);
        return Task.FromResult<object?>(new Contract.ConfigReadResult
        {
            Config = ToElement(result.Config),
            Origins = result.Origins.ToDictionary(pair => pair.Key, pair => ToContract(pair.Value), StringComparer.Ordinal),
            Layers = result.Layers is { } layers
                ? layers.Select(layer => new Contract.ConfigLayer
                {
                    Name = ToName(layer.Layer),
                    Version = layer.Layer.Version,
                    Config = ToElement(layer.Config)
                }).ToArray()
                : default(Protocol.Optional<IReadOnlyList<Contract.ConfigLayer>>)
        });
    }

    private Task<object?> HandleValueWriteAsync(
        AppServerTypedRequest<Contract.ConfigValueWriteParams> request,
        CancellationToken ct)
    {
        var p = request.Params;
        return WriteAsync(
            Contract.AppServerMethodNames.ConfigValueWrite,
            [ToEdit(p.KeyPath, p.Value, p.MergeStrategy)],
            p.FilePath,
            p.ExpectedVersion,
            ct);
    }

    private Task<object?> HandleBatchWriteAsync(
        AppServerTypedRequest<Contract.ConfigBatchWriteParams> request,
        CancellationToken ct)
    {
        var p = request.Params;
        return WriteAsync(
            Contract.AppServerMethodNames.ConfigBatchWrite,
            p.Edits.Select(edit => ToEdit(edit.KeyPath, edit.Value, edit.MergeStrategy)).ToArray(),
            p.FilePath,
            p.ExpectedVersion,
            ct);
    }

    private async Task<object?> WriteAsync(
        string method,
        IReadOnlyList<ConfigEdit> edits,
        string? filePath,
        string? expectedVersion,
        CancellationToken ct)
    {
        var service = Require(method);
        ConfigWriteResult result;
        try
        {
            result = await service.WriteAsync(edits, filePath, expectedVersion, method, ct);
        }
        catch (ConfigWriteException ex)
        {
            throw AppServerErrors.ConfigWrite(ex);
        }

        return new Contract.ConfigWriteResult
        {
            Status = result.Status == ConfigWriteStatus.OkOverridden ? "okOverridden" : "ok",
            Version = result.Version,
            FilePath = result.FilePath,
            OverriddenMetadata = result.OverriddenMetadata is { } overridden
                ? new Contract.ConfigOverriddenMetadata
                {
                    Message = overridden.Message,
                    OverridingLayer = ToContract(overridden.OverridingLayer),
                    EffectiveValue = overridden.EffectiveValue is null ? null : ToElement(overridden.EffectiveValue)
                }
                : null
        };
    }

    private ConfigurationService Require(string method) =>
        configuration ?? throw AppServerErrors.MethodNotFound(method);

    private static ConfigEdit ToEdit(string keyPath, JsonElement? value, string mergeStrategy)
    {
        var strategy = mergeStrategy switch
        {
            "replace" => ConfigMergeStrategy.Replace,
            "upsert" => ConfigMergeStrategy.Upsert,
            _ => throw AppServerErrors.InvalidParams("'mergeStrategy' must be 'replace' or 'upsert'.")
        };
        var node = value is { ValueKind: not JsonValueKind.Null } element ? JsonNode.Parse(element.GetRawText()) : null;
        return new ConfigEdit(keyPath, node, strategy);
    }

    private static Contract.ConfigLayerMetadata ToContract(ConfigLayerInfo layer) => new()
    {
        Name = ToName(layer),
        Version = layer.Version
    };

    private static Contract.ConfigLayerName ToName(ConfigLayerInfo layer) => new()
    {
        Type = layer.Type == ConfigLayerType.User ? "user" : "workspace",
        File = layer.FilePath
    };

    private static JsonElement ToElement(JsonNode node) => JsonSerializer.SerializeToElement(node);
}
