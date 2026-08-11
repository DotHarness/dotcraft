using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime;

namespace DotCraft.DynamicWorkflows;

public static class WorkflowWorkerRunner
{
    private const string Bootstrap = """
        const __deepFreeze = value => {
          if (value && typeof value === 'object' && !Object.isFrozen(value)) {
            Object.freeze(value);
            for (const key of Object.keys(value)) __deepFreeze(value[key]);
          }
          return value;
        };
        const __args = __deepFreeze(globalThis.__argsValue ?? {});
        const __budgetSource = __deepFreeze(globalThis.__budgetValue ?? {});
        const __cwd = globalThis.__cwdValue;
        const __callAgent = globalThis.__agent;
        const __sendPhase = globalThis.__phase;
        const __sendLog = globalThis.__log;
        delete globalThis.__argsValue;
        delete globalThis.__budgetValue;
        delete globalThis.__cwdValue;
        delete globalThis.__agent;
        delete globalThis.__phase;
        delete globalThis.__log;
        Object.defineProperty(globalThis, 'args', { value: __args, writable: false, configurable: false });
        const __budgetView = {};
        for (const key of Object.keys(__budgetSource)) {
          Object.defineProperty(__budgetView, key, { enumerable: true, get: () => __budgetSource[key] });
        }
        Object.defineProperty(globalThis, 'budget', { value: Object.freeze(__budgetView), writable: false, configurable: false });
        Object.defineProperty(globalThis, 'cwd', { value: __cwd, writable: false, configurable: false });
        Object.defineProperty(globalThis, 'process', { value: Object.freeze({ cwd: () => __cwd }), writable: false, configurable: false });
        globalThis.agent = (input, options = {}) => {
          return __callAgent(options ?? {}, input);
        };
        globalThis.parallel = thunks => {
          if (!Array.isArray(thunks) || thunks.some(item => typeof item !== 'function'))
            throw new TypeError('parallel() requires an array of functions.');
          const pending = thunks.map(thunk => thunk());
          return Promise.all(pending);
        };
        globalThis.pipeline = (items, ...stages) => {
          if (!Array.isArray(items) || stages.some(stage => typeof stage !== 'function'))
            throw new TypeError('pipeline() requires an item array followed by stage functions.');
          return Promise.all(items.map(async (item, index) => {
            const original = item;
            let value = original;
            for (const stage of stages) {
              if (value === null) break;
              value = await stage(value, original, index);
            }
            return value;
          }));
        };
        globalThis.phase = (name, detail) => __sendPhase(name, detail);
        globalThis.log = value => __sendLog(value);
        for (const name of ['Date','Temporal','setTimeout','setInterval','clearTimeout','clearInterval','performance','crypto','WebAssembly','WeakRef','FinalizationRegistry','eval','Function']) {
          Object.defineProperty(globalThis, name, { value: undefined, writable: false, configurable: false });
        }
        Object.defineProperty(Math, 'random', { value: undefined, writable: false, configurable: false });
        """;

    public static async Task<int> RunAsync(
        Stream input,
        Stream output,
        Stream error,
        CancellationToken cancellationToken = default)
    {
        var errorWriter = new StreamWriter(error, new System.Text.UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        WorkflowProtocolConnection? connection = null;
        string? runId = null;
        string? attemptId = null;
        try
        {
            connection = new WorkflowProtocolConnection(input, output, 4 * 1024 * 1024);
            var initialize = await connection.ReadAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new WorkflowProtocolException("initialize_missing", "Worker stdin closed before initialize.");
            runId = initialize.RunId;
            attemptId = initialize.AttemptId;
            if (!string.Equals(initialize.Type, "initialize", StringComparison.Ordinal))
                throw new WorkflowProtocolException("initialize_missing", "The first Host frame must be initialize.");
            var payload = initialize.Payload as JsonObject
                ?? throw new WorkflowProtocolException("initialize_invalid", "Initialize payload must be an object.");
            var script = payload["script"]?.GetValue<string>()
                ?? throw new WorkflowProtocolException("initialize_invalid", "Initialize script is required.");
            var expectedHash = payload["scriptHash"]?.GetValue<string>()
                ?? throw new WorkflowProtocolException("initialize_invalid", "Initialize script hash is required.");
            var limits = payload["limits"]?.Deserialize<DynamicWorkflowLimits>(WorkflowProtocolConnection.JsonOptions)
                ?? throw new WorkflowProtocolException("initialize_invalid", "Initialize limits are required.");
            limits.Validate();
            var parser = new DynamicWorkflowParser();
            var parsed = parser.Parse(script, limits.MaxScriptBytes);
            if (!string.Equals(parsed.SourceHash, expectedHash, StringComparison.Ordinal))
                throw new WorkflowProtocolException("script_hash_mismatch", "Worker script hash does not match the Host snapshot.");

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts.CancelAfter(limits.RunTimeout);
            var pending = new ConcurrentDictionary<string, TaskCompletionSource<object?>>(StringComparer.Ordinal);
            var operationSequence = 0;
            var receiver = ReceiveHostFramesAsync(connection, runId, attemptId, pending, linkedCts);

            var engine = new Engine(options =>
            {
                options.Strict();
                options.DisableStringCompilation();
                options.LimitMemory(limits.MaxJintMemoryBytes);
                options.MaxStatements(limits.MaxStatements);
                options.TimeoutInterval(limits.RunTimeout);
                options.LimitRecursion(limits.MaxRecursionDepth);
                options.CancellationToken(linkedCts.Token);
                options.ExperimentalFeatures = ExperimentalFeature.TaskInterop;
                options.Constraints.PromiseTimeout = limits.RunTimeout;
            });
            var args = engine.Evaluate($"({payload["args"]?.ToJsonString() ?? "{}"})");
            var budget = engine.Evaluate($"({payload["budget"]?.ToJsonString() ?? "{}"})");
            var cwd = payload["cwd"]?.GetValue<string>()
                ?? throw new WorkflowProtocolException("initialize_invalid", "Initialize working directory is required.");
            engine.SetValue("__argsValue", args);
            engine.SetValue("__budgetValue", budget);
            engine.SetValue("__cwdValue", cwd);
            engine.SetValue("__agent", new Func<object?, object?, Task<object?>>(async (options, agentInput) =>
            {
                var operationId = $"op_{Interlocked.Increment(ref operationSequence):D4}";
                var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
                if (!pending.TryAdd(operationId, completion)) throw new InvalidOperationException("Duplicate workflow operation id.");
                await connection.WriteAsync(runId, attemptId, "agent.request", new JsonObject
                {
                    ["operationId"] = operationId,
                    ["options"] = JsonSerializer.SerializeToNode(options, WorkflowProtocolConnection.JsonOptions),
                    ["input"] = JsonSerializer.SerializeToNode(agentInput, WorkflowProtocolConnection.JsonOptions)
                }, linkedCts.Token).ConfigureAwait(false);
                return await completion.Task.WaitAsync(linkedCts.Token).ConfigureAwait(false);
            }));
            engine.SetValue("__phase", new Action<object?, object?>((name, detail) =>
                connection.WriteAsync(runId, attemptId, "phase", new JsonObject
                {
                    ["name"] = JsonSerializer.SerializeToNode(name),
                    ["detail"] = JsonSerializer.SerializeToNode(detail)
                }, linkedCts.Token).GetAwaiter().GetResult()));
            engine.SetValue("__log", new Action<object?>(value =>
                connection.WriteAsync(runId, attemptId, "log", new JsonObject
                {
                    ["value"] = JsonSerializer.SerializeToNode(value)
                }, linkedCts.Token).GetAwaiter().GetResult()));
            engine.Execute(Bootstrap);
            await connection.WriteAsync(runId, attemptId, "ready", null, linkedCts.Token).ConfigureAwait(false);
            var result = await engine.EvaluateAsync(parsed.ExecutableSource).ConfigureAwait(false);
            var node = ConvertResult(engine, result, new HashSet<ObjectInstance>(ReferenceEqualityComparer.Instance));
            var resultBytes = System.Text.Encoding.UTF8.GetByteCount(node?.ToJsonString() ?? "null");
            if (resultBytes > limits.MaxResultBytes)
                throw new DynamicWorkflowValidationException("result_too_large", "Workflow result exceeds the configured limit.");
            linkedCts.Cancel();
            await connection.WriteAsync(runId, attemptId, "complete", new JsonObject { ["result"] = node }, CancellationToken.None).ConfigureAwait(false);
            _ = receiver.ContinueWith(
                static task => _ = task.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return 0;
        }
        catch (Exception ex)
        {
            await errorWriter.WriteLineAsync(ex.ToString()).ConfigureAwait(false);
            if (connection != null && runId != null && attemptId != null)
            {
                try
                {
                    await connection.WriteAsync(runId, attemptId, "failed", new JsonObject
                    {
                        ["code"] = ex is DynamicWorkflowValidationException validation ? validation.Code
                            : ex is WorkflowProtocolException protocol ? protocol.Code
                            : "worker_failed",
                        ["message"] = ex.Message
                    }, CancellationToken.None).ConfigureAwait(false);
                }
                catch { }
            }
            return 1;
        }
        finally
        {
            if (connection != null) await connection.DisposeAsync().ConfigureAwait(false);
            await errorWriter.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static JsonNode? ConvertResult(
        Engine engine,
        JsValue value,
        HashSet<ObjectInstance> ancestors)
    {
        switch (value.Type)
        {
            case Types.Null:
                return null;
            case Types.Boolean:
                return JsonValue.Create(value.AsBoolean());
            case Types.String:
                return JsonValue.Create(value.AsString());
            case Types.Number:
                var number = value.AsNumber();
                if (!double.IsFinite(number)) throw NonJsonResult();
                return JsonValue.Create(number);
            case Types.Object:
                return ConvertObject(engine, value.AsObject(), ancestors);
            default:
                throw NonJsonResult();
        }
    }

    private static JsonNode ConvertObject(
        Engine engine,
        ObjectInstance value,
        HashSet<ObjectInstance> ancestors)
    {
        if (!ancestors.Add(value)) throw NonJsonResult();
        try
        {
            if (value.IsArray())
            {
                var source = value.AsArray();
                var arrayResult = new JsonArray();
                for (uint index = 0; index < source.Length; index++)
                    arrayResult.Add(ConvertResult(engine, source[index], ancestors));
                return arrayResult;
            }
            if (value.Prototype != null
                && !ReferenceEquals(value.Prototype, engine.Intrinsics.Object.PrototypeObject))
                throw NonJsonResult();

            var properties = new SortedDictionary<string, JsonNode?>(StringComparer.Ordinal);
            foreach (var pair in value.GetOwnProperties())
            {
                if (!pair.Value.Enumerable) continue;
                if (!pair.Key.IsString() || !pair.Value.IsDataDescriptor()) throw NonJsonResult();
                properties.Add(pair.Key.AsString(), ConvertResult(engine, pair.Value.Value, ancestors));
            }
            var objectResult = new JsonObject();
            foreach (var pair in properties) objectResult[pair.Key] = pair.Value;
            return objectResult;
        }
        finally { ancestors.Remove(value); }
    }

    private static DynamicWorkflowValidationException NonJsonResult() =>
        new("result_not_serializable", "Workflow values must contain only JSON primitives, arrays, and plain objects.");

    private static async Task ReceiveHostFramesAsync(
        WorkflowProtocolConnection connection,
        string runId,
        string attemptId,
        ConcurrentDictionary<string, TaskCompletionSource<object?>> pending,
        CancellationTokenSource cancellation)
    {
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                var frame = await connection.ReadAsync(cancellation.Token).ConfigureAwait(false);
                if (frame == null) { cancellation.Cancel(); return; }
                if (!string.Equals(frame.RunId, runId, StringComparison.Ordinal) || !string.Equals(frame.AttemptId, attemptId, StringComparison.Ordinal))
                    throw new WorkflowProtocolException("protocol_identity_mismatch", "Host frame belongs to another run or attempt.");
                if (string.Equals(frame.Type, "cancel", StringComparison.Ordinal))
                {
                    cancellation.Cancel();
                    return;
                }
                if (!string.Equals(frame.Type, "agent.result", StringComparison.Ordinal) || frame.Payload is not JsonObject payload)
                    throw new WorkflowProtocolException("protocol_message_invalid", $"Unexpected Host message '{frame.Type}'.");
                var operationId = payload["operationId"]?.GetValue<string>() ?? string.Empty;
                if (!pending.TryRemove(operationId, out var completion))
                    throw new WorkflowProtocolException("protocol_operation_unknown", $"Unknown operation '{operationId}'.");
                if (payload["error"] is JsonValue errorValue && errorValue.TryGetValue<string>(out var error))
                    completion.TrySetException(new InvalidOperationException(error));
                else
                    completion.TrySetResult(ToClrJsonValue(payload["result"]));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            foreach (var completion in pending.Values) completion.TrySetException(ex);
            cancellation.Cancel();
            throw;
        }
    }

    private static object? ToClrJsonValue(JsonNode? node)
    {
        if (node == null) return null;
        var element = JsonSerializer.Deserialize<JsonElement>(node.ToJsonString());
        return ConvertElement(element);

        static object? ConvertElement(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.Array => value.EnumerateArray().Select(ConvertElement).ToArray(),
            JsonValueKind.Object => value.EnumerateObject().ToDictionary(
                property => property.Name,
                property => ConvertElement(property.Value),
                StringComparer.Ordinal),
            _ => throw new WorkflowProtocolException("protocol_result_invalid", "Agent result is not valid JSON.")
        };
    }
}
