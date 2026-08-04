using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace DotCraft.ProtocolGen;

public static class SdkBindingArtifactGenerator
{
    public const string TypeScriptRoot = "sdk/typescript/src/generated/appserver";
    public const string PythonRoot = "sdk/python/dotcraft/_generated/appserver";
    private const string ModelGeneratorVersion = "0.42.2";

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
            [$"{TypeScriptRoot}/index.ts"] = EmitTypeScriptIndex(),
            ["sdk/python/dotcraft/_generated/__init__.py"] = GeneratedPythonHeader(),
            ["sdk/python/dotcraft/contracts.py"] = EmitPythonPublicContracts(ir),
            [$"{PythonRoot}/__init__.py"] = EmitPythonIndex(),
            [$"{PythonRoot}/models_generated.py"] = GeneratePythonModels(contractFiles["schemas/appserver.schema.json"], repositoryRoot),
            [$"{PythonRoot}/item_payloads_generated.py"] = EmitPythonItemPayloads(ir),
            [$"{PythonRoot}/notification_registry_generated.py"] = EmitPythonRegistries(ir),
            [$"{PythonRoot}/client_methods_generated.py"] = EmitPythonClientMixin(ir),
            [$"{PythonRoot}/method_groups_generated.py"] = EmitPythonMethodGroups(ir),
            [$"{PythonRoot}/protocol_info_generated.py"] = EmitPythonProtocolInfo(ir, hash)
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

    private static string EmitPythonPublicContracts(ContractIr ir)
    {
        var names = ir.Types.Select(type => type.Name).Order(StringComparer.Ordinal).ToArray();
        var source = new StringBuilder("\"\"\"Generated public, I/O-free AppServer contracts.\"\"\"\n\n")
            .AppendLine("from ._generated.appserver import (")
            .AppendLine("    APPSERVER_PROTOCOL_VERSION,")
            .AppendLine("    CLIENT_NOTIFICATION_METHODS,")
            .AppendLine("    CLIENT_REQUEST_METHODS,")
            .AppendLine("    CONTRACT_FORMAT_VERSION,")
            .AppendLine("    CONTRACT_SHA256,")
            .AppendLine("    CONTRACT_VERSION,")
            .AppendLine("    SERVER_NOTIFICATION_METHODS,")
            .AppendLine("    SERVER_NOTIFICATION_MODELS,")
            .AppendLine("    SERVER_REQUEST_METHODS,")
            .AppendLine("    SERVER_REQUEST_MODELS,")
            .AppendLine("    parse_server_notification,")
            .AppendLine("    parse_server_request,")
            .AppendLine(")")
            .AppendLine("from ._generated.appserver.models_generated import (");
        foreach (var name in names)
            source.Append("    ").Append(name).AppendLine(",");
        source.AppendLine(")")
            .AppendLine()
            .AppendLine("_CONTRACT_MODEL_NAMES = [");
        foreach (var name in names)
            source.Append("    ").Append(PythonString(name)).AppendLine(",");
        source.AppendLine("]")
            .AppendLine("for _name in _CONTRACT_MODEL_NAMES:")
            .AppendLine("    _value = globals()[_name]")
            .AppendLine("    if isinstance(_value, type):")
            .AppendLine("        _value.__module__ = __name__")
            .AppendLine()
            .AppendLine("__all__ = [")
            .AppendLine("    \"APPSERVER_PROTOCOL_VERSION\",")
            .AppendLine("    \"CLIENT_NOTIFICATION_METHODS\",")
            .AppendLine("    \"CLIENT_REQUEST_METHODS\",")
            .AppendLine("    \"CONTRACT_FORMAT_VERSION\",")
            .AppendLine("    \"CONTRACT_SHA256\",")
            .AppendLine("    \"CONTRACT_VERSION\",")
            .AppendLine("    \"SERVER_NOTIFICATION_METHODS\",")
            .AppendLine("    \"SERVER_NOTIFICATION_MODELS\",")
            .AppendLine("    \"SERVER_REQUEST_METHODS\",")
            .AppendLine("    \"SERVER_REQUEST_MODELS\",")
            .AppendLine("    \"parse_server_notification\",")
            .AppendLine("    \"parse_server_request\",");
        foreach (var name in names)
            source.Append("    ").Append(PythonString(name)).AppendLine(",");
        source.AppendLine("]");
        return Normalize(source.ToString());
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

    private static string EmitPythonClientMixin(ContractIr ir)
    {
        var methods = ir.Methods.Where(static method => method.Direction == "clientToServer").ToArray();
        var imports = methods.SelectMany(static method => new[] { TypeName(method.ParamsType), TypeName(method.ResultType) })
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
        var source = new StringBuilder(GeneratedPythonHeader())
            .AppendLine("from typing import Any")
            .AppendLine()
            .Append("from .models_generated import ").AppendLine(string.Join(", ", imports))
            .AppendLine()
            .AppendLine()
            .AppendLine("class GeneratedAppServerClientMixin:")
            .AppendLine("    async def _request(self, method: str, params: dict | None = None) -> Any:")
            .AppendLine("        raise NotImplementedError")
            .AppendLine()
            .AppendLine("    async def _notify(self, method: str, params: dict) -> None:")
            .AppendLine("        raise NotImplementedError")
            .AppendLine();

        foreach (var method in methods)
        {
            var functionName = "rpc_" + PythonIdentifier(method.Name);
            var paramsType = TypeName(method.ParamsType);
            var resultType = TypeName(method.ResultType);
            if (method.Kind == "request")
            {
                source.Append("    async def ").Append(functionName).Append("(self, params: ").Append(paramsType).Append(") -> ").Append(resultType).AppendLine(":")
                    .Append("        raw = await self._request(").Append(PythonString(method.Name)).AppendLine(", params.model_dump(by_alias=True, exclude_unset=True, mode=\"json\"))")
                    .Append("        return ").Append(resultType).AppendLine(".model_validate(raw)")
                    .AppendLine();
            }
            else
            {
                source.Append("    async def ").Append(functionName).Append("(self, params: ").Append(paramsType).Append(" | None = None) -> None:").AppendLine()
                    .Append("        payload = (params or ").Append(paramsType).AppendLine("()).model_dump(by_alias=True, exclude_unset=True, mode=\"json\")")
                    .Append("        await self._notify(").Append(PythonString(method.Name)).AppendLine(", payload)")
                    .AppendLine();
            }
        }
        return Normalize(source.ToString());
    }

    private static string EmitPythonRegistries(ContractIr ir)
    {
        var notifications = ir.Methods.Where(static method => method.Direction == "serverToClient" && method.Kind == "notification").ToArray();
        var requests = ir.Methods.Where(static method => method.Direction == "serverToClient" && method.Kind == "request").ToArray();
        var imports = notifications.Select(static method => TypeName(method.ParamsType))
            .Concat(requests.SelectMany(static method => new[] { TypeName(method.ParamsType), TypeName(method.ResultType) }))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
        var source = new StringBuilder(GeneratedPythonHeader())
            .AppendLine("from typing import Any")
            .AppendLine()
            .AppendLine("from pydantic import BaseModel")
            .AppendLine()
            .Append("from .models_generated import ").AppendLine(string.Join(", ", imports))
            .AppendLine()
            .AppendLine()
            .AppendLine("SERVER_NOTIFICATION_MODELS: dict[str, type[BaseModel]] = {");
        foreach (var method in notifications)
            source.Append("    ").Append(PythonString(method.Name)).Append(": ").Append(TypeName(method.ParamsType)).AppendLine(",");
        source.AppendLine("}")
            .AppendLine()
            .AppendLine("SERVER_REQUEST_MODELS: dict[str, tuple[type[BaseModel], type[BaseModel]]] = {");
        foreach (var method in requests)
            source.Append("    ").Append(PythonString(method.Name)).Append(": (").Append(TypeName(method.ParamsType)).Append(", ").Append(TypeName(method.ResultType)).AppendLine("),");
        source.AppendLine("}")
            .AppendLine()
            .AppendLine()
            .AppendLine("def parse_server_notification(method: str, params: dict[str, Any]) -> BaseModel | dict[str, Any]:")
            .AppendLine("    model = SERVER_NOTIFICATION_MODELS.get(method)")
            .AppendLine("    return params if model is None else model.model_validate(params)")
            .AppendLine()
            .AppendLine()
            .AppendLine("def parse_server_request(method: str, params: dict[str, Any]) -> BaseModel | dict[str, Any]:")
            .AppendLine("    models = SERVER_REQUEST_MODELS.get(method)")
            .AppendLine("    return params if models is None else models[0].model_validate(params)");
        return Normalize(source.ToString());
    }

    private static string EmitPythonItemPayloads(ContractIr ir)
    {
        var imports = ir.ItemPayloads.Select(static payload => TypeName(payload.TypeId));
        var source = new StringBuilder(GeneratedPythonHeader())
            .AppendLine("from typing import Any")
            .AppendLine()
            .AppendLine("from pydantic import BaseModel")
            .AppendLine()
            .Append("from .models_generated import ").AppendLine(string.Join(", ", imports))
            .AppendLine()
            .AppendLine()
            .AppendLine("SESSION_ITEM_PAYLOAD_MODELS: dict[str, type[BaseModel]] = {");
        foreach (var payload in ir.ItemPayloads)
            source.Append("    ").Append(PythonString(payload.Kind)).Append(": ").Append(TypeName(payload.TypeId)).AppendLine(",");
        source.AppendLine("}")
            .AppendLine()
            .AppendLine()
            .AppendLine("def parse_session_item_payload(payload_kind: str | None, payload: Any) -> BaseModel | Any:")
            .AppendLine("    model = SESSION_ITEM_PAYLOAD_MODELS.get(payload_kind) if payload_kind is not None else None")
            .AppendLine("    return payload if model is None or payload is None else model.model_validate(payload)");
        return Normalize(source.ToString());
    }

    private static string EmitPythonMethodGroups(ContractIr ir)
    {
        var source = new StringBuilder(GeneratedPythonHeader());
        AppendPythonGroup(source, "CLIENT_REQUEST_METHODS", ir.Methods.Where(static method => method.Direction == "clientToServer" && method.Kind == "request"));
        AppendPythonGroup(source, "CLIENT_NOTIFICATION_METHODS", ir.Methods.Where(static method => method.Direction == "clientToServer" && method.Kind == "notification"));
        AppendPythonGroup(source, "SERVER_REQUEST_METHODS", ir.Methods.Where(static method => method.Direction == "serverToClient" && method.Kind == "request"));
        AppendPythonGroup(source, "SERVER_NOTIFICATION_METHODS", ir.Methods.Where(static method => method.Direction == "serverToClient" && method.Kind == "notification"));
        return Normalize(source.ToString());
    }

    private static void AppendPythonGroup(StringBuilder source, string name, IEnumerable<ContractMethod> methods)
    {
        source.Append(name).AppendLine(" = (");
        foreach (var method in methods)
            source.Append("    ").Append(PythonString(method.Name)).AppendLine(",");
        source.AppendLine(")").AppendLine();
    }

    private static string EmitPythonProtocolInfo(ContractIr ir, string hash) => Normalize(GeneratedPythonHeader() + $"""
        CONTRACT_FORMAT_VERSION = {ir.FormatVersion}
        CONTRACT_VERSION = {PythonString(ir.ContractVersion)}
        APPSERVER_PROTOCOL_VERSION = {PythonString(ir.ProtocolVersion)}
        CONTRACT_SHA256 = {PythonString(hash)}
        """);

    private static string EmitPythonIndex() => Normalize(GeneratedPythonHeader() + """
        from .client_methods_generated import GeneratedAppServerClientMixin
        from .method_groups_generated import (
            CLIENT_NOTIFICATION_METHODS,
            CLIENT_REQUEST_METHODS,
            SERVER_NOTIFICATION_METHODS,
            SERVER_REQUEST_METHODS,
        )
        from .item_payloads_generated import SESSION_ITEM_PAYLOAD_MODELS, parse_session_item_payload
        from .notification_registry_generated import (
            SERVER_NOTIFICATION_MODELS,
            SERVER_REQUEST_MODELS,
            parse_server_notification,
            parse_server_request,
        )
        from .protocol_info_generated import (
            APPSERVER_PROTOCOL_VERSION,
            CONTRACT_FORMAT_VERSION,
            CONTRACT_SHA256,
            CONTRACT_VERSION,
        )

        __all__ = [
            "APPSERVER_PROTOCOL_VERSION",
            "CLIENT_NOTIFICATION_METHODS",
            "CLIENT_REQUEST_METHODS",
            "CONTRACT_FORMAT_VERSION",
            "CONTRACT_SHA256",
            "CONTRACT_VERSION",
            "GeneratedAppServerClientMixin",
            "SESSION_ITEM_PAYLOAD_MODELS",
            "SERVER_NOTIFICATION_METHODS",
            "SERVER_NOTIFICATION_MODELS",
            "SERVER_REQUEST_METHODS",
            "SERVER_REQUEST_MODELS",
            "parse_server_notification",
            "parse_server_request",
            "parse_session_item_payload",
        ]
        """);

    private static string GeneratePythonModels(string schema, string repositoryRoot)
    {
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "dotcraft-protocolgen-python", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);
        try
        {
            var input = Path.Combine(temporaryRoot, "appserver.schema.json");
            var output = Path.Combine(temporaryRoot, "models_generated.py");
            File.WriteAllText(input, schema, new UTF8Encoding(false));

            var python = ResolvePython(repositoryRoot);
            var version = RunPython(python, ["-c", "import importlib.metadata; print(importlib.metadata.version('datamodel-code-generator'))"]);
            if (!string.Equals(version.Trim(), ModelGeneratorVersion, StringComparison.Ordinal))
                throw new ProtocolGenerationException("DPG030", $"datamodel-code-generator {ModelGeneratorVersion} is required; found '{version.Trim()}'.");

            RunPython(python, [
                "-m", "datamodel_code_generator",
                "--input", input,
                "--input-file-type", "jsonschema",
                "--output", output,
                "--output-model-type", "pydantic_v2.BaseModel",
                "--target-python-version", "3.10",
                "--snake-case-field",
                "--allow-population-by-field-name",
                "--extra-fields", "allow",
                "--use-union-operator",
                "--use-title-as-name",
                "--disable-timestamp",
                "--encoding", "utf-8",
                "--formatters", "black", "isort"
            ]);
            return Normalize(File.ReadAllText(output));
        }
        finally
        {
            if (Directory.Exists(temporaryRoot))
                Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    private static string ResolvePython(string repositoryRoot)
    {
        var configured = Environment.GetEnvironmentVariable("DOTCRAFT_PYTHON");
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        var relative = OperatingSystem.IsWindows()
            ? Path.Combine("sdk", "python", ".venv", "Scripts", "python.exe")
            : Path.Combine("sdk", "python", ".venv", "bin", "python");
        var local = Path.Combine(repositoryRoot, relative);
        if (File.Exists(local))
            return local;

        throw new ProtocolGenerationException(
            "DPG030",
            $"Python model generation requires DOTCRAFT_PYTHON or the repository environment '{relative}'. Install sdk/python development dependencies with datamodel-code-generator {ModelGeneratorVersion}.");
    }

    private static string RunPython(string python, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = python,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)
            ?? throw new ProtocolGenerationException("DPG030", "Could not start the Python model generator.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new ProtocolGenerationException("DPG030", $"Python model generation failed: {error.Trim()}");
        return output;
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

    private static string PythonString(string value) => $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    private static string PythonIdentifier(string method)
    {
        var result = new StringBuilder();
        for (var index = 0; index < method.Length; index++)
        {
            var character = method[index];
            if (!char.IsLetterOrDigit(character))
            {
                if (result.Length > 0 && result[^1] != '_')
                    result.Append('_');
                continue;
            }
            if (char.IsUpper(character) && index > 0 && char.IsLower(method[index - 1]))
                result.Append('_');
            result.Append(char.ToLowerInvariant(character));
        }
        return result.ToString().Trim('_');
    }

    private static string TypeName(string typeId) => typeId[(typeId.IndexOf('.') + 1)..];

    private static string GeneratedTypeScriptHeader() => "// <auto-generated />\n\n";

    private static string GeneratedPythonHeader() => "# Generated by DotCraft.ProtocolGen. Do not edit.\n";

    private static string Normalize(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd() + "\n";
}
