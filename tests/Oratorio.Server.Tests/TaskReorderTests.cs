using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Oratorio.Server.Api;
using BoardTaskStatus = Oratorio.Server.Domain.TaskStatus;

namespace Oratorio.Server.Tests;

public sealed class TaskReorderTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [Fact]
    public async Task ReorderTasks_ChangesDefaultTaskListOrder()
    {
        await using var app = new TestOratorioApp();
        var client = app.CreateClient();
        var first = await CreateLocalTaskAsync(client, "First card");
        var second = await CreateLocalTaskAsync(client, "Second card");
        var third = await CreateLocalTaskAsync(client, "Third card");

        await PostAsync<TaskReorderResponse>(
            client,
            "/api/v1/tasks/reorder",
            new TaskReorderRequest(
            [
                new TaskReorderEntry(third.Item.ItemId, 0),
                new TaskReorderEntry(first.Item.ItemId, 1),
                new TaskReorderEntry(second.Item.ItemId, 2)
            ]));

        var listed = await client.GetFromJsonAsync<TaskListResponse>("/api/v1/tasks", JsonOptions)
            ?? throw new InvalidOperationException("Expected task list response.");

        Assert.Equal(
            new[] { third.Item.ItemId, first.Item.ItemId, second.Item.ItemId },
            listed.Tasks.Select(x => x.ItemId).ToArray());
    }

    [Fact]
    public async Task ReorderTasks_CanMoveSortOrderAcrossColumns_WithoutChangingState()
    {
        await using var app = new TestOratorioApp();
        var client = app.CreateClient();
        var task = await CreateLocalTaskAsync(client, "Move sort bucket only");

        var response = await PostAsync<TaskReorderResponse>(
            client,
            "/api/v1/tasks/reorder",
            new TaskReorderRequest([new TaskReorderEntry(task.Item.ShortId!, 2000)]));

        var updated = Assert.Single(response.Tasks);
        Assert.Equal(task.Item.ItemId, updated.ItemId);
        Assert.Equal(2000, updated.BoardSortOrder);
        Assert.Equal(BoardTaskStatus.Todo, updated.TaskStatus);
    }

    [Fact]
    public async Task ReorderTasks_RollsBackWhenAnyTaskIdIsUnknown()
    {
        await using var app = new TestOratorioApp();
        var client = app.CreateClient();
        var first = await CreateLocalTaskAsync(client, "Stable first");
        var second = await CreateLocalTaskAsync(client, "Stable second");

        var response = await client.PostAsJsonAsync(
            "/api/v1/tasks/reorder",
            new TaskReorderRequest(
            [
                new TaskReorderEntry(first.Item.ItemId, 99),
                new TaskReorderEntry("missing-task", 0)
            ]),
            JsonOptions);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var listed = await client.GetFromJsonAsync<TaskListResponse>("/api/v1/tasks", JsonOptions)
            ?? throw new InvalidOperationException("Expected task list response.");

        Assert.Equal(
            new[] { first.Item.ItemId, second.Item.ItemId },
            listed.Tasks.Select(x => x.ItemId).ToArray());
        Assert.Equal(new long[] { 0, 1 }, listed.Tasks.Select(x => x.BoardSortOrder).ToArray());
    }

    private static Task<ItemDetailResponse> CreateLocalTaskAsync(HttpClient client, string title) =>
        PostAsync<ItemDetailResponse>(
            client,
            "/api/v1/local-tasks",
            new CreateLocalTaskRequest(
                title,
                "Reorder test task.",
                "example-owner/oratorio",
                "operator",
                "local/m2",
                ["m2", "board"]));

    private static async Task<T> PostAsync<T>(HttpClient client, string path, object request)
    {
        var response = await client.PostAsJsonAsync(path, request, JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions)
            ?? throw new InvalidOperationException($"Expected {typeof(T).Name} response.");
    }
}
