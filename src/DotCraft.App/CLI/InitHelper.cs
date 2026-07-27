using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DotCraft.Configuration;
using DotCraft.Text;
using Spectre.Console;

namespace DotCraft.CLI;

/// <summary>
/// 初始化辅助工具类
/// </summary>
public static class InitHelper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Reads a workspace bootstrap template from embedded resources.
    /// </summary>
    private static string GetTemplateContent(
        string templateName,
        WorkspaceBootstrapProfile profile = WorkspaceBootstrapProfile.Default)
    {
        const string langSuffix = "en";
        var extension = templateName == "gitignore" ? string.Empty : ".md";
        string resourceName;

        if (profile == WorkspaceBootstrapProfile.Default)
        {
            resourceName = $"DotCraft.Resources.Templates.{templateName}_{langSuffix}{extension}";
        }
        else
        {
            var profileSuffix = profile switch
            {
                WorkspaceBootstrapProfile.Developer => "developer",
                WorkspaceBootstrapProfile.PersonalAssistant => "personal_assistant",
                _ => "default"
            };
            resourceName = $"DotCraft.Resources.Templates.{templateName}_{profileSuffix}_{langSuffix}{extension}";
        }

        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName);

        if (stream == null)
        {
            throw new InvalidOperationException($"Template resource not found: {resourceName}");
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static JsonObject LoadJsonObject(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        return JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8)) as JsonObject ?? [];
    }

    private static void SaveJsonObject(string path, JsonObject node)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, node.ToJsonString(JsonOptions), Encoding.UTF8);
    }

    private static void EnsureWorkspaceStructure(string craftPath, List<(string Status, string Path)>? createdItems = null)
    {
        var directories = new[]
        {
            craftPath,
            Path.Combine(craftPath, "memory"),
            Path.Combine(craftPath, "skills"),
            Path.Combine(craftPath, "security")
        };

        foreach (var dir in directories)
        {
            Directory.CreateDirectory(dir);
            createdItems?.Add(("[green]✓[/]", dir.EscapeMarkup()));
        }
    }

    private static void WriteWorkspaceTemplates(
        string craftPath,
        WorkspaceBootstrapProfile profile,
        List<(string Status, string Path)>? createdItems = null)
    {
        if (profile != WorkspaceBootstrapProfile.Default)
        {
            var agentsPath = Path.Combine(craftPath, "AGENTS.md");
            File.WriteAllText(agentsPath, GetTemplateContent("AGENTS", profile), Encoding.UTF8);
            createdItems?.Add(("[green]✓[/]", "AGENTS.md"));
        }

        var memoryPath = Path.Combine(craftPath, "memory", "MEMORY.md");
        File.WriteAllText(memoryPath, GetTemplateContent("MEMORY"), Encoding.UTF8);
        createdItems?.Add(("[green]✓[/]", "memory/MEMORY.md"));

        var gitignorePath = Path.Combine(craftPath, ".gitignore");
        File.WriteAllText(gitignorePath, GetTemplateContent("gitignore"), Encoding.UTF8);
        createdItems?.Add(("[green]✓[/]", ".gitignore"));
    }

    private static string? ReadTrimmedString(JsonObject node, string key)
    {
        var matched = node.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
        return matched.Value?.GetValue<string>()?.Trim();
    }

    private static void RemoveCaseInsensitive(JsonObject node, string key)
    {
        var matched = node.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(matched.Key))
            node.Remove(matched.Key);
    }

    private static void RemoveProviderAwareWorkspaceFields(JsonObject node)
    {
        node.Remove("ProviderId");
        node.Remove("ProviderPreferences");
    }

    private static JsonObject GetOrCreateObject(JsonObject node, string key)
    {
        if (node[key] is JsonObject existing)
            return existing;

        var created = new JsonObject();
        node[key] = created;
        return created;
    }

    private static JsonObject? GetProviderRegistry(JsonObject globalNode)
    {
        var matched = globalNode.FirstOrDefault(p => string.Equals(p.Key, "Providers", StringComparison.OrdinalIgnoreCase));
        return matched.Value as JsonObject;
    }

    private static bool ProviderExists(JsonObject globalNode, string providerId)
    {
        var providers = GetProviderRegistry(globalNode);
        return providers?.Any(p => string.Equals(p.Key, providerId, StringComparison.OrdinalIgnoreCase)) == true;
    }

    private static string NormalizeProviderId(string providerId)
    {
        var trimmed = providerId.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new ArgumentException("Provider id is required.");
        return trimmed;
    }

    private static string NormalizeProviderProtocol(string protocol)
    {
        try
        {
            return ModelProviderProtocols.Normalize(protocol);
        }
        catch (ArgumentException)
        {
            throw new ArgumentException(
                $"Unsupported provider protocol '{protocol}'. Expected openai-chat-completions, openai-responses, or anthropic.");
        }
    }

    private static void ValidateProviderEndpoint(string protocol, string? endPoint)
    {
        if (string.IsNullOrWhiteSpace(endPoint))
        {
            return;
        }

        if (!Uri.TryCreate(endPoint.Trim(), UriKind.Absolute, out _))
            throw new ArgumentException("Provider endpoint must be an absolute URI.");
    }

    private static WorkspaceSetupProviderDraft NormalizeProviderDraft(WorkspaceSetupProviderDraft draft)
    {
        var id = NormalizeProviderId(draft.Id);
        var protocol = NormalizeProviderProtocol(draft.Protocol);
        ValidateProviderEndpoint(protocol, draft.EndPoint);
        if (draft.NetworkTimeoutSeconds is <= 0)
            throw new ArgumentException("Provider network timeout must be greater than zero.");

        return draft with
        {
            Id = id,
            Protocol = protocol,
            DisplayName = string.IsNullOrWhiteSpace(draft.DisplayName) ? id : draft.DisplayName.Trim(),
            ApiKey = draft.ApiKey.Trim(),
            EndPoint = draft.EndPoint.Trim()
        };
    }

    private static void SaveProviderDraft(JsonObject globalNode, WorkspaceSetupProviderDraft draft)
    {
        var providers = GetOrCreateObject(globalNode, "Providers");
        var providerNode = new JsonObject
        {
            ["DisplayName"] = draft.DisplayName,
            ["Protocol"] = draft.Protocol,
            ["ApiKey"] = draft.ApiKey
        };
        if (!string.IsNullOrWhiteSpace(draft.EndPoint))
            providerNode["EndPoint"] = draft.EndPoint;
        if (draft.NetworkTimeoutSeconds is int timeout)
            providerNode["NetworkTimeoutSeconds"] = timeout;
        if (!string.IsNullOrWhiteSpace(draft.AuthMethod) &&
            !string.Equals(draft.AuthMethod, "apiKey", StringComparison.OrdinalIgnoreCase))
        {
            providerNode["AuthMethod"] = draft.AuthMethod;
            // ChatGPT OAuth uses Responses API on the chatgpt.com backend.
            providerNode["Protocol"] = "openai-responses";
        }

        providers[draft.Id] = providerNode;
    }

    private static void ApplyProviderAwareWorkspaceSelection(
        JsonObject workspaceNode,
        JsonObject globalNode,
        string providerId,
        ModelPreference preference)
    {
        RemoveProviderAwareWorkspaceFields(workspaceNode);

        var trimmedProviderId = providerId.Trim();
        if (!string.IsNullOrWhiteSpace(trimmedProviderId)
            && !string.Equals(ReadTrimmedString(globalNode, "ProviderId"), trimmedProviderId, StringComparison.Ordinal))
        {
            workspaceNode["ProviderId"] = trimmedProviderId;
        }
        else
        {
            workspaceNode.Remove("ProviderId");
        }

        var globalPreferences = globalNode["ProviderPreferences"] as JsonObject;
        var inheritedPreference = globalPreferences?
            .FirstOrDefault(pair => string.Equals(
                pair.Key,
                trimmedProviderId,
                StringComparison.OrdinalIgnoreCase)).Value;
        var preferenceNode = JsonSerializer.SerializeToNode(preference, JsonOptions);
        if (!JsonNode.DeepEquals(inheritedPreference, preferenceNode))
        {
            GetOrCreateObject(workspaceNode, "ProviderPreferences")[trimmedProviderId] = preferenceNode;
        }
    }

    private static int RunProviderAwareSetup(
        string craftPath,
        WorkspaceSetupRequest request,
        string globalConfigPath)
    {
        var globalNode = LoadJsonObject(globalConfigPath);
        var workspaceConfigPath = Path.Combine(craftPath, "config.json");
        var workspaceNode = LoadJsonObject(workspaceConfigPath);

        if (request.ProviderMode == WorkspaceSetupProviderMode.Skip)
        {
            RemoveProviderAwareWorkspaceFields(workspaceNode);
            SaveJsonObject(workspaceConfigPath, workspaceNode);
            WriteWorkspaceTemplates(craftPath, request.Profile);
            return 0;
        }

        string providerId;
        var setAsUserDefault = request.SetAsUserDefault;
        if (request.ProviderMode == WorkspaceSetupProviderMode.Create)
        {
            var provider = request.Provider ?? throw new ArgumentException("Provider draft is required.");
            var normalizedProvider = NormalizeProviderDraft(provider);
            SaveProviderDraft(globalNode, normalizedProvider);
            providerId = normalizedProvider.Id;
        }
        else if (request.ProviderMode == WorkspaceSetupProviderMode.Existing)
        {
            providerId = NormalizeProviderId(request.ProviderId);
            if (!ProviderExists(globalNode, providerId))
                throw new ArgumentException($"Provider '{providerId}' is not configured.");
        }
        else
        {
            throw new ArgumentException($"Unsupported provider setup mode '{request.ProviderMode}'.");
        }

        var preference = request.Preference == null
            ? ModelPreferenceRules.CreateManual(request.Model)
            : ModelPreferenceRules.Clone(request.Preference);
        if (string.IsNullOrWhiteSpace(preference.Model))
            throw new ArgumentException("Missing model preference.");
        preference.Model = preference.Model.Trim();

        if (setAsUserDefault)
        {
            globalNode["ProviderId"] = providerId;
            GetOrCreateObject(globalNode, "ProviderPreferences")[providerId] =
                JsonSerializer.SerializeToNode(preference, JsonOptions);
        }

        SaveJsonObject(globalConfigPath, globalNode);
        ApplyProviderAwareWorkspaceSelection(workspaceNode, globalNode, providerId, preference);
        SaveJsonObject(workspaceConfigPath, workspaceNode);
        WriteWorkspaceTemplates(craftPath, request.Profile);
        return 0;
    }

    /// <summary>
    /// 询问用户是否确认，使用 Spectre.Console 选项。
    /// </summary>
    public static bool AskYesNo(string title)
    {
        var yesOption = FallbackText.InitAskYes;
        var noOption = FallbackText.InitAskNo;

        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title(title)
                .AddChoices(yesOption, noOption));

        return choice == yesOption;
    }

    /// <summary>
    /// 初始化工作区
    /// </summary>
    public static int InitializeWorkspace(
        string craftPath,
        WorkspaceBootstrapProfile profile = WorkspaceBootstrapProfile.Default)
    {
        AnsiConsole.MarkupLine($"[blue]🚀 {FallbackText.InitInitializing}[/]");

        var createdItems = new List<(string Status, string Path)>();

        try
        {
            EnsureWorkspaceStructure(craftPath, createdItems);

            var workspaceNode = new JsonObject();

            var globalConfigPath = GetGlobalConfigPath();
            if (File.Exists(globalConfigPath))
            {
                var globalNode = LoadJsonObject(globalConfigPath);
                foreach (var prop in globalNode.ToList())
                {
                    workspaceNode.Remove(prop.Key);
                }
            }

            var configPath = Path.Combine(craftPath, "config.json");
            SaveJsonObject(configPath, workspaceNode);
            createdItems.Add(("[green]✓[/]", configPath.EscapeMarkup()));

            WriteWorkspaceTemplates(craftPath, profile, createdItems);

            var table = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Grey)
                .AddColumn(new TableColumn(FallbackText.InitStatus).Centered())
                .AddColumn(new TableColumn(FallbackText.InitPath).LeftAligned());

            foreach (var item in createdItems)
            {
                table.AddRow(item.Status, item.Path);
            }

            AnsiConsole.Write(table);
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[red]✗ {FallbackText.InitFailedShort}: {ex.Message.EscapeMarkup()}[/]");
            return 1;
        }
    }

    public static int RunSetup(string craftPath, WorkspaceSetupRequest request)
    {
        return RunSetup(craftPath, request, GetGlobalConfigPath());
    }

    internal static int RunSetup(string craftPath, WorkspaceSetupRequest request, string globalConfigPath)
    {
        EnsureWorkspaceStructure(craftPath);

        if (request.SaveToUserConfig && request.PreferExistingUserConfig)
            throw new InvalidOperationException("SaveToUserConfig and PreferExistingUserConfig cannot both be enabled.");

        return RunProviderAwareSetup(craftPath, request, globalConfigPath);
    }

    /// <summary>
    /// 获取全局配置文件路径
    /// </summary>
    public static string GetGlobalConfigPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".craft", "config.json");
    }

    /// <summary>
    /// 创建全局配置文件
    /// </summary>
    public static void CreateGlobalConfig(string configPath, string apiKey)
    {
        var configNode = new JsonObject();
        var providers = GetOrCreateObject(configNode, "Providers");
        providers["openai"] = new JsonObject
        {
            ["DisplayName"] = "OpenAI",
            ["Protocol"] = ModelProviderProtocols.OpenAI,
            ["ApiKey"] = apiKey
        };
        configNode["ProviderId"] = "openai";
        configNode["ProviderPreferences"] = new JsonObject
        {
            ["openai"] = JsonSerializer.SerializeToNode(
                ModelPreferenceRules.CreateManual("gpt-4o-mini"),
                JsonOptions)
        };
        SaveJsonObject(configPath, configNode);
    }
}
