using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Contract = DotCraft.Protocol.AppServer;
using DotCraft.Plugins;
using DotCraft.Tools;
using Microsoft.Extensions.AI;

namespace DotCraft.AppServer;

/// <summary>
/// Routes runtime dynamic tool calls to the AppServer client bound to the current thread.
/// </summary>
public sealed class WireDynamicToolProxy : IToolSource, IThreadScopedToolSource, IThreadForkToolBindingSource
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<string, DynamicToolThreadBinding> _byThread = new();
    private long _generation;

    /// <inheritdoc />
    public string SourceId => "runtime-dynamic";

    public int Priority => 90;

    public void BindThread(
        string threadId,
        IAppServerTransport transport,
        AppServerConnection connection,
        IReadOnlyList<ThreadDynamicToolDeclaration> declarations)
    {
        var binding = new DynamicToolThreadBinding(
            threadId, transport, connection, declarations, Interlocked.Increment(ref _generation));
        _byThread[threadId] = binding;
        if (connection.IsClosed)
            _byThread.TryRemove(KeyValuePair.Create(threadId, binding));
    }

    internal bool OwnsThread(string threadId, AppServerConnection connection) =>
        _byThread.TryGetValue(threadId, out var binding) && ReferenceEquals(binding.Connection, connection);

    private bool IsCurrentBinding(DynamicToolThreadBinding binding) =>
        !binding.Connection.IsClosed
        && _byThread.TryGetValue(binding.ThreadId, out var current)
        && ReferenceEquals(current, binding);

    bool IThreadForkToolBindingSource.TryForkThreadBinding(string parentThreadId, string childThreadId)
    {
        if (!_byThread.TryGetValue(parentThreadId, out var parent)
            || parent.Connection.IsClosed)
        {
            return false;
        }

        BindThread(childThreadId, parent.Transport, parent.Connection, parent.Declarations);
        return true;
    }

    public static bool TryValidateSpecs(
        IReadOnlyList<RuntimeDynamicToolDeclarationSpec>? tools,
        out string message)
    {
        message = string.Empty;
        if (tools is not { Count: > 0 })
            return true;

        var namespaces = new HashSet<string>(StringComparer.Ordinal);
        var qualifiedNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var declaration in tools)
        {
            if (!PluginManifestParser.IsValidFunctionName(declaration.Name))
            {
                message = "dynamicTools[].name is required and must be a valid model-visible function name.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(declaration.Description))
            {
                message = $"Dynamic declaration '{declaration.Name}' must declare a description.";
                return false;
            }

            switch (declaration)
            {
                case RuntimeDynamicToolFunctionSpec function:
                    if (function.DeferLoading == true)
                    {
                        message = $"Top-level Dynamic Function '{function.Name}' cannot set deferLoading=true.";
                        return false;
                    }
                    if (!TryValidateFunction(function, null, qualifiedNames, out message))
                        return false;
                    break;
                case RuntimeDynamicToolNamespaceSpec toolNamespace:
                    if (!namespaces.Add(toolNamespace.Name))
                    {
                        message = $"Dynamic namespace '{toolNamespace.Name}' is declared more than once.";
                        return false;
                    }
                    if (toolNamespace.Tools.Count == 0)
                    {
                        message = $"Dynamic namespace '{toolNamespace.Name}' must contain at least one Function.";
                        return false;
                    }
                    foreach (var child in toolNamespace.Tools)
                    {
                        if (child is not RuntimeDynamicToolFunctionSpec childFunction)
                        {
                            message = $"Dynamic namespace '{toolNamespace.Name}' may contain Functions only.";
                            return false;
                        }
                        if (!TryValidateFunction(childFunction, toolNamespace.Name, qualifiedNames, out message))
                            return false;
                    }
                    break;
                default:
                    message = $"Dynamic declaration '{declaration.Name}' has an unsupported type.";
                    return false;
            }
        }

        return true;
    }

    private static bool TryValidateFunction(
        RuntimeDynamicToolFunctionSpec tool,
        string? toolNamespace,
        HashSet<string> qualifiedNames,
        out string message)
    {
        if (!PluginManifestParser.IsValidFunctionName(tool.Name))
        {
            message = "Dynamic Function name is required and must be a valid model-visible function name.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(tool.Description))
        {
            message = $"Dynamic Function '{tool.Name}' must declare a description.";
            return false;
        }

        if (tool.InputSchema == null)
        {
            message = $"Dynamic Function '{tool.Name}' must declare inputSchema.";
            return false;
        }

        if (!PluginFunctionSchemaValidator.TryValidateSchema(tool.InputSchema, out var schemaError))
        {
            message = $"Dynamic Function '{tool.Name}' has an invalid inputSchema: {schemaError}";
            return false;
        }

        var qualifiedName = $"{toolNamespace ?? string.Empty}\u001f{tool.Name}";
        if (!qualifiedNames.Add(qualifiedName))
        {
            message = $"Dynamic Function '{tool.Name}' is declared more than once in namespace '{toolNamespace}'.";
            return false;
        }

        if (tool.Approval is { } approval && !TryValidateApprovalDescriptor(tool.InputSchema, approval, out message))
        {
            message = $"Dynamic Function '{tool.Name}' has an invalid approval descriptor: {message}";
            return false;
        }

        message = string.Empty;
        return true;
    }

    /// <summary>
    /// Removes all thread bindings for a disconnected transport.
    /// </summary>
    public void UnbindTransport(IAppServerTransport transport)
    {
        foreach (var kv in _byThread.ToArray())
        {
            if (ReferenceEquals(kv.Value.Transport, transport))
                _byThread.TryRemove(kv);
        }
    }

    /// <summary>
    /// Removes a single thread binding.
    /// </summary>
    public void UnbindThread(string threadId) => _byThread.TryRemove(threadId, out _);

    /// <inheritdoc />
    public ValueTask ReleaseThreadAsync(
        string threadId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        UnbindThread(threadId);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ToolRegistration>> GetRegistrationsAsync(
        ToolPlanningContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        _byThread.TryGetValue(context.ThreadId, out var binding);
        if (binding is not null && !ThreadDynamicToolDeclaration.Match(binding.Declarations, context.DynamicToolDeclarations))
            binding = null;

        var registrations = context.DynamicToolDeclarations
            .Select(tool => CreateRegistration(context.ThreadId, binding, tool, context.Revision))
            .ToArray();
        return ValueTask.FromResult<IReadOnlyList<ToolRegistration>>(registrations);
    }

    private ToolRegistration CreateRegistration(
        string threadId,
        DynamicToolThreadBinding? binding,
        ThreadDynamicToolDeclaration spec,
        long snapshotRevision)
    {
        var sourceId = $"thread:{threadId}";
        var sourceToolId = new SourceToolId(spec.Namespace is null ? spec.Name : $"{spec.Namespace}/{spec.Name}");
        var definitionId = new ToolDefinitionId(ToolSourceKind.RuntimeDynamic, sourceId, sourceToolId);
        var annotations = spec.Approval is { } approval
            ? new Dictionary<string, JsonElement>(StringComparer.Ordinal) { ["dotcraft/dynamicApproval"] = approval }
            : null;
        var definition = new ToolDefinition(
            definitionId,
            new ToolName(spec.Namespace, spec.Name),
            spec.Description,
            spec.InputSchema,
            annotations: annotations,
            policyHints: new ToolPolicyHints(RequiresApproval: spec.Approval is not null),
            provenance: new ToolProvenance(ToolSourceKind.RuntimeDynamic, sourceId, "thread"),
            namespaceDescription: spec.NamespaceDescription,
            policyScope: ToolPolicyScope.RuntimeManaged);
        var bindingScope = $"dynamic:{threadId}:{binding?.Generation ?? 0}";
        var runtimeBinding = new ToolRuntimeBinding(
            new RuntimeBindingId($"{bindingScope}:{sourceToolId.Value}"),
            definitionId,
            new DynamicToolRuntime(this, binding, spec),
            new DynamicToolBindingLease(this, binding),
            bindingScope,
            snapshotRevision);
        var deferred = spec.DeferLoading && spec.Namespace is not null;
        return new ToolRegistration(
            definition,
            runtimeBinding,
            ToolProjectionShape.DynamicLifecycle,
            deferred ? ToolExposure.Deferred : ToolExposure.Direct,
            ToolInvocationAudience.Model | ToolInvocationAudience.Host,
            deferred
                ? new DeferredToolDescriptor(spec.Namespace!, spec.Description, spec.NamespaceDescription)
                : null);
    }

    private async ValueTask<RuntimeDynamicToolCallResult> InvokeAsync(
        DynamicToolThreadBinding? binding,
        ThreadDynamicToolDeclaration spec,
        ToolInvocationContext execution,
        JsonObject arguments,
        CancellationToken cancellationToken)
    {
        if (binding is null || !IsCurrentBinding(binding))
            return Failed("DynamicToolUnavailable", $"Dynamic tool '{spec.Name}' has no current client connection.");

        try
        {
            var requestParams = new Contract.DynamicToolCallParams
            {
                ThreadId = execution.ThreadId,
                TurnId = execution.TurnId ?? string.Empty,
                CallId = execution.CallId,
                Namespace = spec.Namespace,
                Tool = spec.Name,
                Arguments = JsonSerializer.SerializeToElement(arguments, JsonOptions)
            };
            var response = await binding.Transport.RequestAsync(
                Contract.AppServerRpc.DynamicToolCall,
                requestParams,
                cancellationToken,
                Timeout.InfiniteTimeSpan);

            if (response.Error.HasValue)
                return Failed("DynamicToolProtocolError", response.Error.Value.ToString());

            if (response.Result is null)
                return Failed(
                    "DynamicToolResultInvalid",
                    response.InvalidResult ?? $"Dynamic tool '{spec.Name}' returned no result.");

            var result = ToRuntimeResult(response.Result);

            return TryValidateResult(result, out var resultError)
                ? result
                : Failed("DynamicToolResultInvalid", resultError);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Failed("DynamicToolCancelled", $"Dynamic tool '{spec.Name}' was cancelled before the client responded.");
        }
        catch (OperationCanceledException)
        {
            return Failed("DynamicToolDisconnected", $"Dynamic tool '{spec.Name}' lost its client connection before it responded.");
        }
        catch (Exception ex)
        {
            return Failed("DynamicToolProtocolError", ex.Message);
        }
    }

    private static RuntimeDynamicToolCallResult ToRuntimeResult(Contract.DynamicToolCallResult result) => new()
    {
        Success = result.Success,
        ContentItems = result.ContentItems is { } items
            ? items.Select(item => new RuntimeDynamicToolContentItem
            {
                Type = item.Type,
                Text = item.Text,
                MediaType = item.MediaType,
                Url = item.Url,
                DataBase64 = item.DataBase64
            }).ToList()
            : null,
        StructuredContent = result.StructuredContent is { } structured
            ? JsonNode.Parse(structured.GetRawText())
            : null,
        ErrorCode = result.ErrorCode,
        ErrorMessage = result.ErrorMessage
    };

    internal static IReadOnlyList<ThreadDynamicToolDeclaration> Describe(
        IReadOnlyList<RuntimeDynamicToolDeclarationSpec> declarations) =>
        declarations.SelectMany(declaration => declaration switch
        {
            RuntimeDynamicToolFunctionSpec function => new[] { DescribeFunction(null, null, function) },
            RuntimeDynamicToolNamespaceSpec group => group.Tools
                .Cast<RuntimeDynamicToolFunctionSpec>()
                .Select(function => DescribeFunction(group.Name, group.Description, function)),
            _ => throw new ArgumentOutOfRangeException(nameof(declarations))
        })
            .OrderBy(tool => tool.Namespace, StringComparer.Ordinal)
            .ThenBy(tool => tool.Name, StringComparer.Ordinal)
            .ToArray();

    private static ThreadDynamicToolDeclaration DescribeFunction(
        string? toolNamespace,
        string? namespaceDescription,
        RuntimeDynamicToolFunctionSpec function) =>
        new(toolNamespace, namespaceDescription, function.Name, function.Description,
            JsonSerializer.SerializeToElement(function.InputSchema, JsonOptions),
            function.DeferLoading == true,
            function.Approval is { } approval ? JsonSerializer.SerializeToElement(approval, JsonOptions) : null);

    private static RuntimeDynamicToolCallResult Failed(string code, string message) =>
        new()
        {
            Success = false,
            ErrorCode = code,
            ErrorMessage = message,
            ContentItems = [new RuntimeDynamicToolContentItem { Type = "text", Text = $"{code}: {message}" }]
        };

    private static bool TryValidateResult(RuntimeDynamicToolCallResult result, out string message)
    {
        var hasUsefulText = false;
        foreach (var item in result.ContentItems ?? [])
        {
            if (string.Equals(item.Type, "text", StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(item.Text)
                    || item.MediaType != null
                    || item.Url != null
                    || item.DataBase64 != null)
                {
                    message = "Dynamic text content requires non-empty text and no image fields.";
                    return false;
                }

                hasUsefulText = true;
                continue;
            }

            if (!string.Equals(item.Type, "image", StringComparison.Ordinal))
            {
                message = $"Dynamic content type '{item.Type}' is not supported.";
                return false;
            }

            if (item.Text != null
                || string.IsNullOrWhiteSpace(item.MediaType)
                || !item.MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                || (item.Url is null) == (item.DataBase64 is null))
            {
                message = "Dynamic image content requires mediaType and exactly one of url or dataBase64.";
                return false;
            }

            if (item.Url != null
                && (!Uri.TryCreate(item.Url, UriKind.Absolute, out var uri)
                    || uri.Scheme is not ("https" or "http")))
            {
                message = "Dynamic image url must be an absolute HTTP(S) URL; data URLs are not allowed.";
                return false;
            }

            if (item.DataBase64 != null)
            {
                if (item.DataBase64.Length > 20_000_000)
                {
                    message = "Dynamic image dataBase64 exceeds the size limit.";
                    return false;
                }

                try
                {
                    _ = Convert.FromBase64String(item.DataBase64);
                }
                catch (FormatException)
                {
                    message = "Dynamic image dataBase64 is invalid.";
                    return false;
                }
            }
        }

        if (result.Success && !hasUsefulText)
        {
            message = "A successful Runtime Dynamic Tool result requires a useful text content item.";
            return false;
        }

        if (!result.Success
            && (string.IsNullOrWhiteSpace(result.ErrorCode) || string.IsNullOrWhiteSpace(result.ErrorMessage)))
        {
            message = "A failed Runtime Dynamic Tool result requires errorCode and errorMessage.";
            return false;
        }

        message = string.Empty;
        return true;
    }

    private static bool TryValidateApprovalDescriptor(
        JsonObject schema,
        ChannelToolApprovalSpec approval,
        out string message)
    {
        if (string.IsNullOrWhiteSpace(approval.Kind))
        {
            message = "approval.kind is required.";
            return false;
        }

        if (!approval.Kind.Equals("file", StringComparison.OrdinalIgnoreCase)
            && !approval.Kind.Equals("shell", StringComparison.OrdinalIgnoreCase)
            && !approval.Kind.Equals("remoteResource", StringComparison.OrdinalIgnoreCase))
        {
            message = $"approval.kind '{approval.Kind}' is not supported.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(approval.TargetArgument))
        {
            message = "approval.targetArgument is required.";
            return false;
        }

        if (!TryValidateStringProperty(schema, approval.TargetArgument, out message))
            return false;

        var hasStaticOperation = !string.IsNullOrWhiteSpace(approval.Operation);
        var hasOperationArgument = !string.IsNullOrWhiteSpace(approval.OperationArgument);
        if (hasStaticOperation == hasOperationArgument)
        {
            message = "exactly one of approval.operation or approval.operationArgument must be set.";
            return false;
        }

        if (hasOperationArgument
            && !TryValidateStringProperty(schema, approval.OperationArgument!, out message))
        {
            return false;
        }

        message = string.Empty;
        return true;
    }

    private static bool TryValidateStringProperty(JsonObject schema, string propertyName, out string message)
    {
        if (!string.Equals(schema["type"]?.GetValue<string>(), "object", StringComparison.Ordinal))
        {
            message = "inputSchema.type must be 'object' when approval metadata is declared.";
            return false;
        }

        if (schema["properties"] is not JsonObject properties
            || !properties.TryGetPropertyValue(propertyName, out var propertySchema)
            || propertySchema is not JsonObject propertySchemaObject)
        {
            message = $"approval references unknown property '{propertyName}'.";
            return false;
        }

        if (!string.Equals(propertySchemaObject["type"]?.GetValue<string>(), "string", StringComparison.Ordinal))
        {
            message = $"approval property '{propertyName}' must be declared as a string.";
            return false;
        }

        message = string.Empty;
        return true;
    }

    private sealed record DynamicToolThreadBinding(
        string ThreadId,
        IAppServerTransport Transport,
        AppServerConnection Connection,
        IReadOnlyList<ThreadDynamicToolDeclaration> Declarations,
        long Generation);

    private sealed class DynamicToolRuntime(
        WireDynamicToolProxy proxy,
        DynamicToolThreadBinding? binding,
        ThreadDynamicToolDeclaration spec) : IToolRuntime
    {
        public async ValueTask<ToolExecutionResult> InvokeAsync(
            ToolInvocationContext context,
            JsonObject arguments,
            CancellationToken cancellationToken = default)
        {
            var result = await proxy.InvokeAsync(
                binding,
                spec,
                context,
                arguments,
                cancellationToken).ConfigureAwait(false);
            var content = string.Join(
                Environment.NewLine,
                (result.ContentItems ?? [])
                    .Where(item => string.Equals(item.Type, "text", StringComparison.Ordinal))
                    .Select(item => item.Text)
                    .Where(text => !string.IsNullOrWhiteSpace(text)));
            if (string.IsNullOrWhiteSpace(content) && !result.Success)
                content = result.ErrorMessage ?? "Dynamic tool call failed.";
            var contentItems = ToModelContentItems(result.ContentItems);

            JsonElement? structuredContent = result.StructuredContent is null
                ? null
                : JsonSerializer.Deserialize<JsonElement>(
                    result.StructuredContent.ToJsonString(JsonOptions),
                    JsonOptions);
            var rawResult = JsonSerializer.SerializeToElement(result, JsonOptions);
            if (result.Success)
            {
                return ToolExecutionResult.Succeeded(
                    content,
                    structuredContent,
                    rawSourceResult: rawResult,
                    contentItems: contentItems);
            }

            var stableCode = result.ErrorCode switch
            {
                "DynamicToolUnavailable" => ToolErrorCodes.DynamicDisconnected,
                "DynamicToolDisconnected" => ToolErrorCodes.DynamicDisconnected,
                "DynamicToolCancelled" => ToolErrorCodes.Cancelled,
                "DynamicToolProtocolError" => ToolErrorCodes.DynamicProtocolError,
                "DynamicToolResultInvalid" => ToolErrorCodes.ResultInvalid,
                "InvalidArguments" => ToolErrorCodes.InputInvalid,
                _ => ToolErrorCodes.ExecutionFailed
            };
            return new ToolExecutionResult(
                false,
                content,
                structuredContent,
                rawSourceResult: rawResult,
                error: new ToolError(
                    stableCode,
                    result.ErrorMessage ?? "Dynamic tool call failed.",
                    string.IsNullOrWhiteSpace(result.ErrorCode)
                        ? null
                        : new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                        {
                            ["sourceErrorCode"] = JsonSerializer.SerializeToElement(result.ErrorCode)
                        }),
                contentItems: contentItems);
        }

        private static IReadOnlyList<AIContent>? ToModelContentItems(
            IReadOnlyList<RuntimeDynamicToolContentItem>? items)
        {
            if (items is not { Count: > 0 })
                return null;

            var content = new List<AIContent>(items.Count);
            foreach (var item in items)
            {
                if (string.Equals(item.Type, "text", StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(item.Text))
                {
                    content.Add(new TextContent(item.Text));
                }
                else if (string.Equals(item.Type, "image", StringComparison.Ordinal)
                         && !string.IsNullOrWhiteSpace(item.MediaType))
                {
                    if (!string.IsNullOrWhiteSpace(item.Url))
                    {
                        content.Add(new UriContent(item.Url, item.MediaType));
                    }
                    else if (!string.IsNullOrWhiteSpace(item.DataBase64))
                    {
                        try
                        {
                            content.Add(new DataContent(Convert.FromBase64String(item.DataBase64), item.MediaType));
                        }
                        catch (FormatException)
                        {
                            content.Add(new TextContent("[Invalid dynamic tool image payload]"));
                        }
                    }
                }
            }

            return content.Count == 0 ? null : content;
        }
    }

    private sealed class DynamicToolBindingLease(
        WireDynamicToolProxy proxy,
        DynamicToolThreadBinding? binding) : IToolBindingLease
    {
        public ValueTask<ToolBindingLeaseResult> CheckAsync(
            ToolInvocationContext context,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(binding is not null && proxy.IsCurrentBinding(binding)
                ? ToolBindingLeaseResult.Available
                : new ToolBindingLeaseResult(
                    false,
                    new ToolError(
                        ToolErrorCodes.DynamicDisconnected,
                        "The Runtime Dynamic Tool owner disconnected or its binding generation was replaced.")));
        }
    }
}
