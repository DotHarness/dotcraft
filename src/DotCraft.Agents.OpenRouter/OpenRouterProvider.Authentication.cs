using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotCraft.Configuration;

namespace DotCraft.Agents;

public sealed partial class OpenRouterProvider
{
    public Task<ProviderAuthenticationStatus> GetStatusAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(new ProviderAuthenticationStatus(!string.IsNullOrWhiteSpace(ApiKey())));
    }

    public async Task<ProviderAuthenticationStatus> LoginAsync(string method, IProviderLoginInteraction interaction, CancellationToken ct)
    {
        if (method != OpenRouterBackend.OAuth) throw new ArgumentException("Use the API key field to configure a key.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct, _context.Stopping);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        var token = timeout.Token;
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        var port = ((IPEndPoint)socket.LocalEndpoint).Port;
        socket.Stop();
        var callback = new Uri($"http://127.0.0.1:{port}/{Convert.ToHexString(RandomNumberGenerator.GetBytes(24))}/");
        using var listener = new HttpListener();
        listener.Prefixes.Add(callback.GetLeftPart(UriPartial.Authority) + "/");
        listener.Start();
        var authorize = "https://openrouter.ai/auth?callback_url=" + Uri.EscapeDataString(callback.AbsoluteUri)
            + "&code_challenge=" + challenge + "&code_challenge_method=S256";
        await interaction.NotifyAsync(new("auth_url", "Authorize OpenRouter in your browser.", authorize), token).ConfigureAwait(false);
        using var inputCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var browser = ReceiveCallbackAsync(listener, callback, inputCancellation.Token);
        var manual = interaction.PromptAsync(new("text", "Paste the complete OpenRouter callback URL if the browser cannot reach this host."), inputCancellation.Token);
        string code;
        try
        {
            var winner = await Task.WhenAny(browser, manual).WaitAsync(token).ConfigureAwait(false);
            code = winner == browser ? await browser.ConfigureAwait(false) : ParseCallback(await manual.ConfigureAwait(false), callback);
        }
        finally
        {
            await inputCancellation.CancelAsync().ConfigureAwait(false);
            listener.Stop();
            await ObserveAsync(browser).ConfigureAwait(false);
            await ObserveAsync(manual).ConfigureAwait(false);
        }
        using var response = await _http.PostAsJsonAsync(_root + "/auth/keys", new OpenRouterKeyExchange(code, verifier),
            OpenRouterJson.Options, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var credential = await response.Content.ReadFromJsonAsync<OpenRouterCredential>(OpenRouterJson.Options, token).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(credential?.Key)) throw new JsonException("OpenRouter authorization returned no API key.");
        await _context.Credentials.ModifyAsync((_, commitToken) =>
        {
            commitToken.ThrowIfCancellationRequested();
            token.ThrowIfCancellationRequested();
            return Task.FromResult<JsonElement?>(JsonSerializer.SerializeToElement(credential, OpenRouterJson.Options));
        }, token).ConfigureAwait(false);
        return new ProviderAuthenticationStatus(true);
    }

    public Task LogoutAsync(CancellationToken ct) => _context.Credentials.DeleteAsync(ct);

    public async Task<ProviderUsageSnapshot?> ReadAsync(CancellationToken ct)
    {
        if (_context.Runtime.IsRemote)
        {
            var usage = _context.Runtime.RemoteUsage;
            var remoteValues = usage?.Values.ToDictionary() ?? new Dictionary<string, double>();
            lock (_usageGate) { if (_lastRequestCost is { } cost) remoteValues["last_request_cost"] = (double)cost; }
            return usage is not null ? usage with { Values = remoteValues }
                : remoteValues.Count == 0 ? null : new ProviderUsageSnapshot(DateTimeOffset.UtcNow, remoteValues,
                    new Dictionary<string, string> { ["currency"] = "USD", ["scope"] = "apiKey" });
        }
        var key = ApiKey();
        if (string.IsNullOrWhiteSpace(key)) return null;
        string json;
        try { json = await GetAsync("key", key, ct).ConfigureAwait(false); }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        { return null; }
        var data = OpenRouterJson.Read<OpenRouterData<OpenRouterKeyUsage>>(json).Data;
        var values = new Dictionary<string, double>();
        lock (_usageGate) { if (_lastRequestCost is { } cost) values["last_request_cost"] = (double)cost; }
        Add("usage", data.Usage);
        Add("usage_daily", data.UsageDaily);
        Add("usage_weekly", data.UsageWeekly);
        Add("usage_monthly", data.UsageMonthly);
        Add("limit", data.Limit);
        Add("limit_remaining", data.LimitRemaining);
        var unlimited = data.Limit is null;
        return new ProviderUsageSnapshot(DateTimeOffset.UtcNow, values,
            new Dictionary<string, string> { ["currency"] = "USD", ["scope"] = "apiKey" },
            Credits: new ProviderCreditStatus(unlimited || data.LimitRemaining is null or > 0, unlimited,
                data.LimitRemaining?.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        void Add(string name, double? value) { if (value is { } number) values[name] = number; }
    }

    public static string ParseCallback(string input, Uri expected)
    {
        if (!Uri.TryCreate(input.Trim(), UriKind.Absolute, out var actual)
            || actual.GetLeftPart(UriPartial.Path) != expected.GetLeftPart(UriPartial.Path))
            throw new ArgumentException("The authorization callback URL does not match this sign-in attempt.");
        var query = ParseQuery(actual.Query);
        if (query.ContainsKey("error")) throw new ArgumentException("OpenRouter authorization was declined.");
        return query.TryGetValue("code", out var code) && !string.IsNullOrWhiteSpace(code) ? code
            : throw new ArgumentException("The authorization callback contains no code.");
    }

    private static async Task<string> ReceiveCallbackAsync(HttpListener listener, Uri expected, CancellationToken ct)
    {
        while (true)
        {
            var request = await listener.GetContextAsync().WaitAsync(ct).ConfigureAwait(false);
            if (request.Request.Url?.AbsolutePath != expected.AbsolutePath)
            { request.Response.StatusCode = 404; request.Response.Close(); continue; }
            try
            {
                var code = ParseCallback(request.Request.Url.AbsoluteUri, expected);
                var message = Encoding.UTF8.GetBytes("OpenRouter connected. You can close this window.");
                request.Response.ContentType = "text/plain; charset=utf-8";
                await request.Response.OutputStream.WriteAsync(message, ct).ConfigureAwait(false);
                request.Response.Close();
                return code;
            }
            catch (ArgumentException) { request.Response.StatusCode = 400; request.Response.Close(); throw; }
        }
    }

    private static Dictionary<string, string> ParseQuery(string query) => query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(pair => pair.Split('=', 2)).ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : string.Empty);
    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static async Task ObserveAsync(Task task)
    {
        try { await task.ConfigureAwait(false); }
        catch (Exception ex) when (ex is OperationCanceledException or HttpListenerException or ObjectDisposedException or IOException or ArgumentException) { }
    }
}
