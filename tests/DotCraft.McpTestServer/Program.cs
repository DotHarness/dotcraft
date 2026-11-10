using System.Net;
using System.Text.Json.Nodes;
using DotCraft.McpTestServer;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

if (args.Contains("stateful"))
{
    await StatefulMcpFixture.RunAsync();
    return;
}

var legacy = args.Contains("legacy");
if (args.Contains("http"))
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Logging.ClearProviders();
    builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
    var app = builder.Build();
    var legacyResponse = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
    app.MapPost("/", async context =>
    {
        var request = (await JsonNode.ParseAsync(context.Request.Body))!.AsObject();
        if (legacy && request["id"]?.ToString() == "legacy-input")
        {
            legacyResponse.TrySetResult(request);
            context.Response.StatusCode = 202;
            return;
        }
        if (legacy && request["method"]?.GetValue<string>() == "tools/call")
        {
            context.Response.ContentType = "text/event-stream";
            var input = new JsonObject
            {
                ["jsonrpc"] = "2.0", ["id"] = "legacy-input", ["method"] = "elicitation/create",
                ["params"] = McpFixtureProtocol.Input("legacy")
            };
            await context.Response.WriteAsync($"event: message\ndata: {input.ToJsonString()}\n\n");
            await context.Response.Body.FlushAsync(context.RequestAborted);
            var answer = await legacyResponse.Task.WaitAsync(context.RequestAborted);
            var completed = McpFixtureProtocol.Result(request, new JsonObject
            {
                ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = answer.ToJsonString() })
            });
            await context.Response.WriteAsync($"event: message\ndata: {completed.ToJsonString()}\n\n");
            return;
        }
        var response = McpFixtureProtocol.Respond(request, legacy);
        if (response is null)
        {
            context.Response.StatusCode = 202;
            return;
        }
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(response.ToJsonString());
    });
    await app.StartAsync();
    Console.WriteLine(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
    await app.WaitForShutdownAsync();
    return;
}

JsonObject? pending = null;
while (await Console.In.ReadLineAsync() is { } line)
{
    var request = JsonNode.Parse(line)!.AsObject();
    JsonObject? response;
    if (request["method"]?.GetValue<string>() == "tools/call" && legacy)
    {
        pending = request;
        response = new JsonObject
        {
            ["jsonrpc"] = "2.0", ["id"] = "legacy-input", ["method"] = "elicitation/create",
            ["params"] = McpFixtureProtocol.Input("legacy")
        };
    }
    else if (pending is not null && request["id"]?.ToString() == "legacy-input")
    {
        response = McpFixtureProtocol.Result(pending, new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = request.ToJsonString() })
        });
        pending = null;
    }
    else if (request["params"]?["name"]?.GetValue<string>() == "disconnect")
    {
        return;
    }
    else
    {
        response = McpFixtureProtocol.Respond(request, legacy);
    }
    if (response is not null)
        await Console.Out.WriteLineAsync(response.ToJsonString());
}
