using System.Text.Json;
using DotCraft.Sdk.AppBinding;
using DotCraft.Sdk.AppServer;

// DotCraft App Binding sample shipping an MCP-Apps "Interactive Tool UI".
//
// It declares a tool (`sample.ShowCard`) whose `_meta.ui.resourceUri` points at a `ui://`
// resource, handles the tool call, and serves the resource HTML on `item/resource/read`.
// DotCraft Desktop renders that HTML in a sandboxed `dotcraft-app://` iframe (M-ii).
//
//   AUTO (recommended for testing): one command does connect + bind + attach + serve.
//     dotnet run -- <workspacePath> [threadId]
//   HANDOFF (the real external-app pattern): run per Desktop-issued handoff URL.
//     dotnet run -- --handoff "<handoff-url>"

const string AppId = "com.dotcraft.sample-ui";
const string ToolNamespace = "sample";
const string ToolName = "ShowCard";
const string ResourceUri = "ui://dotcraft-sample/card";
const string ClientName = "dotcraft-sample-ui";

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
var ct = cts.Token;

if (args.Length >= 2 && args[0] == "--handoff")
{
    return await RunHandoffModeAsync(args[1]);
}
if (args.Length >= 1 && args[0].Contains("://"))
{
    return await RunHandoffModeAsync(args[0]);
}
if (args.Length >= 1)
{
    return await RunAutoModeAsync(args[0], args.Length >= 2 ? args[1] : null);
}

Console.Error.WriteLine("Usage:");
Console.Error.WriteLine("  InteractiveToolSample <workspacePath> [threadId]    (auto: connect + bind + serve)");
Console.Error.WriteLine("  InteractiveToolSample --handoff \"<handoff-url>\"      (real app handoff pattern)");
return 1;

// ---------------------------------------------------------------------------
// Auto mode: one process plays both the Desktop side (start connection, create
// binding request, start thread) and the app side (complete connection, accept
// binding, attach tools, serve) over a single AppServer connection. No manual
// handoff URLs or Desktop clicks — just open the printed thread in Desktop.
// ---------------------------------------------------------------------------
async Task<int> RunAutoModeAsync(string workspacePath, string? existingThreadId)
{
    Console.WriteLine($"Auto mode — connecting to workspace AppServer ({workspacePath})…");
    await using var client = await DotCraftClient.ConnectLocalAsync(
        workspacePath,
        new DotCraftLocalClientOptions { ClientName = ClientName, ClientVersion = "0.1.0" },
        ct);

    // 1) Establish the app connection (account).
    var start = await client.AppBindings.StartConnectionAsync(AppId, cancellationToken: ct);
    await client.AppBindings.CompleteConnectionAsync(new CompleteConnectionRequest(
        ConnectionRequestId: start.ConnectionRequestId,
        RequestToken: TokenFromHandoff(start.Handoff),
        AppId: AppId,
        AccountLabel: "local-sample"), ct);
    Console.WriteLine("Connected app account.");

    // 2) Use the given thread, or start a fresh one.
    string threadId;
    if (!string.IsNullOrWhiteSpace(existingThreadId))
    {
        threadId = existingThreadId!;
    }
    else
    {
        var thread = await client.Threads.StartAsync(new DotCraftThreadStartRequest(
            new SessionIdentity(ChannelName: ClientName, UserId: Environment.UserName, WorkspacePath: workspacePath),
            DisplayName: "Interactive UI sample",
            HistoryMode: "server",
            Config: new { mode = "agent" }), ct);
        threadId = thread.Id;
    }

    // 3) Binding request → accept → attach tools (with _meta.ui).
    var requestCreated = await client.AppBindings.CreateBindingRequestAsync(threadId, AppId, ["card.read"], cancellationToken: ct);
    var grantId = "grant_" + Guid.NewGuid().ToString("N");
    var accepted = await client.AppBindings.AcceptBindingAsync(new AcceptBindingRequest(
        BindingRequestId: requestCreated.BindingRequestId,
        RequestToken: TokenFromHandoff(requestCreated.Handoff),
        GrantId: grantId,
        GrantedScopes: ["card.read"],
        ApprovalMode: "appAccepted",
        ApprovedBy: Environment.UserName), ct);
    await client.AppBindings.AttachToolsAsync(new AttachToolsRequest(
        BindingId: accepted.Binding.BindingId,
        ThreadId: threadId,
        AppId: AppId,
        GrantId: grantId,
        Tools: BuildTools()), ct);
    Console.WriteLine($"Bound + attached '{ToolNamespace}.{ToolName}' (binding {accepted.Binding.BindingId}).");

    RegisterHandlers(client, threadId);

    Console.WriteLine();
    Console.WriteLine("Ready. In DotCraft Desktop (same workspace), open this thread:");
    Console.WriteLine($"    {threadId}");
    Console.WriteLine("then ask the agent to use ShowCard (e.g. \"use the ShowCard tool with note hello\").");
    Console.WriteLine("Keep this process running. Press Ctrl+C to exit.");

    await KeepAliveAsync(client);
    return 0;
}

// ---------------------------------------------------------------------------
// Handoff mode: the production pattern — an external app launched per a Desktop
// handoff URL ("connect" then "bind").
// ---------------------------------------------------------------------------
async Task<int> RunHandoffModeAsync(string handoffUrl)
{
    var handoff = AppBindingHandoff.Parse(handoffUrl, expectedAppId: AppId);
    if (string.IsNullOrWhiteSpace(handoff.AppServerUrl))
    {
        Console.Error.WriteLine("Handoff URL is missing the AppServer endpoint.");
        return 1;
    }

    await using var client = await DotCraftClient.ConnectRemoteAsync(
        handoff.AppServerUrl,
        options: new DotCraftClientOptions { ClientName = ClientName, ClientVersion = "0.1.0" },
        cancellationToken: ct);
    Console.WriteLine($"Connected to AppServer (operation: {handoff.Operation}).");

    if (string.Equals(handoff.Operation, "connect", StringComparison.OrdinalIgnoreCase))
    {
        await client.AppBindings.CompleteConnectionAsync(new CompleteConnectionRequest(
            ConnectionRequestId: handoff.RequestId,
            RequestToken: handoff.RequestToken,
            AppId: AppId,
            AccountLabel: "local-sample"), ct);
        Console.WriteLine("Connection established. Now bind the app to a thread in Desktop and");
        Console.WriteLine("run again with the resulting 'bind' handoff URL.");
        return 0;
    }

    if (!string.Equals(handoff.Operation, "bind", StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine($"Unsupported handoff operation: {handoff.Operation} (expected 'connect' or 'bind').");
        return 1;
    }

    var request = await client.AppBindings.GetBindingRequestAsync<JsonElement>(new
    {
        appId = AppId,
        bindingRequestId = handoff.RequestId,
        requestToken = handoff.RequestToken
    }, ct);
    var threadId = request.GetProperty("threadId").GetString()
        ?? throw new InvalidOperationException("Binding request did not include a threadId.");
    var grantId = "grant_" + Guid.NewGuid().ToString("N");
    var accepted = await client.AppBindings.AcceptBindingAsync(new AcceptBindingRequest(
        BindingRequestId: handoff.RequestId,
        RequestToken: handoff.RequestToken,
        GrantId: grantId,
        GrantedScopes: ["card.read"],
        ApprovalMode: "appAccepted",
        ApprovedBy: Environment.UserName), ct);
    await client.AppBindings.AttachToolsAsync(new AttachToolsRequest(
        BindingId: accepted.Binding.BindingId,
        ThreadId: threadId,
        AppId: AppId,
        GrantId: grantId,
        Tools: BuildTools()), ct);
    Console.WriteLine($"Bound + attached '{ToolNamespace}.{ToolName}' to thread {threadId}.");

    RegisterHandlers(client, threadId);
    Console.WriteLine("Ready. Trigger ShowCard in Desktop. Press Ctrl+C to exit.");
    await KeepAliveAsync(client);
    return 0;
}

string TokenFromHandoff(AppHandoffMode handoff)
{
    var uri = handoff.Uri ?? throw new InvalidOperationException("Handoff did not include a URI to extract the request token.");
    return AppBindingHandoff.Parse(uri).RequestToken;
}

DynamicToolSpec[] BuildTools()
{
    var inputSchema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new { note = new { type = "string", description = "A note to render on the card." } }
    });
    return
    [
        new DynamicToolSpec(
            Namespace: ToolNamespace,
            Name: ToolName,
            Description: "Show a sample interactive card for a note.",
            InputSchema: inputSchema,
            Meta: new DynamicToolMeta(new DynamicToolUiMeta(
                ResourceUri: ResourceUri,
                Visibility: ["model", "app"],
                PrefersBorder: true)))
    ];
}

void RegisterHandlers(DotCraftClient client, string threadId)
{
    client.RegisterDynamicToolHandler(threadId, ToolNamespace, ToolName, (call, _) =>
    {
        var note = call.Arguments.ValueKind == JsonValueKind.Object
                   && call.Arguments.TryGetProperty("note", out var n)
                   && n.ValueKind == JsonValueKind.String
            ? n.GetString()
            : "(no note provided)";
        return Task.FromResult(new DynamicToolResult(
            Success: true,
            StructuredResult: new { title = "Sample Card", value = note, ts = DateTimeOffset.UtcNow.ToString("u") },
            Meta: new { accent = "sample" }));
    });

    client.RegisterResourceHandler(ResourceUri, (_, _) =>
        Task.FromResult(new ResourceReadResult(new[]
        {
            new ResourceContent(ResourceUri, "text/html;profile=mcp-app", CardResource.Html)
        })));
}

async Task KeepAliveAsync(DotCraftClient client)
{
    try
    {
        await client.AppBindings.KeepAliveAsync(
            onNotification: (notification, _) =>
            {
                Console.WriteLine($"  · {notification.Method}");
                return Task.CompletedTask;
            },
            cancellationToken: ct);
    }
    catch (OperationCanceledException)
    {
        // Ctrl+C — graceful shutdown.
    }
}

/// <summary>The self-contained Interactive Tool UI document served on <c>item/resource/read</c>.</summary>
static class CardResource
{
    public const string Html = """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8" />
<style>
  :root { color-scheme: dark; --bg:#1e1e22; --fg:#e8e8ea; --muted:#9a9aa2; --accent:#6ea8fe; --border:rgba(255,255,255,.12); }
  html[data-theme="light"] { color-scheme: light; --bg:#ffffff; --fg:#1a1a1e; --muted:#6a6a72; --accent:#2f6fed; --border:rgba(0,0,0,.12); }
  * { box-sizing: border-box; }
  body { margin:0; font:14px/1.5 system-ui, -apple-system, sans-serif; color:var(--fg); background:var(--bg); }
  .card { padding:16px; }
  .eyebrow { font-size:11px; letter-spacing:.08em; text-transform:uppercase; color:var(--muted); }
  h1 { font-size:18px; margin:4px 0 12px; }
  .value { padding:12px; border:1px solid var(--border); border-radius:8px; background:rgba(127,127,127,.06); white-space:pre-wrap; }
  .meta { margin-top:10px; font-size:12px; color:var(--muted); }
  button { margin-top:14px; font:inherit; padding:8px 14px; border-radius:8px; border:1px solid var(--border); background:var(--accent); color:#fff; cursor:pointer; }
</style>
</head>
<body>
  <div class="card">
    <div class="eyebrow">Interactive Tool UI</div>
    <h1 id="title">Loading…</h1>
    <div class="value" id="value">Waiting for tool result…</div>
    <div class="meta" id="meta"></div>
    <button id="open">Open in Sample</button>
  </div>
<script>
  const pending = {};
  let nextId = 1;
  function notify(method, params) { parent.postMessage({ jsonrpc: "2.0", method, params }, "*"); }
  function request(method, params) {
    return new Promise((resolve) => {
      const id = nextId++;
      pending[id] = resolve;
      parent.postMessage({ jsonrpc: "2.0", id, method, params }, "*");
    });
  }
  function applyContext(ctx) { if (ctx && ctx.theme) document.documentElement.dataset.theme = ctx.theme; }
  function renderResult(p) {
    const sc = (p && p.structuredContent) || {};
    document.getElementById("title").textContent = sc.title || "Sample Card";
    document.getElementById("value").textContent = sc.value != null ? String(sc.value) : "";
    document.getElementById("meta").textContent = sc.ts ? ("Updated " + sc.ts) : "";
  }
  window.addEventListener("message", (event) => {
    const m = event.data;
    if (!m || typeof m !== "object") return;
    if (m.id !== undefined && pending[m.id]) { const r = pending[m.id]; delete pending[m.id]; r(m.result); return; }
    if (m.method === "ui/notifications/tool-result") renderResult(m.params);
  });
  document.getElementById("open").addEventListener("click", () => {
    // M-iii will route this through ui/open-link; the read-only M-ii host ignores it.
    notify("ui/open-link", { url: "https://github.com/DotHarness/dotcraft" });
  });
  (async () => {
    const result = await request("ui/initialize", { app: { name: "dotcraft-sample-ui", version: "0.1.0" } });
    applyContext(result && result.hostContext);
  })();
</script>
</body>
</html>
""";
}
