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
    public async Task HeadlessServer_AdvertisesLiveUpdatesAllowsDesktopCorsAndServesNoRendererRoutes()
    {
        await using var app = new TestOratorioApp();
        var client = app.CreateClient();

        using (var document = JsonDocument.Parse(await client.GetStringAsync("/api/v1/status")))
        {
            var capabilities = document.RootElement.GetProperty("capabilities");
            Assert.True(capabilities.GetProperty("webSocketUpdates").GetBoolean());
            Assert.True(capabilities.GetProperty("pollingLiveUpdates").GetBoolean());
        }

        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/status");
        request.Headers.Add("Origin", "http://127.0.0.1:5173");
        request.Headers.Add("Access-Control-Request-Method", "GET");
        var preflight = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, preflight.StatusCode);
        Assert.Equal("http://127.0.0.1:5173", preflight.Headers.GetValues("Access-Control-Allow-Origin").Single());

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/projects/default")).StatusCode);
    }

    [Fact]
    public async Task CreateLocalTasks_ProjectsMonotonicShortIdsAndResolvesByShortId()
    {
        await using var app = new TestOratorioApp();
        var client = app.CreateClient();

        var first = await CreateLocalTaskAsync(client, "First local task");
        var second = await CreateLocalTaskAsync(client, "Second local task");

        Assert.Equal("DEF-1", first.Item.ShortId);
        Assert.Equal("DEF-2", second.Item.ShortId);
        Assert.Equal(0, first.Item.BoardSortOrder);
        Assert.Equal(1, second.Item.BoardSortOrder);

        var json = await client.GetStringAsync("/api/v1/tasks");
        using (var document = JsonDocument.Parse(json))
        {
            var task = document.RootElement.GetProperty("tasks").EnumerateArray().First();
            Assert.Equal(first.Item.ItemId, task.GetProperty("itemId").GetString());
            Assert.Equal("DEF-1", task.GetProperty("shortId").GetString());
            Assert.Equal("todo", task.GetProperty("taskStatus").GetString());
            Assert.Equal(0, task.GetProperty("boardSortOrder").GetInt64());
        }

        var typed = JsonSerializer.Deserialize<TaskListResponse>(json, JsonOptions)
            ?? throw new InvalidOperationException("Expected task list response.");
        Assert.All(typed.Tasks, x => Assert.Equal(BoardTaskStatus.Todo, x.TaskStatus));

        var byShortId = await client.GetFromJsonAsync<ItemDetailResponse>($"/api/v1/tasks/{second.Item.ShortId}", JsonOptions);
        Assert.Equal(second.Item.ItemId, byShortId!.Item.ItemId);
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
