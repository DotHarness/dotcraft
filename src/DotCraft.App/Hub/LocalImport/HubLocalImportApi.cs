using DotCraft.Protocol.AppServer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DotCraft.Hub;

internal static class HubLocalImportApi
{
    public static void Map(
        IEndpointRouteBuilder app,
        HubLocalImport import,
        HubLocalImportConnection connection,
        string chatPath,
        Func<HttpRequest, IResult?> unauthorized,
        Func<Func<Task<IResult>>, Task<IResult>> protectedAsync)
    {
        var routes = app.MapGroup("/v1/local-import");
        routes.MapGet("/detect", async (HttpRequest request, CancellationToken ct) =>
            unauthorized(request) ?? await protectedAsync(async () =>
                Results.Json(new { sources = await import.DetectAsync(ct) }, HubJson.Options)));
        routes.MapGet("/state", async (HttpRequest request, CancellationToken ct) =>
            unauthorized(request) ?? await protectedAsync(async () =>
                Results.Json(await connection.AddLiveAttentionAsync(import.GetState(), chatPath, ct), HubJson.Options)));
        routes.MapPost("/run", async (HttpRequest request, HubLocalImportRequest body) =>
            unauthorized(request) ?? await protectedAsync(() =>
                Task.FromResult(Results.Json(new { importId = import.Run(body) }, HubJson.Options))));
        routes.MapPost("/settings", async (HttpRequest request, ImportSettingsSetParams body) =>
            unauthorized(request) ?? await protectedAsync(() =>
                Task.FromResult(Results.Json(new { settings = import.SetSettings(body) }, HubJson.Options))));
    }
}

internal sealed record HubLocalImportRequest(
    string Source, IReadOnlyList<HubLocalImportReference> Items, ImportSelection Selection, ImportSelection Offered);

internal sealed record HubLocalImportReference(string Key, string Fingerprint);

internal sealed record HubLocalImportState(
    bool Enabled, ImportSettings Settings, ImportProgressNotification? Running,
    IReadOnlyList<HubLocalImportBatch> Imports, IReadOnlyList<HubLocalImportOutcome> Attention);

internal sealed record HubLocalImportBatch(
    string ImportId, string Trigger, DateTimeOffset StartedAt, DateTimeOffset CompletedAt,
    IReadOnlyList<HubLocalImportOutcome> Outcomes);

internal sealed record HubLocalImportOutcome(
    string Category, string Scope, string TargetPath, string Source, string SourceId, string Status,
    string? ThreadId, string? Title, string? ErrorCode, string? Error,
    string? ProjectPath, string? ProjectName)
{
    public static HubLocalImportOutcome AtDestination(ImportOutcome outcome, string? projectPath, string? projectName) => new(
        outcome.Category, outcome.Scope, outcome.TargetPath, outcome.Source, outcome.SourceId, outcome.Status,
        outcome.ThreadId.IsSet ? outcome.ThreadId.Value : null,
        outcome.Title.IsSet ? outcome.Title.Value : null,
        outcome.ErrorCode.IsSet ? outcome.ErrorCode.Value : null,
        outcome.Error.IsSet ? outcome.Error.Value : null,
        outcome.Scope == "user" ? null : projectPath,
        outcome.Scope == "user" ? null : projectName);
}
