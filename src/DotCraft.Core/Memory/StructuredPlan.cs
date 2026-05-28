using System.Text.Json.Serialization;

namespace DotCraft.Memory;

public sealed class StructuredPlan
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("overview")]
    public string Overview { get; set; } = "";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";

    [JsonPropertyName("todos")]
    public List<PlanTodo> Todos { get; set; } = [];

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class PlanTodo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";

    [JsonPropertyName("priority")]
    public string Priority { get; set; } = PlanTodoPriority.Medium;

    [JsonPropertyName("status")]
    public string Status { get; set; } = PlanTodoStatus.Pending;
}

public static class PlanTodoPriority
{
    public const string High = "high";
    public const string Medium = "medium";
    public const string Low = "low";
}

public static class PlanTodoStatus
{
    public const string Pending = "pending";
    public const string InProgress = "in_progress";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
}
