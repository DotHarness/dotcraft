using System.Text.RegularExpressions;
using DotCraft.Automations.Abstractions;
using Fluid;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DotCraft.Automations.Local;

/// <summary>
/// Loads <c>workflow.md</c> for local tasks (YAML front matter + Liquid body).
/// </summary>
public sealed partial class LocalWorkflowLoader(ILogger<LocalWorkflowLoader> logger)
{
    private static readonly Regex FrontMatterRegex = GetFrontMatterRegex();
    private readonly FluidParser _fluidParser = new();

    /// <summary>
    /// Loads workflow.md for the task and renders Liquid templates.
    /// </summary>
    public Task<AutomationWorkflowDefinition> LoadAsync(LocalAutomationTask task, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!File.Exists(task.WorkflowFilePath))
            throw new FileNotFoundException($"workflow.md not found: {task.WorkflowFilePath}");

        var content = File.ReadAllText(task.WorkflowFilePath);
        var match = FrontMatterRegex.Match(content);
        if (!match.Success)
        {
            return Task.FromResult(new AutomationWorkflowDefinition
            {
                Steps = [new WorkflowStep { Prompt = RenderTemplate(content.Trim(), task) }],
                MaxRounds = 10,
                WorkspaceMode = AutomationWorkspaceMode.Project
            });
        }

        var yamlText = match.Groups[1].Value;
        var body = match.Groups[2].Value.Trim();

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var fm = deserializer.Deserialize<WorkflowYamlFrontMatter>(yamlText) ?? new WorkflowYamlFrontMatter();

        IReadOnlyList<WorkflowStep> steps;
        if (fm.Steps is { Count: > 0 } list)
        {
            steps = list
                .Select(s => new WorkflowStep { Prompt = RenderTemplate(s, task) })
                .ToList();
        }
        else
        {
            steps = [new WorkflowStep { Prompt = RenderTemplate(body, task) }];
        }

        return Task.FromResult(new AutomationWorkflowDefinition
        {
            Steps = steps,
            MaxRounds = fm.MaxRounds > 0 ? fm.MaxRounds : 10,
            WorkspaceMode = MapWorkspaceString(fm.Workspace)
        });
    }

    /// <summary>
    /// Reads only <c>workflow.md</c> front matter to determine <see cref="AutomationWorkspaceMode"/>
    /// before the orchestrator sets <see cref="LocalAutomationTask.AgentWorkspacePath"/>.
    /// </summary>
    public Task<AutomationWorkspaceMode> GetWorkspaceModeAsync(LocalAutomationTask task, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!File.Exists(task.WorkflowFilePath))
            return Task.FromResult(AutomationWorkspaceMode.Project);

        var content = File.ReadAllText(task.WorkflowFilePath);
        var match = FrontMatterRegex.Match(content);
        if (!match.Success)
            return Task.FromResult(AutomationWorkspaceMode.Project);

        var yamlText = match.Groups[1].Value;
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var fm = deserializer.Deserialize<WorkflowYamlFrontMatter>(yamlText) ?? new WorkflowYamlFrontMatter();
        return Task.FromResult(MapWorkspaceString(fm.Workspace));
    }

    private static AutomationWorkspaceMode MapWorkspaceString(string? workspace)
    {
        if (string.Equals(workspace?.Trim(), "isolated", StringComparison.OrdinalIgnoreCase))
            return AutomationWorkspaceMode.Isolated;
        return AutomationWorkspaceMode.Project;
    }

    /// <summary>
    /// Watches workflow.md for changes and invokes <paramref name="onReload"/> with a new definition.
    /// </summary>
    public IDisposable Watch(LocalAutomationTask task, Action<AutomationWorkflowDefinition> onReload)
    {
        var path = task.WorkflowFilePath;
        var dir = Path.GetDirectoryName(path);
        var file = Path.GetFileName(path);
        if (dir == null || file == null)
            return new EmptyDisposable();

        var watcher = new FileSystemWatcher(dir, file)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };

        watcher.Changed += (_, _) =>
        {
            Task.Delay(100).ContinueWith(_ =>
            {
                try
                {
                    var def = LoadAsync(task, CancellationToken.None).GetAwaiter().GetResult();
                    onReload(def);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "workflow.md reload failed for {Path}", path);
                }
            });
        };

        return watcher;
    }

    private string RenderTemplate(string template, LocalAutomationTask task)
    {
        if (string.IsNullOrWhiteSpace(template))
            return "You are working on a local automation task.";

        if (!_fluidParser.TryParse(template, out var fluidTemplate, out var error))
        {
            logger.LogWarning("Liquid parse error: {Error}", error);
            return template;
        }

        var options = new TemplateOptions();
        options.MemberAccessStrategy.Register<Dictionary<string, object?>>();

        var workspace = task.AgentWorkspacePath ?? Path.Combine(task.TaskDirectory, "workspace");
        var taskModel = new Dictionary<string, object?>
        {
            ["id"] = task.Id,
            ["title"] = task.Title,
            ["description"] = task.Description ?? string.Empty,
            ["workspace_path"] = workspace
        };

        var workItem = new Dictionary<string, object?>
        {
            ["id"] = task.Id,
            ["title"] = task.Title
        };

        var context = new TemplateContext(options);
        context.SetValue("task", taskModel);
        context.SetValue("work_item", workItem);

        return fluidTemplate.Render(context);
    }

    private sealed class WorkflowYamlFrontMatter
    {
        public int MaxRounds { get; set; } = 10;
        public List<string>? Steps { get; set; }

        /// <summary><c>project</c> (default) or <c>isolated</c>.</summary>
        public string? Workspace { get; set; }
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public void Dispose() { }
    }

    [GeneratedRegex(@"^---\s*\r?\n(.*?)---\s*\r?\n(.*)$", RegexOptions.Compiled | RegexOptions.Singleline)]
    private static partial Regex GetFrontMatterRegex();
}
