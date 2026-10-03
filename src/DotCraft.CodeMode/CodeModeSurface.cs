using System.Text;
using DotCraft.Tools;
using Microsoft.Extensions.Logging;

namespace DotCraft.CodeMode;

public sealed record CodeModeNestedTool(string JsName, ToolRegistration Registration, bool Listed, string Declaration);

public sealed class CodeModeSurface
{
    public static readonly ToolName CodeModeToolName = new(null, "CodeMode");

    private readonly Dictionary<string, CodeModeNestedTool> _byJsName;

    private CodeModeSurface(
        IReadOnlyList<CodeModeNestedTool> tools,
        IReadOnlyDictionary<string, string> aliases,
        IReadOnlyDictionary<string, string> namespaceDescriptions)
    {
        Tools = tools;
        Aliases = aliases;
        NamespaceDescriptions = namespaceDescriptions;
        _byJsName = tools.ToDictionary(static tool => tool.JsName, StringComparer.Ordinal);
    }

    public IReadOnlyList<CodeModeNestedTool> Tools { get; }

    public IReadOnlyDictionary<string, string> Aliases { get; }

    public IReadOnlyDictionary<string, string> NamespaceDescriptions { get; }

    public bool HasUnlistedTools => Tools.Any(static tool => !tool.Listed);

    public bool TryResolve(string name, out CodeModeNestedTool tool)
    {
        if (Aliases.TryGetValue(name, out var target))
            name = target;
        return _byJsName.TryGetValue(name, out tool!);
    }

    public static CodeModeSurface Build(EffectiveToolSnapshot snapshot, ILogger? logger = null)
    {
        var direct = snapshot.ModelVisibleDefinitions.Select(definition => (definition, listed: true));
        var deferred = snapshot.DeferredDefinitions.Values.SelectMany(static group => group)
            .Select(definition => (definition, listed: false));
        var candidates = direct.Concat(deferred)
            .Select(entry => (registration: snapshot.Registrations[entry.definition.Name], entry.listed))
            .Where(entry => entry.registration.Exposure is ToolExposure.Direct or ToolExposure.Deferred
                            && entry.registration.Definition.Name != CodeModeToolName
                            && !EffectiveToolSnapshot.IsDeferredToolSearch(entry.registration)
                            && entry.registration.InvocationAudiences.HasFlag(ToolInvocationAudience.Model))
            .OrderBy(entry => entry.registration.Definition.Name.Namespace, StringComparer.Ordinal)
            .ThenBy(entry => entry.registration.Definition.Name.Name, StringComparer.Ordinal);

        var tools = new List<CodeModeNestedTool>();
        var taken = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (registration, listed) in candidates)
        {
            var jsName = ToJsName(registration.Definition.Name);
            if (!taken.Add(jsName))
            {
                logger?.LogWarning(
                    "Code mode omitted tool {ToolName} because its JavaScript name {JsName} is already taken.",
                    registration.Definition.Name,
                    jsName);
                continue;
            }
            tools.Add(new CodeModeNestedTool(
                jsName,
                registration,
                listed,
                CodeModeDeclarations.Declaration(jsName, registration.Definition)));
        }

        var aliases = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var tool in tools)
        {
            if (snapshot.ProviderFlatNames.TryGetValue(tool.Registration.Definition.Name, out var flatName)
                && !taken.Contains(flatName))
            {
                aliases.TryAdd(flatName, tool.JsName);
            }
        }

        return new CodeModeSurface(tools, aliases, snapshot.NamespaceDescriptions);
    }

    public static string ToJsName(ToolName name)
    {
        var raw = name.Namespace is null ? name.Name : $"{name.Namespace}__{name.Name}";
        var builder = new StringBuilder(raw.Length);
        foreach (var character in raw)
        {
            builder.Append(character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '$'
                ? character
                : '_');
        }
        return builder.ToString();
    }
}
