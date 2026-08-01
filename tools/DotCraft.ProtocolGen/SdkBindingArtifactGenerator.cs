using System.Diagnostics;
using System.Text;

namespace DotCraft.ProtocolGen;

public static class SdkBindingArtifactGenerator
{
    public const string TypeScriptRoot = "sdk/typescript/src/generated/appserver";
    public const string PythonRoot = "sdk/python/dotcraft/_generated/appserver";
    private const string ModelGeneratorVersion = "0.42.2";

    public static IReadOnlyDictionary<string, string> Build(
        ContractIr ir,
        IReadOnlyDictionary<string, string> contractFiles)
    {
        var hash = contractFiles["contract.sha256"].Trim();
        var files = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            [$"{TypeScriptRoot}/models.generated.ts"] = EmitTypeScriptModels(ir),
            [$"{TypeScriptRoot}/client-requests.generated.ts"] = EmitTypeScriptMethodMap(ir, "ClientRequestMethods", "clientToServer", "request"),
            [$"{TypeScriptRoot}/client-notifications.generated.ts"] = EmitTypeScriptMethodMap(ir, "ClientNotificationMethods", "clientToServer", "notification"),
            [$"{TypeScriptRoot}/server-requests.generated.ts"] = EmitTypeScriptMethodMap(ir, "ServerRequestMethods", "serverToClient", "request"),
            [$"{TypeScriptRoot}/server-notifications.generated.ts"] = EmitTypeScriptMethodMap(ir, "ServerNotificationMethods", "serverToClient", "notification"),
            [$"{TypeScriptRoot}/method-groups.generated.ts"] = EmitTypeScriptMethodGroups(ir, hash),
            [$"{TypeScriptRoot}/index.ts"] = EmitTypeScriptIndex(),
            ["sdk/python/dotcraft/_generated/__init__.py"] = GeneratedPythonHeader(),
            [$"{PythonRoot}/__init__.py"] = EmitPythonIndex(),
            [$"{PythonRoot}/models_generated.py"] = GeneratePythonModels(contractFiles["schemas/appserver.schema.json"]),
            [$"{PythonRoot}/notification_registry_generated.py"] = EmitPythonRegistries(ir),
            [$"{PythonRoot}/client_methods_generated.py"] = EmitPythonClientMixin(ir),
            [$"{PythonRoot}/method_groups_generated.py"] = EmitPythonMethodGroups(ir),
            [$"{PythonRoot}/protocol_info_generated.py"] = EmitPythonProtocolInfo(ir, hash)
        };
        return files;
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
        export * from "./client-requests.generated.js";
        export * from "./client-notifications.generated.js";
        export * from "./server-requests.generated.js";
        export * from "./server-notifications.generated.js";
        export * from "./method-groups.generated.js";
        """);

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
            "SERVER_NOTIFICATION_METHODS",
            "SERVER_NOTIFICATION_MODELS",
            "SERVER_REQUEST_METHODS",
            "SERVER_REQUEST_MODELS",
            "parse_server_notification",
            "parse_server_request",
        ]
        """);

    private static string GeneratePythonModels(string schema)
    {
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "dotcraft-protocolgen-python", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);
        try
        {
            var input = Path.Combine(temporaryRoot, "appserver.schema.json");
            var output = Path.Combine(temporaryRoot, "models_generated.py");
            File.WriteAllText(input, schema, new UTF8Encoding(false));

            var version = RunPython(["-c", "import importlib.metadata; print(importlib.metadata.version('datamodel-code-generator'))"]);
            if (!string.Equals(version.Trim(), ModelGeneratorVersion, StringComparison.Ordinal))
                throw new ProtocolGenerationException("DPG030", $"datamodel-code-generator {ModelGeneratorVersion} is required; found '{version.Trim()}'.");

            RunPython([
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

    private static string RunPython(IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("DOTCRAFT_PYTHON") ?? "python",
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
