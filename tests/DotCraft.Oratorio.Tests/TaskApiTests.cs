using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using DotCraft.Oratorio.Api;
using DotCraft.Oratorio.Data;
using DotCraft.Oratorio.Domain;
using BoardTaskStatus = DotCraft.Oratorio.Domain.TaskStatus;

namespace DotCraft.Oratorio.Tests;

public sealed class TaskApiTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [Fact]
    public async Task Status_AdvertisesWebSocketUpdatesWithPollingFallback()
    {
        await using var app = new TestOratorioApp();
        var client = app.CreateClient();

        var json = await client.GetStringAsync("/api/v1/status");

        using var document = JsonDocument.Parse(json);
        var capabilities = document.RootElement.GetProperty("capabilities");
        Assert.True(capabilities.GetProperty("webSocketUpdates").GetBoolean());
        Assert.True(capabilities.GetProperty("pollingLiveUpdates").GetBoolean());
    }

    [Fact]
    public async Task HeadlessServer_DoesNotServeDesktopRendererRoutes()
    {
        await using var app = new TestOratorioApp();
        var client = app.CreateClient();

        var response = await client.GetAsync("/projects/default");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Cors_AllowsDesktopRendererOrigins()
    {
        await using var app = new TestOratorioApp();
        var client = app.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/status");
        request.Headers.Add("Origin", "http://127.0.0.1:5173");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("http://127.0.0.1:5173", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Fact]
    public async Task GetTasks_ReturnsTaskProjectionFields()
    {
        await using var app = new TestOratorioApp();
        var client = app.CreateClient();

        var created = await CreateLocalTaskAsync(client, "Write task board spec");
        var response = await client.GetAsync("/api/v1/tasks");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();

        using var document = JsonDocument.Parse(json);
        var task = document.RootElement.GetProperty("tasks").EnumerateArray().Single();
        Assert.Equal(created.Item.ItemId, task.GetProperty("itemId").GetString());
        Assert.Equal(created.Item.ShortId, task.GetProperty("shortId").GetString());
        Assert.Equal("todo", task.GetProperty("taskStatus").GetString());
        Assert.Equal(0, task.GetProperty("boardSortOrder").GetInt64());

        var typed = JsonSerializer.Deserialize<TaskListResponse>(json, JsonOptions)
            ?? throw new InvalidOperationException("Expected task list response.");
        var dto = Assert.Single(typed.Tasks);
        Assert.Equal(BoardTaskStatus.Todo, dto.TaskStatus);
        Assert.StartsWith("DEF-", dto.ShortId, StringComparison.Ordinal);
        Assert.Equal(0, dto.BoardSortOrder);
    }

    [Fact]
    public async Task CreateLocalTasks_AssignsMonotonicShortIds()
    {
        await using var app = new TestOratorioApp();
        var client = app.CreateClient();

        var first = await CreateLocalTaskAsync(client, "First local task");
        var second = await CreateLocalTaskAsync(client, "Second local task");

        Assert.Equal("DEF-1", first.Item.ShortId);
        Assert.Equal("DEF-2", second.Item.ShortId);
        Assert.Equal(BoardTaskStatus.Todo, first.Item.TaskStatus);
        Assert.Equal(BoardTaskStatus.Todo, second.Item.TaskStatus);
        Assert.Equal(0, first.Item.BoardSortOrder);
        Assert.Equal(1, second.Item.BoardSortOrder);
    }

    [Fact]
    public async Task GetTasks_DefaultsToBoardSortOrder()
    {
        await using var app = new TestOratorioApp();
        var client = app.CreateClient();

        var first = await CreateLocalTaskAsync(client, "First local task");
        var second = await CreateLocalTaskAsync(client, "Second local task");
        var third = await CreateLocalTaskAsync(client, "Third local task");

        await PostAsync<TaskReorderResponse>(
            client,
            "/api/v1/tasks/reorder",
            new TaskReorderRequest(
            [
                new TaskReorderEntry(third.Item.ItemId, 0),
                new TaskReorderEntry(second.Item.ItemId, 1),
                new TaskReorderEntry(first.Item.ItemId, 2)
            ]));

        var listed = await client.GetFromJsonAsync<TaskListResponse>("/api/v1/tasks", JsonOptions)
            ?? throw new InvalidOperationException("Expected task list response.");

        Assert.Equal(
            new[] { third.Item.ItemId, second.Item.ItemId, first.Item.ItemId },
            listed.Tasks.Select(x => x.ItemId).ToArray());
    }

    [Fact]
    public async Task GetTasks_PaginatesWithNextCursor()
    {
        await using var app = new TestOratorioApp();
        var client = app.CreateClient();

        var first = await CreateLocalTaskAsync(client, "First local task");
        var second = await CreateLocalTaskAsync(client, "Second local task");
        var third = await CreateLocalTaskAsync(client, "Third local task");

        var firstPage = await client.GetFromJsonAsync<TaskListResponse>("/api/v1/tasks?limit=2", JsonOptions)
            ?? throw new InvalidOperationException("Expected first page.");
        Assert.NotNull(firstPage.NextCursor);
        Assert.Equal(
            new[] { first.Item.ItemId, second.Item.ItemId },
            firstPage.Tasks.Select(x => x.ItemId).ToArray());

        var secondPage = await client.GetFromJsonAsync<TaskListResponse>($"/api/v1/tasks?limit=2&cursor={Uri.EscapeDataString(firstPage.NextCursor!)}", JsonOptions)
            ?? throw new InvalidOperationException("Expected second page.");
        Assert.Null(secondPage.NextCursor);
        var item = Assert.Single(secondPage.Tasks);
        Assert.Equal(third.Item.ItemId, item.ItemId);
    }

    [Fact]
    public async Task GetTasks_StateFiltersClosedBuckets()
    {
        await using var app = new TestOratorioApp();
        var client = app.CreateClient();

        var active = await CreateLocalTaskAsync(client, "Active local task");
        var rejected = await CreateLocalTaskAsync(client, "Rejected local task");
        var archived = await CreateLocalTaskAsync(client, "Archived local task");
        await SetItemStateAsync(app, rejected.Item.ItemId, ItemState.Rejected);
        await SetItemStateAsync(app, archived.Item.ItemId, ItemState.Archived);

        var defaultList = await client.GetFromJsonAsync<TaskListResponse>("/api/v1/tasks", JsonOptions)
            ?? throw new InvalidOperationException("Expected default list.");
        Assert.Contains(defaultList.Tasks, x => x.ItemId == active.Item.ItemId);
        Assert.Contains(defaultList.Tasks, x => x.ItemId == rejected.Item.ItemId);
        Assert.DoesNotContain(defaultList.Tasks, x => x.ItemId == archived.Item.ItemId);

        var rejectedList = await client.GetFromJsonAsync<TaskListResponse>("/api/v1/tasks?state=rejected", JsonOptions)
            ?? throw new InvalidOperationException("Expected rejected list.");
        var rejectedItem = Assert.Single(rejectedList.Tasks);
        Assert.Equal(rejected.Item.ItemId, rejectedItem.ItemId);

        var archivedList = await client.GetFromJsonAsync<TaskListResponse>("/api/v1/tasks?state=archived", JsonOptions)
            ?? throw new InvalidOperationException("Expected archived list.");
        var archivedItem = Assert.Single(archivedList.Tasks);
        Assert.Equal(archived.Item.ItemId, archivedItem.ItemId);
    }

    [Fact]
    public async Task GetTaskByShortId_ReturnsSameDetailAsItemIdRoute()
    {
        await using var app = new TestOratorioApp();
        var client = app.CreateClient();

        var created = await CreateLocalTaskAsync(client, "Open drawer by short id");
        var byShortId = await client.GetFromJsonAsync<ItemDetailResponse>($"/api/v1/tasks/{created.Item.ShortId}", JsonOptions);
        var byItemId = await client.GetFromJsonAsync<ItemDetailResponse>($"/api/v1/items/id/{created.Item.ItemId}", JsonOptions);

        Assert.NotNull(byShortId);
        Assert.NotNull(byItemId);
        Assert.Equal(byItemId!.Item.ItemId, byShortId!.Item.ItemId);
        Assert.Equal(byItemId.Item.ShortId, byShortId.Item.ShortId);
    }

    private static Task<ItemDetailResponse> CreateLocalTaskAsync(HttpClient client, string title) =>
        PostAsync<ItemDetailResponse>(
            client,
            "/api/v1/local-tasks",
            new CreateLocalTaskRequest(
                title,
                "Document the M1 task projection.",
                "example-owner/oratorio",
                "operator",
                "local/m1",
                ["m1", "task"]));

    private static async Task SetItemStateAsync(TestOratorioApp app, string itemId, ItemState state)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var item = await db.Items.SingleAsync(x => x.ItemId == itemId);
        item.State = state;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string path, object request)
    {
        var response = await client.PostAsJsonAsync(path, request, JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions)
            ?? throw new InvalidOperationException($"Expected {typeof(T).Name} response.");
    }
}
