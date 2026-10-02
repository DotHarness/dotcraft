using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Scripting;

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
        ScriptProtocolConnection? connection = null;
        string? scope = null;
        try
        {
            connection = new ScriptProtocolConnection(input, output, 4 * 1024 * 1024);
            var initialize = await connection.ReadAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new ScriptProtocolException("initialize_missing", "Worker stdin closed before initialize.");
            scope = initialize.Scope;
            if (!string.Equals(initialize.Type, "initialize", StringComparison.Ordinal))
                throw new ScriptProtocolException("initialize_missing", "The first Host frame must be initialize.");
            var payload = initialize.Payload as JsonObject
                ?? throw new ScriptProtocolException("initialize_invalid", "Initialize payload must be an object.");
            var script = payload["script"]?.GetValue<string>()
                ?? throw new ScriptProtocolException("initialize_invalid", "Initialize script is required.");
            var expectedHash = payload["scriptHash"]?.GetValue<string>()
                ?? throw new ScriptProtocolException("initialize_invalid", "Initialize script hash is required.");
            var limits = payload["limits"]?.Deserialize<DynamicWorkflowLimits>(ScriptProtocolConnection.JsonOptions)
                ?? throw new ScriptProtocolException("initialize_invalid", "Initialize limits are required.");
            limits.Validate();
            var parser = new DynamicWorkflowParser();
            var parsed = parser.Parse(script, limits.MaxScriptBytes);
            if (!string.Equals(parsed.SourceHash, expectedHash, StringComparison.Ordinal))
                throw new ScriptProtocolException("script_hash_mismatch", "Worker script hash does not match the Host snapshot.");

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts.CancelAfter(limits.RunTimeout);
            var calls = new ScriptHostCalls("op_");
            var receiver = ReceiveHostFramesAsync(connection, scope, calls, linkedCts);

            var engine = ScriptEngineFactory.Create(
                new ScriptEngineLimits(limits.MaxJintMemoryBytes, limits.MaxStatements, limits.MaxRecursionDepth, limits.RunTimeout),
                linkedCts.Token);
            var args = engine.Evaluate($"({payload["args"]?.ToJsonString() ?? "{}"})");
            var budget = engine.Evaluate($"({payload["budget"]?.ToJsonString() ?? "{}"})");
            var cwd = payload["cwd"]?.GetValue<string>()
                ?? throw new ScriptProtocolException("initialize_invalid", "Initialize working directory is required.");
            engine.SetValue("__argsValue", args);
            engine.SetValue("__budgetValue", budget);
            engine.SetValue("__cwdValue", cwd);
            engine.SetValue("__agent", new Func<object?, object?, Task<object?>>((options, agentInput) =>
                calls.CallAsync(operationId => connection.WriteAsync(scope, "agent.request", new JsonObject
                {
                    ["operationId"] = operationId,
                    ["options"] = JsonSerializer.SerializeToNode(options, ScriptProtocolConnection.JsonOptions),
                    ["input"] = JsonSerializer.SerializeToNode(agentInput, ScriptProtocolConnection.JsonOptions)
                }, linkedCts.Token), linkedCts.Token)));
            engine.SetValue("__phase", new Action<object?, object?>((name, detail) =>
                connection.WriteAsync(scope, "phase", new JsonObject
                {
                    ["name"] = JsonSerializer.SerializeToNode(name),
                    ["detail"] = JsonSerializer.SerializeToNode(detail)
                }, linkedCts.Token).GetAwaiter().GetResult()));
            engine.SetValue("__log", new Action<object?>(value =>
                connection.WriteAsync(scope, "log", new JsonObject
                {
                    ["value"] = JsonSerializer.SerializeToNode(value)
                }, linkedCts.Token).GetAwaiter().GetResult()));
            engine.Execute(Bootstrap);
            await connection.WriteAsync(scope, "ready", null, linkedCts.Token).ConfigureAwait(false);
            var result = await engine.EvaluateAsync(parsed.ExecutableSource).ConfigureAwait(false);
            var node = ScriptValues.ToJson(engine, result);
            var resultBytes = System.Text.Encoding.UTF8.GetByteCount(node?.ToJsonString() ?? "null");
            if (resultBytes > limits.MaxResultBytes)
                throw new DynamicWorkflowValidationException("result_too_large", "Workflow result exceeds the configured limit.");
            linkedCts.Cancel();
            await connection.WriteAsync(scope, "complete", new JsonObject { ["result"] = node }, CancellationToken.None).ConfigureAwait(false);
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
            if (connection != null && scope != null)
            {
                try
                {
                    await connection.WriteAsync(scope, "failed", new JsonObject
                    {
                        ["code"] = ex switch
                        {
                            DynamicWorkflowValidationException validation => validation.Code,
                            ScriptProtocolException protocol => protocol.Code,
                            ScriptValueException value => value.Code,
                            _ => "worker_failed"
                        },
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

    private static async Task ReceiveHostFramesAsync(
        ScriptProtocolConnection connection,
        string scope,
        ScriptHostCalls calls,
        CancellationTokenSource cancellation)
    {
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                var frame = await connection.ReadAsync(cancellation.Token).ConfigureAwait(false);
                if (frame == null)
                {
                    calls.CancelAll();
                    cancellation.Cancel();
                    return;
                }
                if (!string.Equals(frame.Scope, scope, StringComparison.Ordinal))
                    throw new ScriptProtocolException("protocol_identity_mismatch", "Host frame belongs to another run or attempt.");
                if (string.Equals(frame.Type, "cancel", StringComparison.Ordinal))
                {
                    cancellation.Cancel();
                    return;
                }
                if (!string.Equals(frame.Type, "agent.result", StringComparison.Ordinal) || frame.Payload is not JsonObject payload)
                    throw new ScriptProtocolException("protocol_message_invalid", $"Unexpected Host message '{frame.Type}'.");
                var error = payload["error"] is JsonValue errorValue && errorValue.TryGetValue<string>(out var text) ? text : null;
                calls.Resolve(payload["operationId"]?.GetValue<string>() ?? string.Empty, payload["result"], error);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            calls.FailAll(ex);
            cancellation.Cancel();
            throw;
        }
    }
}
