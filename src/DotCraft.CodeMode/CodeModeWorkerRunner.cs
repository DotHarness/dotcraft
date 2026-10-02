using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Nodes;
using DotCraft.Scripting;
using Jint;
using Jint.Native;
using Jint.Runtime;

namespace DotCraft.CodeMode;

public static class CodeModeWorkerRunner
{
    private const int MaxStoreValueBytes = 256 * 1024;
    private const int MaxStoreBytes = 1024 * 1024;

    public static async Task<int> RunAsync(
        Stream input,
        Stream output,
        Stream error,
        CancellationToken cancellationToken = default)
    {
        await using var errorWriter = new StreamWriter(error, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        await using var connection = new ScriptProtocolConnection(input, output, new CodeModeLimits().MaxFrameBytes);
        var cells = new ConcurrentDictionary<string, WorkerCell>(StringComparer.Ordinal);
        var running = new ConcurrentDictionary<Task, byte>();
        try
        {
            await connection.WriteAsync(CodeModeProtocol.WorkerScope, CodeModeProtocol.Ready, null, cancellationToken).ConfigureAwait(false);
            while (await connection.ReadAsync(cancellationToken).ConfigureAwait(false) is { } frame)
            {
                switch (frame.Type)
                {
                    case CodeModeProtocol.CellStart:
                        var cell = new WorkerCell(frame.Scope, frame.Payload as JsonObject
                            ?? throw new ScriptProtocolException("protocol_message_invalid", "cell.start requires an object payload."));
                        if (!cells.TryAdd(cell.Id, cell))
                            throw new ScriptProtocolException("protocol_cell_duplicate", $"Cell '{cell.Id}' is already running.");
                        var task = Task.Factory.StartNew(
                                () => RunCellAsync(connection, cell, cells, errorWriter),
                                CancellationToken.None,
                                TaskCreationOptions.LongRunning,
                                TaskScheduler.Default)
                            .Unwrap();
                        running[task] = 0;
                        _ = task.ContinueWith(completed => running.TryRemove(completed, out _), TaskScheduler.Default);
                        break;
                    case CodeModeProtocol.CallResult:
                        if (cells.TryGetValue(frame.Scope, out var target) && frame.Payload is JsonObject result)
                            target.Resolve(result);
                        break;
                    case CodeModeProtocol.CellCancel:
                        if (cells.TryGetValue(frame.Scope, out var cancelled))
                            cancelled.Cancel(frame.Payload?["reason"]?.GetValue<string>() ?? "cancelled");
                        break;
                    default:
                        throw new ScriptProtocolException("protocol_message_invalid", $"Unexpected host message '{frame.Type}'.");
                }
            }
            return 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await errorWriter.WriteLineAsync(ex.ToString()).ConfigureAwait(false);
            return 1;
        }
        finally
        {
            foreach (var cell in cells.Values)
                cell.Cancel("cancelled");
            try { await Task.WhenAll(running.Keys).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
            catch { }
        }
    }

    private static async Task RunCellAsync(
        ScriptProtocolConnection connection,
        WorkerCell cell,
        ConcurrentDictionary<string, WorkerCell> cells,
        StreamWriter errorWriter)
    {
        var outcome = CodeModeProtocol.Completed;
        string? error = null;
        string? stack = null;
        try
        {
            var engine = ScriptEngineFactory.Create(cell.EngineLimits, cell.Token);
            Install(engine, connection, cell);
            engine.Execute(CodeModeWorkerBootstrap.Source);
            await engine.EvaluateAsync("(async () => {" + cell.Source + "\n})()", "exec").ConfigureAwait(false);
        }
        catch (Exception) when (cell.ExitRequested)
        {
        }
        catch (Exception ex)
        {
            (outcome, error, stack) = Describe(cell, ex);
        }
        finally
        {
            cell.EndCalls();
        }

        var payload = new JsonObject
        {
            ["outcome"] = outcome,
            ["error"] = error,
            ["stack"] = stack
        };
        if (outcome == CodeModeProtocol.Completed)
            payload["store"] = cell.StoreWrites();
        cells.TryRemove(cell.Id, out _);
        try
        {
            await connection.WriteAsync(cell.Id, CodeModeProtocol.CellDone, payload, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await errorWriter.WriteLineAsync(ex.ToString()).ConfigureAwait(false);
        }
    }

    private static (string Outcome, string? Error, string? Stack) Describe(WorkerCell cell, Exception exception)
    {
        if (cell.CancelReason == CodeModeProtocol.TimeoutReason)
            return (CodeModeProtocol.TimedOut, "The script exceeded its deadline.", null);
        if (cell.CancelReason is { } reason)
            return (CodeModeProtocol.Failed, $"The script was stopped: {reason}.", null);
        return exception switch
        {
            PromiseRejectedException rejected => (CodeModeProtocol.Failed, ErrorMessage(rejected.RejectedValue), ErrorStack(rejected.RejectedValue)),
            JavaScriptException script => (CodeModeProtocol.Failed, script.Message, script.JavaScriptStackTrace),
            MemoryLimitExceededException => (CodeModeProtocol.Failed, $"The script exceeded its {cell.EngineLimits.MaxMemoryBytes / (1024 * 1024)} MiB memory limit.", null),
            StatementsCountOverflowException => (CodeModeProtocol.Failed, $"The script exceeded its limit of {cell.EngineLimits.MaxStatements} statements.", null),
            RecursionDepthOverflowException => (CodeModeProtocol.Failed, $"The script exceeded its recursion limit of {cell.EngineLimits.MaxRecursionDepth}.", null),
            _ => (CodeModeProtocol.Failed, exception.Message, null)
        };
    }

    private static string ErrorMessage(JsValue value)
    {
        if (value.IsObject())
        {
            var error = value.AsObject();
            var message = error.Get("message");
            if (!message.IsUndefined())
            {
                var name = error.Get("name");
                return name.IsString() ? $"{name.AsString()}: {message}" : message.ToString();
            }
        }
        return value.ToString();
    }

    private static string? ErrorStack(JsValue value)
    {
        if (!value.IsObject())
            return null;
        var stack = value.AsObject().Get("stack");
        return stack.IsString() ? stack.AsString() : null;
    }

    private static void Install(Engine engine, ScriptProtocolConnection connection, WorkerCell cell)
    {
        engine.SetValue("__toolsJson", cell.ToolsJson);
        engine.SetValue("__aliasesJson", cell.AliasesJson);
        engine.SetValue("__call", new Func<string, JsValue, Task<object?>>((name, args) =>
        {
            JsonNode? arguments;
            if (args.IsUndefined() || args.IsNull())
                arguments = new JsonObject();
            else if (!args.IsObject() || args.IsArray())
                return Task.FromResult<object?>(CodeModeProtocol.ErrorEnvelope($"tools.{name} requires an object argument."));
            else
            {
                try
                {
                    arguments = ScriptValues.ToJson(engine, args);
                }
                catch (ScriptValueException ex)
                {
                    return Task.FromResult<object?>(CodeModeProtocol.ErrorEnvelope($"tools.{name} arguments are not JSON-serializable: {ex.Message}"));
                }
            }
            return cell.CallAsync(requestId => connection.WriteAsync(cell.Id, CodeModeProtocol.CallRequest, new JsonObject
            {
                ["requestId"] = requestId,
                ["tool"] = name,
                ["arguments"] = arguments
            }, cell.Token));
        }));
        engine.SetValue("__text", new Func<string, string?>(text =>
            cell.TryReserveOutput(Encoding.UTF8.GetByteCount(text), out var limitError)
                ? Send(connection, cell, new JsonObject { ["type"] = "text", ["text"] = text })
                : limitError));
        engine.SetValue("__image", new Func<string, string, string?>((mimeType, data) =>
        {
            var buffer = new byte[data.Length];
            if (!Convert.TryFromBase64String(data, buffer, out _))
                return "image() data is not valid base64.";
            return cell.TryReserveOutput(data.Length, out var limitError)
                ? Send(connection, cell, new JsonObject { ["type"] = "image", ["mimeType"] = mimeType, ["data"] = data })
                : limitError;
        }));
        engine.SetValue("__exit", new Action(cell.Exit));
        engine.SetValue("__storeSet", new Func<string, string, string?>(cell.StoreSet));
        engine.SetValue("__storeDelete", new Action<string>(cell.StoreDelete));
        engine.SetValue("__load", new Func<string, string?>(cell.Load));
    }

    private static string? Send(ScriptProtocolConnection connection, WorkerCell cell, JsonObject item)
    {
        connection.WriteAsync(cell.Id, CodeModeProtocol.CellOutput, item, CancellationToken.None).GetAwaiter().GetResult();
        return null;
    }

    private sealed class WorkerCell
    {
        private readonly CancellationTokenSource _cancellation = new();
        private readonly ScriptHostCalls _calls = new("call_");
        private readonly Dictionary<string, string> _store = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string?> _writes = new(StringComparer.Ordinal);
        private readonly long _maxOutputBytes;
        private long _storeBytes;
        private long _outputBytes;
        private volatile string? _cancelReason;

        public WorkerCell(string id, JsonObject payload)
        {
            Id = id;
            Source = payload["source"]?.GetValue<string>()
                ?? throw new ScriptProtocolException("protocol_message_invalid", "cell.start requires source.");
            var tools = payload["tools"] as JsonArray
                ?? throw new ScriptProtocolException("protocol_message_invalid", "cell.start requires tools.");
            ToolsJson = tools.ToJsonString();
            AliasesJson = (payload["aliases"] as JsonObject ?? new JsonObject()).ToJsonString();
            var limits = payload["limits"] as JsonObject
                ?? throw new ScriptProtocolException("protocol_message_invalid", "cell.start requires limits.");
            EngineLimits = new ScriptEngineLimits(
                limits["maxMemoryBytes"]!.GetValue<long>(),
                limits["maxStatements"]!.GetValue<int>(),
                limits["maxRecursionDepth"]!.GetValue<int>(),
                TimeSpan.FromMilliseconds(limits["engineTimeoutMs"]!.GetValue<long>()),
                TimeSpan.FromMilliseconds(limits["regexTimeoutMs"]!.GetValue<long>()));
            _maxOutputBytes = limits["maxOutputBytes"]!.GetValue<long>();
            if (payload["store"] is JsonObject store)
            {
                foreach (var (key, value) in store)
                {
                    var json = value?.ToJsonString() ?? "null";
                    _store[key] = json;
                    _storeBytes += Size(key, json);
                }
            }
        }

        public string Id { get; }
        public string Source { get; }
        public string ToolsJson { get; }
        public string AliasesJson { get; }
        public ScriptEngineLimits EngineLimits { get; }
        public CancellationToken Token => _cancellation.Token;
        public bool ExitRequested { get; private set; }
        public string? CancelReason => _cancelReason;

        public Task<object?> CallAsync(Func<string, Task> sendRequest) => _calls.CallAsync(sendRequest, Token);

        public void Resolve(JsonObject payload)
        {
            var requestId = payload["requestId"]?.GetValue<string>()
                ?? throw new ScriptProtocolException("protocol_message_invalid", "call.result requires requestId.");
            var envelope = payload["error"] is JsonValue error
                ? CodeModeProtocol.ErrorEnvelope(error.GetValue<string>())
                : new JsonObject { ["ok"] = true, ["value"] = payload["value"]?.DeepClone() }.ToJsonString();
            try
            {
                _calls.Resolve(requestId, JsonValue.Create(envelope), null);
            }
            catch (ScriptProtocolException) when (Token.IsCancellationRequested)
            {
            }
        }

        public void Cancel(string reason)
        {
            _cancelReason ??= reason;
            _cancellation.Cancel();
        }

        public void Exit()
        {
            ExitRequested = true;
            _cancellation.Cancel();
        }

        public void EndCalls()
        {
            _calls.CancelAll();
            _cancellation.Cancel();
        }

        public bool TryReserveOutput(long bytes, out string? error)
        {
            error = null;
            if (Interlocked.Add(ref _outputBytes, bytes) <= _maxOutputBytes)
                return true;
            error = $"Script output exceeded {_maxOutputBytes / (1024 * 1024)} MiB.";
            return false;
        }

        public string? StoreSet(string key, string json)
        {
            var size = Size(key, json);
            if (Encoding.UTF8.GetByteCount(json) > MaxStoreValueBytes)
                return $"store() value for '{key}' exceeds {MaxStoreValueBytes / 1024} KiB.";
            var previous = _store.TryGetValue(key, out var existing) ? Size(key, existing) : 0;
            if (_storeBytes - previous + size > MaxStoreBytes)
                return $"store() would exceed the {MaxStoreBytes / (1024 * 1024)} MiB store.";
            _storeBytes += size - previous;
            _store[key] = json;
            _writes[key] = json;
            return null;
        }

        public void StoreDelete(string key)
        {
            if (_store.Remove(key, out var existing))
                _storeBytes -= Size(key, existing);
            _writes[key] = null;
        }

        public string? Load(string key) => _store.GetValueOrDefault(key);

        public JsonObject StoreWrites()
        {
            var set = new JsonObject();
            var deleted = new JsonArray();
            foreach (var (key, json) in _writes)
            {
                if (json is null)
                    deleted.Add(key);
                else
                    set[key] = JsonNode.Parse(json);
            }
            return new JsonObject { ["set"] = set, ["deleted"] = deleted };
        }

        private static long Size(string key, string json) =>
            Encoding.UTF8.GetByteCount(key) + Encoding.UTF8.GetByteCount(json);
    }
}
