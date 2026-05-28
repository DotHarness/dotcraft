using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DotCraft.Configuration;
using DotCraft.Localization;
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
    /// 从嵌入资源读取模板内容
    /// </summary>
    private static string GetTemplateContent(
        string templateName,
        Language language,
        WorkspaceBootstrapProfile profile = WorkspaceBootstrapProfile.Default)
    {
        var langSuffix = language == Language.Chinese ? "zh" : "en";
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
        Language language,
        WorkspaceBootstrapProfile profile,
        List<(string Status, string Path)>? createdItems = null)
    {
        var agentsPath = Path.Combine(craftPath, "AGENTS.md");
        File.WriteAllText(agentsPath, GetTemplateContent("AGENTS", language, profile), Encoding.UTF8);
        createdItems?.Add(("[green]✓[/]", "AGENTS.md"));

        var userPath = Path.Combine(craftPath, "USER.md");
        File.WriteAllText(userPath, GetTemplateContent("USER", language, profile), Encoding.UTF8);
        createdItems?.Add(("[green]✓[/]", "USER.md"));

        var memoryPath = Path.Combine(craftPath, "memory", "MEMORY.md");
        File.WriteAllText(memoryPath, GetTemplateContent("MEMORY", language), Encoding.UTF8);
        createdItems?.Add(("[green]✓[/]", "memory/MEMORY.md"));

        var gitignorePath = Path.Combine(craftPath, ".gitignore");
        File.WriteAllText(gitignorePath, GetTemplateContent("gitignore", language), Encoding.UTF8);
        createdItems?.Add(("[green]✓[/]", ".gitignore"));
    }

    private static string? ReadTrimmedString(JsonObject node, string key)
    {
        var matched = node.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
        return matched.Value?.GetValue<string>()?.Trim();
    }

    private static void RemoveCoreConfigFields(JsonObject node)
    {
        node.Remove("Language");
        node.Remove("ApiKey");
        node.Remove("EndPoint");
        node.Remove("Model");
    }

    private static void RemoveProviderAwareWorkspaceFields(JsonObject node)
    {
        RemoveCoreConfigFields(node);
        node.Remove("ProviderId");
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

    private static void ApplyLanguageWorkspaceOverride(JsonObject workspaceNode, JsonObject globalNode, Language language)
    {
        var languageText = language.ToString();
        if (!string.Equals(ReadTrimmedString(globalNode, "Language"), languageText, StringComparison.Ordinal))
            workspaceNode["Language"] = languageText;
        else
            workspaceNode.Remove("Language");
    }

    private static void ApplyProviderAwareWorkspaceSelection(
        JsonObject workspaceNode,
        JsonObject globalNode,
        string providerId,
        string model,
        Language language)
    {
        RemoveProviderAwareWorkspaceFields(workspaceNode);
        ApplyLanguageWorkspaceOverride(workspaceNode, globalNode, language);

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

        var trimmedModel = model.Trim();
        if (!string.IsNullOrWhiteSpace(trimmedModel)
            && !string.Equals(ReadTrimmedString(globalNode, "Model"), trimmedModel, StringComparison.Ordinal))
        {
            workspaceNode["Model"] = trimmedModel;
        }
        else
        {
            workspaceNode.Remove("Model");
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
            ApplyLanguageWorkspaceOverride(workspaceNode, globalNode, request.Language);
            SaveJsonObject(workspaceConfigPath, workspaceNode);
            WriteWorkspaceTemplates(craftPath, request.Language, request.Profile);
            return 0;
        }

        string providerId;
        var setAsUserDefault = request.SetAsUserDefault;
        if (request.ProviderMode == WorkspaceSetupProviderMode.Legacy)
        {
            var protocol = ModelProviderProtocols.OpenAI;
            var legacyProvider = NormalizeProviderDraft(new WorkspaceSetupProviderDraft
            {
                Id = string.IsNullOrWhiteSpace(request.ProviderId) ? "openai" : request.ProviderId,
                DisplayName = "OpenAI",
                Protocol = protocol,
                ApiKey = request.ApiKey,
                EndPoint = request.EndPoint
            });
            SaveProviderDraft(globalNode, legacyProvider);
            providerId = legacyProvider.Id;
            setAsUserDefault = request.SaveToUserConfig || request.SetAsUserDefault;
        }
        else if (request.ProviderMode == WorkspaceSetupProviderMode.Create)
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

        var model = request.Model.Trim();
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Missing --model.");

        if (setAsUserDefault)
        {
            globalNode["Language"] = request.Language.ToString();
            globalNode["ProviderId"] = providerId;
            globalNode["Model"] = model;
        }

        SaveJsonObject(globalConfigPath, globalNode);
        ApplyProviderAwareWorkspaceSelection(workspaceNode, globalNode, providerId, model, request.Language);
        SaveJsonObject(workspaceConfigPath, workspaceNode);
        WriteWorkspaceTemplates(craftPath, request.Language, request.Profile);
        return 0;
    }

    /// <summary>
    /// 选择语言
    /// </summary>
    public static Language SelectLanguage()
    {
        Console.WriteLine();
        var welcomePanel = new Panel(
            new Markup(
                "[cyan]Welcome to DotCraft![/]\n\n" +
                "[grey]请选择语言 / Please select language:[/]"))
        {
            Header = new PanelHeader("[cyan]🌐 Language Selection[/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Cyan),
            Padding = new Padding(1, 0, 1, 0)
        };
        AnsiConsole.Write(welcomePanel);
        Console.WriteLine();

        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .AddChoices("中文 (Chinese)", "English"));

        return choice == "中文 (Chinese)" ? Language.Chinese : Language.English;
    }

    /// <summary>
    /// 询问用户是否确认，使用 Spectre.Console 选项（多语言支持）
    /// </summary>
    public static bool AskYesNo(string title)
    {
        var yesOption = Strings.InitAskYes;
        var noOption = Strings.InitAskNo;

        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title(title)
                .AddChoices(yesOption, noOption));

        return choice == yesOption;
    }

    /// <summary>
    /// 初始化工作区（多语言支持）
    /// </summary>
    public static int InitializeWorkspace(
        string craftPath,
        Language language = Language.Chinese,
        WorkspaceBootstrapProfile profile = WorkspaceBootstrapProfile.Default)
    {
        if (LanguageService.Current.CurrentLanguage != language)
        {
            LanguageService.Current = new LanguageService(language);
        }

        AnsiConsole.MarkupLine($"[blue]🚀 {Strings.InitInitializing}[/]");

        var createdItems = new List<(string Status, string Path)>();

        try
        {
            EnsureWorkspaceStructure(craftPath, createdItems);

            var workspaceNode = new JsonObject
            {
                ["Language"] = language.ToString()
            };

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

            WriteWorkspaceTemplates(craftPath, language, profile, createdItems);

            var table = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Grey)
                .AddColumn(new TableColumn(Strings.InitStatus).Centered())
                .AddColumn(new TableColumn(Strings.InitPath).LeftAligned());

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
            AnsiConsole.MarkupLine($"[red]✗ {Strings.InitFailedShort}: {ex.Message.EscapeMarkup()}[/]");
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
    public static void CreateGlobalConfig(string configPath, string apiKey, Language language)
    {
        var configNode = new JsonObject();
        var providers = GetOrCreateObject(configNode, "Providers");
        providers["openai"] = new JsonObject
        {
            ["DisplayName"] = "OpenAI",
            ["Protocol"] = ModelProviderProtocols.OpenAI,
            ["ApiKey"] = apiKey
        };
        configNode["Language"] = language.ToString();
        configNode["ProviderId"] = "openai";
        configNode["Model"] = "gpt-4o-mini";
        SaveJsonObject(configPath, configNode);
    }
}
