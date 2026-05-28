using System.Text;
using System.Text.RegularExpressions;
using DotCraft.Automations.Local;
using DotCraft.Cron;
using DotCraft.Hosting;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DotCraft.Automations.Templates;

/// <summary>
/// Persists user-authored automation templates as <c>template.md</c> files.
/// Layout: <c>{CraftPath}/automations/templates/&lt;id&gt;/template.md</c>, YAML front matter + body
/// (where the body is the full <c>workflow.md</c> markdown that will be copied into new tasks).
/// </summary>
public sealed partial class UserTemplateFileStore(
    AutomationsConfig config,
    DotCraftPaths paths,
    ILogger<UserTemplateFileStore> logger)
{
    private static readonly Regex FrontMatterRegex = GetFrontMatterRegex();
    private static readonly Regex IdRegex = GetIdRegex();

    /// <summary>Resolved absolute path to the user-templates root directory.</summary>
    public string TemplatesRoot { get; } = string.IsNullOrWhiteSpace(config.UserTemplatesRoot)
        ? Path.Combine(paths.CraftPath, "automations", "templates")
        : Path.GetFullPath(config.UserTemplatesRoot);

    /// <summary>Loads all user templates from disk. Malformed files are skipped with a warning.</summary>
    public Task<IReadOnlyList<LocalTaskTemplate>> LoadAllAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(TemplatesRoot);
        var result = new List<LocalTaskTemplate>();

        foreach (var dir in Directory.EnumerateDirectories(TemplatesRoot))
        {
            var file = Path.Combine(dir, "template.md");
            if (!File.Exists(file))
                continue;

            try
            {
                var tpl = LoadFromFile(dir);
                if (tpl != null)
                    result.Add(tpl);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to load user template from {Dir}", dir);
            }
        }

        return Task.FromResult<IReadOnlyList<LocalTaskTemplate>>(result);
    }

    /// <summary>
    /// Persists (creates or overwrites) a user template. Returns the canonical in-memory record.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when <paramref name="id"/> is invalid.</exception>
    public Task<LocalTaskTemplate> SaveAsync(
        string id,
        string title,
        string? description,
        string? icon,
        string? category,
        string workflowMarkdown,
        CronSchedule? defaultSchedule,
        string? defaultWorkspaceMode,
        string? defaultApprovalPolicy,
        bool needsThreadBinding,
        string? defaultTitle,
        string? defaultDescription,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!IsValidId(id))
            throw new ArgumentException($"Invalid template id '{id}'.", nameof(id));
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Template title must not be empty.", nameof(title));

        // Built-in id collisions must be rejected at a higher layer; defense in depth here.
        if (LocalTaskTemplates.FindById(id) != null)
            throw new ArgumentException($"Template id '{id}' is reserved by a built-in template.", nameof(id));

        Directory.CreateDirectory(TemplatesRoot);
        var dir = Path.Combine(TemplatesRoot, id);
        var fullDir = Path.GetFullPath(dir);
        var fullRoot = Path.GetFullPath(TemplatesRoot);
        if (!fullDir.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Template directory escapes the templates root.", nameof(id));

        Directory.CreateDirectory(fullDir);
        var file = Path.Combine(fullDir, "template.md");

        DateTimeOffset? createdAt = null;
        if (File.Exists(file))
        {
            try
            {
                var existing = LoadFromFile(fullDir);
                createdAt = existing?.CreatedAt;
            }
            catch
            {
                createdAt = null;
            }
        }

        var now = DateTimeOffset.UtcNow;
        createdAt ??= now;

        var fm = new TemplateFileFrontMatter
        {
            Id = id,
            Title = title.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description,
            Icon = string.IsNullOrWhiteSpace(icon) ? null : icon,
            Category = string.IsNullOrWhiteSpace(category) ? null : category,
            DefaultSchedule = ToYaml(defaultSchedule),
            DefaultWorkspaceMode = string.IsNullOrWhiteSpace(defaultWorkspaceMode) ? null : defaultWorkspaceMode,
            DefaultApprovalPolicy = string.IsNullOrWhiteSpace(defaultApprovalPolicy) ? null : defaultApprovalPolicy,
            NeedsThreadBinding = needsThreadBinding,
            DefaultTitle = string.IsNullOrWhiteSpace(defaultTitle) ? null : defaultTitle,
            DefaultDescription = string.IsNullOrWhiteSpace(defaultDescription) ? null : defaultDescription,
            CreatedAt = createdAt,
            UpdatedAt = now
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
        sb.Append(workflowMarkdown ?? string.Empty);

        File.WriteAllText(file, sb.ToString());

        return Task.FromResult(new LocalTaskTemplate(
            Id: id,
            Title: fm.Title ?? id,
            Description: fm.Description ?? string.Empty,
            Icon: fm.Icon ?? string.Empty,
            Category: fm.Category ?? string.Empty,
            WorkflowMarkdown: workflowMarkdown ?? string.Empty,
            DefaultSchedule: defaultSchedule,
            DefaultWorkspaceMode: fm.DefaultWorkspaceMode,
            DefaultApprovalPolicy: fm.DefaultApprovalPolicy,
            NeedsThreadBinding: needsThreadBinding,
            DefaultTitle: fm.DefaultTitle,
            DefaultDescription: fm.DefaultDescription,
            IsUser: true,
            CreatedAt: createdAt,
            UpdatedAt: now));
    }

    /// <summary>Deletes a user template directory. Idempotent when the directory is already gone.</summary>
    public Task DeleteAsync(string id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!IsValidId(id))
            throw new ArgumentException($"Invalid template id '{id}'.", nameof(id));

        var dir = Path.Combine(TemplatesRoot, id);
        var fullDir = Path.GetFullPath(dir);
        var fullRoot = Path.GetFullPath(TemplatesRoot);
        if (!fullDir.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Template directory escapes the templates root.", nameof(id));

        if (Directory.Exists(fullDir))
        {
            try
            {
                Directory.Delete(fullDir, recursive: true);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete user template directory {Dir}", fullDir);
                throw;
            }
        }
        return Task.CompletedTask;
    }

    /// <summary>Validates template id shape. Used before touching the file system.</summary>
    public static bool IsValidId(string id) =>
        !string.IsNullOrWhiteSpace(id) && IdRegex.IsMatch(id);

    private LocalTaskTemplate? LoadFromFile(string dir)
    {
        var file = Path.Combine(dir, "template.md");
        if (!File.Exists(file))
            return null;
        var content = File.ReadAllText(file);
        var match = FrontMatterRegex.Match(content);
        if (!match.Success)
        {
            logger.LogWarning("template.md missing YAML front matter: {File}", file);
            return null;
        }

        var yamlText = match.Groups[1].Value;
        var body = match.Groups[2].Value.TrimEnd();

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .WithTypeConverter(new DateTimeOffsetIsoYamlConverter())
            .Build();

        var fm = deserializer.Deserialize<TemplateFileFrontMatter>(yamlText);
        var id = fm.Id ?? Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (!IsValidId(id))
        {
            logger.LogWarning("Skipping user template with invalid id '{Id}' at {Dir}", id, dir);
            return null;
        }

        return new LocalTaskTemplate(
            Id: id,
            Title: fm.Title ?? id,
            Description: fm.Description ?? string.Empty,
            Icon: fm.Icon ?? string.Empty,
            Category: fm.Category ?? string.Empty,
            WorkflowMarkdown: body,
            DefaultSchedule: FromYaml(fm.DefaultSchedule),
            DefaultWorkspaceMode: fm.DefaultWorkspaceMode,
            DefaultApprovalPolicy: fm.DefaultApprovalPolicy,
            NeedsThreadBinding: fm.NeedsThreadBinding ?? false,
            DefaultTitle: fm.DefaultTitle,
            DefaultDescription: fm.DefaultDescription,
            IsUser: true,
            CreatedAt: fm.CreatedAt,
            UpdatedAt: fm.UpdatedAt);
    }

    private static CronSchedule? FromYaml(ScheduleYaml? y)
    {
        if (y == null || string.IsNullOrWhiteSpace(y.Kind))
            return null;
        var kind = y.Kind.Trim().ToLowerInvariant();
        if (kind == "once")
            return null;
        return new CronSchedule
        {
            Kind = kind,
            AtMs = y.AtMs,
            EveryMs = y.EveryMs,
            InitialDelayMs = y.InitialDelayMs,
            DailyHour = y.DailyHour ?? y.Hour,
            DailyMinute = y.DailyMinute ?? y.Minute,
            Expr = y.Expr,
            Tz = y.Tz
        };
    }

    private static ScheduleYaml? ToYaml(CronSchedule? s)
    {
        if (s == null || string.IsNullOrWhiteSpace(s.Kind))
            return null;
        return new ScheduleYaml
        {
            Kind = s.Kind,
            AtMs = s.AtMs,
            EveryMs = s.EveryMs,
            InitialDelayMs = s.InitialDelayMs,
            DailyHour = s.DailyHour,
            DailyMinute = s.DailyMinute,
            Expr = s.Expr,
            Tz = s.Tz
        };
    }

    private sealed class TemplateFileFrontMatter
    {
        public string? Id { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? Icon { get; set; }
        public string? Category { get; set; }
        public ScheduleYaml? DefaultSchedule { get; set; }
        public string? DefaultWorkspaceMode { get; set; }
        public string? DefaultApprovalPolicy { get; set; }
        public bool? NeedsThreadBinding { get; set; }
        public string? DefaultTitle { get; set; }
        public string? DefaultDescription { get; set; }
        public DateTimeOffset? CreatedAt { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }
    }

    private sealed class ScheduleYaml
    {
        public string? Kind { get; set; }
        public long? AtMs { get; set; }
        public long? EveryMs { get; set; }
        public long? InitialDelayMs { get; set; }
        public int? DailyHour { get; set; }
        public int? DailyMinute { get; set; }
        public int? Hour { get; set; }
        public int? Minute { get; set; }
        public string? Expr { get; set; }
        public string? Tz { get; set; }
    }

    [GeneratedRegex(@"^---\s*\r?\n(.*?)---\s*\r?\n(.*)$", RegexOptions.Compiled | RegexOptions.Singleline)]
    private static partial Regex GetFrontMatterRegex();

    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9_\-]{0,63}$", RegexOptions.Compiled)]
    private static partial Regex GetIdRegex();
}
