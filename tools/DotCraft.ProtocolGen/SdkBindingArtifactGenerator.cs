using System.Text;
using System.Text.Json;

namespace DotCraft.ProtocolGen;

public static class SdkBindingArtifactGenerator
{
    public const string TypeScriptRoot = "sdk/typescript/src/generated/appserver";

    public static IReadOnlyDictionary<string, string> Build(
        ContractIr ir,
        IReadOnlyDictionary<string, string> contractFiles,
        string repositoryRoot)
    {
        var hash = contractFiles["contract.sha256"].Trim();
        var sdkVersion = ReadTypeScriptSdkVersion(repositoryRoot);
        var files = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            [$"{TypeScriptRoot}/models.generated.ts"] = EmitTypeScriptModels(ir),
            [$"{TypeScriptRoot}/client-requests.generated.ts"] = EmitTypeScriptMethodMap(ir, "ClientRequestMethods", "clientToServer", "request"),
            [$"{TypeScriptRoot}/client-notifications.generated.ts"] = EmitTypeScriptMethodMap(ir, "ClientNotificationMethods", "clientToServer", "notification"),
            [$"{TypeScriptRoot}/server-requests.generated.ts"] = EmitTypeScriptMethodMap(ir, "ServerRequestMethods", "serverToClient", "request"),
            [$"{TypeScriptRoot}/server-notifications.generated.ts"] = EmitTypeScriptMethodMap(ir, "ServerNotificationMethods", "serverToClient", "notification"),
            [$"{TypeScriptRoot}/item-payloads.generated.ts"] = EmitTypeScriptItemPayloads(ir),
            [$"{TypeScriptRoot}/method-groups.generated.ts"] = EmitTypeScriptMethodGroups(ir, hash),
            [$"{TypeScriptRoot}/protocol-info.generated.ts"] = EmitTypeScriptProtocolInfo(ir, hash, sdkVersion),
            [$"{TypeScriptRoot}/index.ts"] = EmitTypeScriptIndex()
        };
        return files;
    }

    private static string ReadTypeScriptSdkVersion(string repositoryRoot)
    {
        var packagePath = Path.Combine(repositoryRoot, "sdk", "typescript", "package.json");
        if (!File.Exists(packagePath))
            return "0+unknown";

        using var typeScriptPackage = JsonDocument.Parse(File.ReadAllText(packagePath));
        return typeScriptPackage.RootElement.GetProperty("version").GetString()
               ?? throw new InvalidOperationException("The TypeScript SDK package version is missing.");
    }

    private static string EmitTypeScriptModels(ContractIr ir)
    {
        var source = new StringBuilder(GeneratedTypeScriptHeader())
            .AppendLine("export type JsonPrimitive = null | boolean | number | string;")
            .AppendLine("export type JsonValue = JsonPrimitive | JsonValue[] | { [key: string]: JsonValue };")
            .AppendLine();

        foreach (var type in ir.Types)
        {
            switch (type.Kind)
            {
                case ContractTypeKind.Union:
                    source.Append("export type ").Append(type.Name).Append(" = ")
                        .Append(string.Join(" | ", type.Variants.Select(variant => TypeName(variant.TypeId))))
                        .AppendLine(";")
                        .AppendLine();
                    break;
                case ContractTypeKind.Enum:
                    source.Append("export type ").Append(type.Name).AppendLine(" = string;")
                        .AppendLine();
                    break;
                case ContractTypeKind.Object:
                    source.Append("export interface ").Append(type.Name).AppendLine(" {");
                    foreach (var field in type.Fields)
                    {
                        source.Append("  ").Append(TypeScriptProperty(field.Name));
                        if (!field.Required)
                            source.Append('?');
                        source.Append(": ").Append(TypeScriptType(field.Type));
                        if (field.Nullable && field.Type.Kind != ContractTypeRefKind.AnyJson)
                            source.Append(" | null");
                        if (field.Constant is not null)
                            source.Append(" & ").Append(TypeScriptString(field.Constant));
                        source.AppendLine(";");
                    }
                    source.AppendLine("  [key: string]: unknown;")
                        .AppendLine("}")
                        .AppendLine();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type.Kind));
            }
        }

        return Normalize(source.ToString());
    }

    private static string EmitTypeScriptMethodMap(
        ContractIr ir,
        string mapName,
        string direction,
        string kind)
    {
        var methods = ir.Methods.Where(method => method.Direction == direction && method.Kind == kind).ToArray();
        var source = new StringBuilder(GeneratedTypeScriptHeader())
            .AppendLine("import type * as Models from \"./models.generated.js\";")
            .AppendLine()
            .Append("export interface ").Append(mapName).AppendLine(" {");
        foreach (var method in methods)
        {
            source.Append("  ").Append(TypeScriptString(method.Name)).Append(": { params: Models.")
                .Append(TypeName(method.ParamsType)).Append("; result: Models.")
                .Append(TypeName(method.ResultType)).AppendLine(" };");
        }
        source.AppendLine("}")
            .AppendLine()
            .Append("export type ").Append(mapName[..^7]).Append("Method = keyof ").Append(mapName).AppendLine(";");
        return Normalize(source.ToString());
    }

    private static string EmitTypeScriptMethodGroups(ContractIr ir, string hash)
    {
        var source = new StringBuilder(GeneratedTypeScriptHeader())
            .Append("export const APP_SERVER_CONTRACT_HASH = ").Append(TypeScriptString(hash)).AppendLine(" as const;")
            .AppendLine("export const APP_SERVER_METHOD_GROUPS = {");
        AppendGroup(source, "clientRequests", ir.Methods.Where(static method => method.Direction == "clientToServer" && method.Kind == "request"));
        AppendGroup(source, "clientNotifications", ir.Methods.Where(static method => method.Direction == "clientToServer" && method.Kind == "notification"));
        AppendGroup(source, "serverRequests", ir.Methods.Where(static method => method.Direction == "serverToClient" && method.Kind == "request"));
        AppendGroup(source, "serverNotifications", ir.Methods.Where(static method => method.Direction == "serverToClient" && method.Kind == "notification"));
        source.AppendLine("} as const;");
        return Normalize(source.ToString());
    }

    private static void AppendGroup(StringBuilder source, string name, IEnumerable<ContractMethod> methods)
    {
        source.Append("  ").Append(name).AppendLine(": [");
        foreach (var method in methods)
            source.Append("    ").Append(TypeScriptString(method.Name)).AppendLine(",");
        source.AppendLine("  ],");
    }

    private static string EmitTypeScriptIndex() => Normalize(GeneratedTypeScriptHeader() + """
        export * from "./models.generated.js";
        export * from "./item-payloads.generated.js";
        export * from "./client-requests.generated.js";
        export * from "./client-notifications.generated.js";
        export * from "./server-requests.generated.js";
        export * from "./server-notifications.generated.js";
        export * from "./method-groups.generated.js";
        export * from "./protocol-info.generated.js";
        """);

    private static string EmitTypeScriptProtocolInfo(ContractIr ir, string hash, string sdkVersion) =>
        Normalize(GeneratedTypeScriptHeader() + $"""
            export const SDK_VERSION = {TypeScriptString(sdkVersion)};
            export const CONTRACT_FORMAT_VERSION = {ir.FormatVersion};
            export const CONTRACT_VERSION = {TypeScriptString(ir.ContractVersion)};
            export const APPSERVER_PROTOCOL_VERSION = {TypeScriptString(ir.ProtocolVersion)};
            export const CONTRACT_SHA256 = {TypeScriptString(hash)};
            """);

    private static string EmitTypeScriptItemPayloads(ContractIr ir)
    {
        var source = new StringBuilder(GeneratedTypeScriptHeader())
            .AppendLine("import type * as Models from \"./models.generated.js\";")
            .AppendLine()
            .AppendLine("export interface SessionItemPayloadMap {");
        foreach (var payload in ir.ItemPayloads)
            source.Append("  ").Append(TypeScriptString(payload.Kind)).Append(": Models.").Append(TypeName(payload.TypeId)).AppendLine(";");
        source.AppendLine("}")
            .AppendLine()
            .AppendLine("export type KnownSessionItemPayloadKind = keyof SessionItemPayloadMap;")
            .AppendLine("export type KnownSessionItemPayload = SessionItemPayloadMap[KnownSessionItemPayloadKind];")
            .AppendLine("export type KnownSessionItem = {")
            .AppendLine("  [K in KnownSessionItemPayloadKind]: Omit<Models.SessionItem, \"payloadKind\" | \"payload\"> & {")
            .AppendLine("    payloadKind: K;")
            .AppendLine("    payload: SessionItemPayloadMap[K];")
            .AppendLine("  };")
            .AppendLine("}[KnownSessionItemPayloadKind];")
            .AppendLine()
            .AppendLine("export const SESSION_ITEM_PAYLOAD_KINDS = [");
        foreach (var payload in ir.ItemPayloads)
            source.Append("  ").Append(TypeScriptString(payload.Kind)).AppendLine(",");
        source.AppendLine("] as const satisfies readonly KnownSessionItemPayloadKind[];")
            .AppendLine()
            .AppendLine("const knownPayloadKinds = new Set<string>(SESSION_ITEM_PAYLOAD_KINDS);")
            .AppendLine()
            .AppendLine("export function isKnownSessionItemPayloadKind(kind: string): kind is KnownSessionItemPayloadKind {")
            .AppendLine("  return knownPayloadKinds.has(kind);")
            .AppendLine("}")
            .AppendLine()
            .AppendLine("export type ClassifiedSessionItemPayload =")
            .AppendLine("  | { known: true; kind: KnownSessionItemPayloadKind; raw: Models.JsonValue }")
            .AppendLine("  | { known: false; kind: string | null; raw: Models.JsonValue };")
            .AppendLine()
            .AppendLine("export function classifySessionItemPayload(kind: string | null | undefined, raw: Models.JsonValue): ClassifiedSessionItemPayload {")
            .AppendLine("  return kind !== null && kind !== undefined && isKnownSessionItemPayloadKind(kind)")
            .AppendLine("    ? { known: true, kind, raw }")
            .AppendLine("    : { known: false, kind: kind ?? null, raw };")
            .AppendLine("}")
            .AppendLine()
            .AppendLine("export type ParsedSessionItemPayload =")
            .AppendLine("  | { isKnown: true; payloadKind: KnownSessionItemPayloadKind; raw: Models.JsonValue | undefined; value: KnownSessionItemPayload | null }")
            .AppendLine("  | { isKnown: false; payloadKind: string | null; raw: Models.JsonValue | undefined; value: Models.JsonValue | undefined };")
            .AppendLine()
            .AppendLine("export function parseSessionItemPayload(payloadKind: string | null | undefined, raw: Models.JsonValue | undefined): ParsedSessionItemPayload {")
            .AppendLine("  if (payloadKind !== null && payloadKind !== undefined && isKnownSessionItemPayloadKind(payloadKind)) {")
            .AppendLine("    return { isKnown: true, payloadKind, raw, value: raw === null || raw === undefined ? null : raw as KnownSessionItemPayload };")
            .AppendLine("  }")
            .AppendLine("  return { isKnown: false, payloadKind: payloadKind ?? null, raw, value: raw };")
            .AppendLine("}");
        return Normalize(source.ToString());
    }

    private static string TypeScriptType(ContractTypeRef reference) => reference.Kind switch
    {
        ContractTypeRefKind.String or ContractTypeRefKind.DateTime => "string",
        ContractTypeRefKind.Boolean => "boolean",
        ContractTypeRefKind.Integer or ContractTypeRefKind.Number => "number",
        ContractTypeRefKind.AnyJson => "JsonValue",
        ContractTypeRefKind.Named => TypeName(reference.TypeId!),
        ContractTypeRefKind.Array => $"{TypeScriptType(reference.ElementType!)}[]",
        ContractTypeRefKind.Map => $"Record<string, {TypeScriptType(reference.ElementType!)}>",
        _ => throw new ArgumentOutOfRangeException(nameof(reference))
    };

    private static string TypeScriptProperty(string value) =>
        value.All(character => char.IsLetterOrDigit(character) || character is '_' or '$') && !char.IsDigit(value[0])
            ? value
            : TypeScriptString(value);

    private static string TypeScriptString(string value) => $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    private static string TypeName(string typeId) => typeId[(typeId.IndexOf('.') + 1)..];

    private static string GeneratedTypeScriptHeader() => "// <auto-generated />\n\n";

    private static string Normalize(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd() + "\n";
}
