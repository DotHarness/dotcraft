using System.Net;
using System.Text;

namespace DotCraft.Auth.OpenAI;

/// <summary>
/// Spins up an HTTP listener on a loopback port to receive the OAuth authorization-code redirect.
/// Uses the registered loopback redirect ports so the configured redirect URI in OpenAI's app
/// registration is honored.
/// </summary>
public sealed class LoopbackOAuthServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly TaskCompletionSource<LoopbackOAuthResult> _resultSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenRegistration _cancelRegistration;

    public int Port { get; }

    public string RedirectUri => $"http://localhost:{Port}{OpenAIAuthConstants.RedirectCallbackPath}";

    private LoopbackOAuthServer(HttpListener listener, int port)
    {
        _listener = listener;
        Port = port;
    }

    /// <summary>
    /// Starts a listener, trying the primary port first and falling back if it is in use.
    /// </summary>
    public static LoopbackOAuthServer Start()
    {
        foreach (var port in new[] { OpenAIAuthConstants.RedirectPortPrimary, OpenAIAuthConstants.RedirectPortFallback })
        {
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://localhost:{port}/");
            try
            {
                listener.Start();
                return new LoopbackOAuthServer(listener, port);
            }
            catch (HttpListenerException)
            {
                listener.Close();
            }
        }
        throw new InvalidOperationException(
            $"Failed to bind loopback OAuth listener on ports {OpenAIAuthConstants.RedirectPortPrimary}/{OpenAIAuthConstants.RedirectPortFallback}.");
    }

    /// <summary>Begins accepting one callback. The returned task completes when the user is redirected back.</summary>
    public Task<LoopbackOAuthResult> AwaitCallbackAsync(string expectedState, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(expectedState);

        _cancelRegistration = cancellationToken.Register(() =>
        {
            _resultSource.TrySetCanceled(cancellationToken);
            try { _listener.Stop(); } catch { }
        });

        _ = Task.Run(async () =>
        {
            try
            {
                while (_listener.IsListening)
                {
                    var context = await _listener.GetContextAsync().ConfigureAwait(false);
                    await HandleRequestAsync(context, expectedState).ConfigureAwait(false);
                    if (_resultSource.Task.IsCompleted)
                        return;
                }
            }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested) { }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                _resultSource.TrySetException(ex);
            }
        }, cancellationToken);

        return _resultSource.Task;
    }

    private async Task HandleRequestAsync(HttpListenerContext context, string expectedState)
    {
        var request = context.Request;
        var response = context.Response;
        try
        {
            if (request.Url is null ||
                !string.Equals(request.Url.AbsolutePath, OpenAIAuthConstants.RedirectCallbackPath, StringComparison.Ordinal))
            {
                await WritePlainAsync(response, HttpStatusCode.NotFound, "Not found").ConfigureAwait(false);
                return;
            }

            var query = System.Web.HttpUtility.ParseQueryString(request.Url.Query);
            var code = query.Get("code");
            var state = query.Get("state");
            var error = query.Get("error");
            var errorDescription = query.Get("error_description");

            if (!string.IsNullOrEmpty(error))
            {
                await WriteHtmlAsync(response, HttpStatusCode.BadRequest, RenderErrorPage(error, errorDescription)).ConfigureAwait(false);
                _resultSource.TrySetResult(new LoopbackOAuthResult(false, null, error, errorDescription));
                return;
            }

            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
            {
                await WriteHtmlAsync(response, HttpStatusCode.BadRequest, RenderErrorPage("missing_code", "Authorization code was not returned by OpenAI.")).ConfigureAwait(false);
                _resultSource.TrySetResult(new LoopbackOAuthResult(false, null, "missing_code", null));
                return;
            }

            if (!string.Equals(state, expectedState, StringComparison.Ordinal))
            {
                await WriteHtmlAsync(response, HttpStatusCode.BadRequest, RenderErrorPage("state_mismatch", "State parameter did not match the request.")).ConfigureAwait(false);
                _resultSource.TrySetResult(new LoopbackOAuthResult(false, null, "state_mismatch", null));
                return;
            }

            await WriteHtmlAsync(response, HttpStatusCode.OK, RenderSuccessPage()).ConfigureAwait(false);
            _resultSource.TrySetResult(new LoopbackOAuthResult(true, code, null, null));
        }
        catch (Exception ex)
        {
            _resultSource.TrySetException(ex);
        }
    }

    private static async Task WritePlainAsync(HttpListenerResponse response, HttpStatusCode status, string body)
    {
        response.StatusCode = (int)status;
        response.ContentType = "text/plain; charset=utf-8";
        var bytes = Encoding.UTF8.GetBytes(body);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        response.Close();
    }

    private static async Task WriteHtmlAsync(HttpListenerResponse response, HttpStatusCode status, string body)
    {
        response.StatusCode = (int)status;
        response.ContentType = "text/html; charset=utf-8";
        var bytes = Encoding.UTF8.GetBytes(body);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        response.Close();
    }

    private static string RenderSuccessPage() => """
<!doctype html>
<html lang="en"><head><meta charset="utf-8"><title>Signed in to ChatGPT</title>
<style>
body{font-family:-apple-system,Segoe UI,Roboto,sans-serif;background:#0f1115;color:#f3f4f6;display:flex;align-items:center;justify-content:center;height:100vh;margin:0}
.card{background:#1c1f26;border-radius:16px;padding:32px 40px;box-shadow:0 12px 36px rgba(0,0,0,.4);text-align:center;max-width:420px}
h1{font-size:20px;margin:0 0 8px}
p{margin:0;color:#9ca3af;font-size:14px;line-height:1.5}
.check{width:48px;height:48px;border-radius:50%;background:#10b981;display:inline-flex;align-items:center;justify-content:center;margin-bottom:16px;color:white;font-size:24px;font-weight:bold}
</style></head><body>
<div class="card"><div class="check">✓</div>
<h1>Signed in to ChatGPT</h1>
<p>You can close this tab and return to DotCraft.</p></div></body></html>
""";

    private static string RenderErrorPage(string code, string? description)
    {
        var safeDescription = description ?? "Please try signing in again.";
        return $$"""
<!doctype html>
<html lang="en"><head><meta charset="utf-8"><title>Sign-in failed</title>
<style>
body{font-family:-apple-system,Segoe UI,Roboto,sans-serif;background:#0f1115;color:#f3f4f6;display:flex;align-items:center;justify-content:center;height:100vh;margin:0}
.card{background:#1c1f26;border-radius:16px;padding:32px 40px;box-shadow:0 12px 36px rgba(0,0,0,.4);text-align:center;max-width:420px}
h1{font-size:20px;margin:0 0 8px}
p{margin:0;color:#9ca3af;font-size:14px;line-height:1.5}
code{color:#f97316;background:#27272a;padding:2px 6px;border-radius:6px;font-size:13px}
</style></head><body>
<div class="card"><h1>Sign-in failed</h1>
<p><code>{{WebUtility.HtmlEncode(code)}}</code><br>{{WebUtility.HtmlEncode(safeDescription)}}</p></div></body></html>
""";
    }

    public void Dispose()
    {
        _cancelRegistration.Dispose();
        try { _listener.Stop(); } catch { }
        try { _listener.Close(); } catch { }
    }
}

/// <summary>Outcome of waiting for the OAuth redirect on the loopback server.</summary>
public sealed record LoopbackOAuthResult(bool Success, string? AuthorizationCode, string? Error, string? ErrorDescription);
