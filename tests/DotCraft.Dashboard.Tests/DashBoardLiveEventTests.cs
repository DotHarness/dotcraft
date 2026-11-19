using System.Text.Json;
using DotCraft.DashBoard;
using DotCraft.Tracing;
using DotCraft.Workspaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DotCraft.Tests.DashBoard;

public sealed class DashBoardLiveEventTests
{
    [Fact]
    public async Task TwoDashboardStreamsEachReceiveTheSameLiveTrace()
    {
        var store = new TraceStore();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        await using var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:0");
        var workspace = Path.Combine(Path.GetTempPath(), $"dashboard-test-{Guid.NewGuid():N}");
        app.MapDashBoard(store, new DotCraftPaths(workspace, Path.Combine(workspace, ".craft"), userDataPath: null));
        await app.StartAsync();
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var firstConnection = http.GetAsync("/dashboard/api/events/stream",
            HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        var secondConnection = http.GetAsync("/dashboard/api/events/stream",
            HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        using var firstResponse = await firstConnection;
        using var secondResponse = await secondConnection;
        firstResponse.EnsureSuccessStatusCode();
        secondResponse.EnsureSuccessStatusCode();
        using var firstStream = new StreamReader(await firstResponse.Content.ReadAsStreamAsync(timeout.Token));
        using var secondStream = new StreamReader(await secondResponse.Content.ReadAsStreamAsync(timeout.Token));

        var trace = new TraceEvent { SessionKey = "session", Type = TraceEventType.Request, Content = "request" };
        store.Record(trace);

        await AssertTraceAsync(firstStream, trace.Id, timeout.Token);
        await AssertTraceAsync(secondStream, trace.Id, timeout.Token);
    }

    private static async Task AssertTraceAsync(StreamReader stream, string traceId, CancellationToken ct)
    {
        while (await stream.ReadLineAsync(ct) is { } line)
        {
            if (!line.StartsWith("data: ", StringComparison.Ordinal))
                continue;
            using var data = JsonDocument.Parse(line[6..]);
            Assert.Equal(traceId, data.RootElement.GetProperty("id").GetString());
            return;
        }

        Assert.Fail("The SSE connection closed before sending the trace.");
    }

}
