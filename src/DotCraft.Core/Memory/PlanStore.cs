using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using DotCraft.State;

namespace DotCraft.Memory;

/// <summary>
/// Persists per-thread plans in the workspace state database.
/// </summary>
public sealed class PlanStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly StateRuntime _stateRuntime;

    public PlanStore(string botPath)
        : this(botPath, null)
    {
    }

    internal PlanStore(string botPath, StateRuntime? stateRuntime)
    {
        _stateRuntime = stateRuntime ?? new StateRuntime(botPath);
    }

    // ── Structured plan (JSON + rendered MD) ──

    public async Task SaveStructuredPlanAsync(string sessionId, StructuredPlan plan)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return;

        var json = JsonSerializer.Serialize(plan, JsonOptions);
        var rendered = RenderPlanMarkdown(plan);
        await SavePlanRowAsync(
            sessionId,
            json,
            rendered,
            plan.CreatedAt == default ? DateTimeOffset.UtcNow : plan.CreatedAt,
            plan.UpdatedAt == default ? DateTimeOffset.UtcNow : plan.UpdatedAt);
    }

    public Task<StructuredPlan?> LoadStructuredPlanAsync(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return Task.FromResult<StructuredPlan?>(null);

        using var connection = _stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT plan_json FROM thread_plans WHERE thread_id = $thread_id LIMIT 1";
        command.Parameters.AddWithValue("$thread_id", sessionId);
        var json = command.ExecuteScalar() as string;
        if (string.IsNullOrWhiteSpace(json))
            return Task.FromResult<StructuredPlan?>(null);

        try
        {
            return Task.FromResult(JsonSerializer.Deserialize<StructuredPlan>(json, ReadOptions));
        }
        catch
        {
            return Task.FromResult<StructuredPlan?>(null);
        }
    }

    public bool StructuredPlanExists(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return false;

        using var connection = _stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT 1
            FROM thread_plans
            WHERE thread_id = $thread_id AND plan_json IS NOT NULL AND TRIM(plan_json) <> ''
            LIMIT 1
            """;
        command.Parameters.AddWithValue("$thread_id", sessionId);
        return command.ExecuteScalar() != null;
    }

    /// <summary>
    /// Renders a <see cref="StructuredPlan"/> as human-readable Markdown.
    /// </summary>
    public static string RenderPlanMarkdown(StructuredPlan plan)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {plan.Title}");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(plan.Overview))
        {
            sb.AppendLine($"> {plan.Overview}");
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(plan.Content))
        {
            sb.AppendLine(plan.Content);
            sb.AppendLine();
        }

        if (plan.Todos.Count > 0)
        {
            sb.AppendLine("## Tasks");
            sb.AppendLine();
            foreach (var todo in plan.Todos)
            {
                var icon = todo.Status switch
                {
                    PlanTodoStatus.Completed  => "✓",
                    PlanTodoStatus.InProgress => "●",
                    PlanTodoStatus.Cancelled  => "✗",
                    _                         => "○"
                };
                var statusTag = todo.Status switch
                {
                    PlanTodoStatus.InProgress => " **(in progress)**",
                    PlanTodoStatus.Cancelled  => " *(cancelled)*",
                    _                         => ""
                };
                sb.AppendLine($"- {icon} `{todo.Id}` — {todo.Content}{statusTag}");
            }
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd() + "\n";
    }

    /// <summary>
    /// Renders a <see cref="StructuredPlan"/> as plain text (no Markdown) suitable for
    /// channels that do not support rich formatting (e.g. QQ).
    /// </summary>
    public static string RenderPlanPlainText(StructuredPlan plan)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[{plan.Title}]");

        if (plan.Todos.Count == 0)
        {
            sb.AppendLine("  (no tasks)");
            return sb.ToString().TrimEnd() + "\n";
        }

        foreach (var todo in plan.Todos)
        {
            var icon = todo.Status switch
            {
                PlanTodoStatus.Completed  => "✓",
                PlanTodoStatus.InProgress => "●",
                PlanTodoStatus.Cancelled  => "✗",
                _                         => "○"
            };
            var suffix = todo.Status switch
            {
                PlanTodoStatus.InProgress => " (working)",
                PlanTodoStatus.Cancelled  => " (skipped)",
                _                         => ""
            };
            sb.AppendLine($"  {icon} {todo.Id} — {todo.Content}{suffix}");
        }

        return sb.ToString().TrimEnd() + "\n";
    }

    private Task SavePlanRowAsync(
        string threadId,
        string planJson,
        string renderedMarkdown,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        using var connection = _stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO thread_plans(thread_id, plan_json, rendered_markdown, created_at, updated_at)
            VALUES ($thread_id, $plan_json, $rendered_markdown, $created_at, $updated_at)
            ON CONFLICT(thread_id) DO UPDATE SET
                plan_json = excluded.plan_json,
                rendered_markdown = excluded.rendered_markdown,
                updated_at = excluded.updated_at
            """;
        command.Parameters.AddWithValue("$thread_id", threadId);
        command.Parameters.AddWithValue("$plan_json", planJson);
        command.Parameters.AddWithValue("$rendered_markdown", renderedMarkdown);
        command.Parameters.AddWithValue("$created_at", createdAt.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$updated_at", updatedAt.UtcDateTime.ToString("O"));
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }
}
