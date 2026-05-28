using System.Text;
using System.Text.RegularExpressions;
using DotCraft.Automations.Abstractions;
using DotCraft.Cron;
using DotCraft.Hosting;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DotCraft.Automations.Local;

/// <summary>
/// Reads and writes local <c>task.md</c> files (YAML front matter + Markdown body).
/// </summary>
public sealed partial class LocalTaskFileStore(
    AutomationsConfig config,
    DotCraftPaths paths,
    ILogger<LocalTaskFileStore> logger)
{
    private static readonly Regex FrontMatterRegex = GetFrontMatterRegex();

    /// <summary>Resolved absolute path to the tasks root directory.</summary>
    public string TasksRoot { get; } = string.IsNullOrWhiteSpace(config.LocalTasksRoot)
        ? Path.Combine(paths.CraftPath, "tasks")
        : Path.GetFullPath(config.LocalTasksRoot);

    /// <inheritdoc cref="LoadAllAsync"/>
    public Task<IReadOnlyList<LocalAutomationTask>> LoadAllAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(TasksRoot);
        var result = new List<LocalAutomationTask>();

        foreach (var dir in Directory.EnumerateDirectories(TasksRoot))
        {
            var taskFile = Path.Combine(dir, "task.md");
            if (!File.Exists(taskFile))
                continue;

            try
            {
                result.Add(LoadFromFile(dir));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to load task from {Dir}", dir);
            }
        }

        return Task.FromResult<IReadOnlyList<LocalAutomationTask>>(result);
    }

    /// <inheritdoc cref="LoadAsync"/>
    public Task<LocalAutomationTask> LoadAsync(string taskDirectory, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(LoadFromFile(taskDirectory));
    }

    private LocalAutomationTask LoadFromFile(string taskDirectory)
    {
        var taskFile = Path.Combine(taskDirectory, "task.md");
        if (!File.Exists(taskFile))
            throw new FileNotFoundException($"task.md not found: {taskFile}");

        var content = File.ReadAllText(taskFile);
        return ParseTaskFile(Path.GetFullPath(taskDirectory), content);
    }

    internal LocalAutomationTask ParseTaskFile(string taskDirectory, string content)
    {
        var match = FrontMatterRegex.Match(content);
        if (!match.Success)
        {
            throw new InvalidOperationException(
                $"task.md must start with YAML front matter (--- ... ---): {taskDirectory}");
        }

        var yamlText = match.Groups[1].Value;
        var body = match.Groups[2].Value.TrimEnd();

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .WithTypeConverter(new DateTimeOffsetIsoYamlConverter())
            .Build();

        var fm = deserializer.Deserialize<TaskFileFrontMatter>(yamlText);
        var status = LocalTaskStatusMapping.FromYaml(fm.Status);

        var task = new LocalAutomationTask
        {
            TaskDirectory = taskDirectory,
            Id = fm.Id ?? Path.GetFileName(taskDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            Title = fm.Title ?? fm.Id ?? "Untitled",
            Status = status,
            ThreadId = fm.ThreadId,
            Description = body,
            AgentSummary = fm.AgentSummary,
            CreatedAt = fm.CreatedAt,
            UpdatedAt = fm.UpdatedAt,
            ApprovalPolicy = fm.ApprovalPolicy,
            Schedule = ParseSchedule(fm.Schedule),
            ThreadBinding = ParseBinding(fm.ThreadBinding)
        };
        task.NextRunAt = fm.NextRunAt;

        return task;
    }

    private static CronSchedule? ParseSchedule(ScheduleYaml? yaml)
    {
        if (yaml == null || string.IsNullOrWhiteSpace(yaml.Kind))
            return null;
        var kind = yaml.Kind.Trim().ToLowerInvariant();
        if (kind == "once")
            return null;
        return new CronSchedule
        {
            Kind = kind,
            AtMs = yaml.AtMs,
            EveryMs = yaml.EveryMs,
            InitialDelayMs = yaml.InitialDelayMs,
            DailyHour = yaml.DailyHour ?? yaml.Hour,
            DailyMinute = yaml.DailyMinute ?? yaml.Minute,
            Expr = yaml.Expr,
            Tz = yaml.Tz
        };
    }

    private static ScheduleYaml? ToYaml(CronSchedule? schedule)
    {
        if (schedule == null)
            return null;
        return new ScheduleYaml
        {
            Kind = schedule.Kind,
            AtMs = schedule.AtMs,
            EveryMs = schedule.EveryMs,
            InitialDelayMs = schedule.InitialDelayMs,
            DailyHour = schedule.DailyHour,
            DailyMinute = schedule.DailyMinute,
            Expr = schedule.Expr,
            Tz = schedule.Tz
        };
    }

    private static AutomationThreadBinding? ParseBinding(ThreadBindingYaml? yaml)
    {
        if (yaml == null || string.IsNullOrWhiteSpace(yaml.ThreadId))
            return null;
        return new AutomationThreadBinding
        {
            ThreadId = yaml.ThreadId!,
            Mode = string.IsNullOrWhiteSpace(yaml.Mode) ? "run-in-thread" : yaml.Mode!.Trim()
        };
    }

    private static ThreadBindingYaml? ToYaml(AutomationThreadBinding? binding)
    {
        if (binding == null || string.IsNullOrWhiteSpace(binding.ThreadId))
            return null;
        return new ThreadBindingYaml
        {
            ThreadId = binding.ThreadId,
            Mode = binding.Mode
        };
    }

    /// <summary>Persists status, thread id, and agent summary to task.md.</summary>
    public Task SaveAsync(LocalAutomationTask task, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var taskFile = task.TaskFilePath;
        Directory.CreateDirectory(task.TaskDirectory);

        string body;
        if (File.Exists(taskFile))
        {
            var content = File.ReadAllText(taskFile);
            var match = FrontMatterRegex.Match(content);
            body = match.Success ? match.Groups[2].Value.TrimEnd() : content.Trim();
        }
        else
        {
            body = task.Description ?? string.Empty;
        }

        task.UpdatedAt = DateTimeOffset.UtcNow;
        if (task.CreatedAt == null)
            task.CreatedAt = task.UpdatedAt;

        var fm = new TaskFileFrontMatter
        {
            Id = task.Id,
            Title = task.Title,
            Status = LocalTaskStatusMapping.ToYaml(task.Status),
            CreatedAt = task.CreatedAt,
            UpdatedAt = task.UpdatedAt,
            ThreadId = task.ThreadId,
            AgentSummary = task.AgentSummary,
            ApprovalPolicy = task.ApprovalPolicy,
            Schedule = ToYaml(task.Schedule),
            ThreadBinding = ToYaml(task.ThreadBinding),
            NextRunAt = task.NextRunAt
        };

        var serializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .WithTypeConverter(new DateTimeOffsetIsoYamlConverter())
            .Build();

        var yaml = serializer.Serialize(fm);
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.Append(yaml.TrimEnd());
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine(body);

        File.WriteAllText(taskFile, sb.ToString());
        return Task.CompletedTask;
    }

    /// <summary>Watches for new task directories and task.md files.</summary>
    public IDisposable WatchForNewTasks(Action<LocalAutomationTask> onNewTask)
    {
        Directory.CreateDirectory(TasksRoot);
        var watcher = new FileSystemWatcher(TasksRoot)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.CreationTime,
            Filter = "*.*",
            EnableRaisingEvents = true
        };

        void TryHandle(string fullPath)
        {
            try
            {
                if (!string.Equals(Path.GetFileName(fullPath), "task.md", StringComparison.OrdinalIgnoreCase))
                    return;

                var dir = Path.GetDirectoryName(fullPath);
                if (dir == null || !dir.StartsWith(TasksRoot, StringComparison.OrdinalIgnoreCase))
                    return;

                if (!File.Exists(fullPath))
                    return;

                var task = LoadFromFile(dir);
                onNewTask(task);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Watch callback skipped for {Path}", fullPath);
            }
        }

        watcher.Created += (_, e) => TryHandle(e.FullPath);
        watcher.Renamed += (_, e) => TryHandle(e.FullPath);

        return watcher;
    }

    private sealed class TaskFileFrontMatter
    {
        public string? Id { get; set; }
        public string? Title { get; set; }
        public string? Status { get; set; }
        public DateTimeOffset? CreatedAt { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }
        public string? ThreadId { get; set; }
        public string? AgentSummary { get; set; }

        /// <summary><c>workspaceScope</c> (default) or <c>fullAuto</c>.</summary>
        public string? ApprovalPolicy { get; set; }

        /// <summary>Optional recurring schedule; absent = one-shot (run once when pending).</summary>
        public ScheduleYaml? Schedule { get; set; }

        /// <summary>Optional binding to a pre-existing thread to submit workflow turns into.</summary>
        public ThreadBindingYaml? ThreadBinding { get; set; }

        /// <summary>
        /// Next scheduled run (UTC). Persisted so that orchestrator poll cycles do not drift the cadence
        /// by recomputing from scratch each time. Null means "no cadence decided yet" (e.g. a brand-new
        /// scheduled task) — in that case the orchestrator's initialization decides first-tick behavior.
        /// </summary>
        public DateTimeOffset? NextRunAt { get; set; }
    }

    /// <summary>
    /// YAML mirror of <see cref="DotCraft.Cron.CronSchedule"/> used for <c>task.md</c> front matter.
    /// Kind is one of <c>once</c> (sentinel, maps to null schedule) | <c>every</c> | <c>at</c> | <c>daily</c> | <c>weekly</c>.
    /// </summary>
    private sealed class ScheduleYaml
    {
        public string? Kind { get; set; }
        public long? AtMs { get; set; }
        public long? EveryMs { get; set; }
        public long? InitialDelayMs { get; set; }
        /// <summary>When kind=daily, local hour 0-23 (alias: hour).</summary>
        public int? DailyHour { get; set; }
        /// <summary>When kind=daily, local minute 0-59 (alias: minute).</summary>
        public int? DailyMinute { get; set; }
        public int? Hour { get; set; }
        public int? Minute { get; set; }
        public string? Expr { get; set; }
        public string? Tz { get; set; }
    }

    private sealed class ThreadBindingYaml
    {
        public string? ThreadId { get; set; }

        /// <summary><c>run-in-thread</c> (default).</summary>
        public string? Mode { get; set; }
    }

    [GeneratedRegex(@"^---\s*\r?\n(.*?)---\s*\r?\n(.*)$", RegexOptions.Compiled | RegexOptions.Singleline)]
    private static partial Regex GetFrontMatterRegex();
}
