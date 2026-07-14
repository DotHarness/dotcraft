using System.Diagnostics;
using System.Text.Json.Nodes;
using DotCraft.Plugins;

namespace DotCraft.Tools;

/// <summary>Common reusable binding lease implementations.</summary>
public static class ToolBindingLeases
{
    /// <summary>Gets a lease that is always available.</summary>
    public static IToolBindingLease AlwaysAvailable { get; } = new AlwaysAvailableLease();

    private sealed class AlwaysAvailableLease : IToolBindingLease
    {
        public ValueTask<ToolBindingLeaseResult> CheckAsync(
            ToolInvocationContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ToolBindingLeaseResult.Available);
    }
}

/// <summary>
/// Common dispatcher implementing the fixed M1 lookup, authority, schema, policy, hook, approval,
/// lifecycle, runtime, audience-normalization, and terminal-hook pipeline.
/// </summary>
public sealed class ToolDispatcher(
    IToolAuthorityEvaluator? authorityEvaluator = null,
    IToolPolicyEvaluator? policyEvaluator = null,
    IToolDispatchHookRunner? hookRunner = null,
    IToolApprovalEvaluator? approvalEvaluator = null,
    IToolInvocationRecorder? recorder = null,
    IToolResultNormalizer? resultNormalizer = null) : IToolDispatcher
{
    private readonly IToolAuthorityEvaluator _authorityEvaluator =
        authorityEvaluator ?? new AllowAllToolAuthorityEvaluator();
    private readonly IToolPolicyEvaluator _policyEvaluator =
        policyEvaluator ?? new AllowAllToolPolicyEvaluator();
    private readonly IToolDispatchHookRunner _hookRunner = hookRunner ?? new NoopToolDispatchHookRunner();
    private readonly IToolApprovalEvaluator _approvalEvaluator =
        approvalEvaluator ?? new PolicyHintApprovalEvaluator();
    private readonly IToolInvocationRecorder _recorder = recorder ?? new NoopToolInvocationRecorder();
    private readonly IToolResultNormalizer _resultNormalizer =
        resultNormalizer ?? new DefaultToolResultNormalizer();

    /// <inheritdoc />
    public ValueTask<ToolExecutionResult> DispatchProviderCallAsync(
        EffectiveToolSnapshot snapshot,
        string providerCallName,
        JsonObject arguments,
        ToolInvocationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(providerCallName))
            throw new ArgumentException("A provider call name is required.", nameof(providerCallName));

        if (!snapshot.TryResolveProviderCallName(providerCallName, out var toolName))
        {
            return ValueTask.FromResult(ToolExecutionResult.Failed(new ToolError(
                ToolErrorCodes.NotFound,
                $"No tool is registered for provider call name '{providerCallName}'.")));
        }

        return DispatchAsync(snapshot, toolName, arguments, request, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<ToolExecutionResult> DispatchAsync(
        EffectiveToolSnapshot snapshot,
        ToolName toolName,
        JsonObject arguments,
        ToolInvocationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(request);

        if (!snapshot.Registrations.TryGetValue(toolName, out var registration))
        {
            return ToolExecutionResult.Failed(new ToolError(
                ToolErrorCodes.NotFound,
                $"Tool '{toolName}' is not registered in snapshot {snapshot.Revision}."));
        }

        if (request.Audience == ToolInvocationAudience.None
            || (registration.InvocationAudiences & request.Audience) != request.Audience)
        {
            return ToolExecutionResult.Failed(new ToolError(
                ToolErrorCodes.Unauthorized,
                $"Tool '{toolName}' is not authorized for the requested invocation audience."));
        }

        if (request.Audience.HasFlag(ToolInvocationAudience.Model)
            && registration.Exposure == ToolExposure.Hidden)
        {
            return ToolExecutionResult.Failed(new ToolError(
                ToolErrorCodes.Unauthorized,
                $"Tool '{toolName}' is hidden from model invocation."));
        }

        if (registration.Binding.Availability != ToolBindingAvailability.Available)
        {
            return ToolExecutionResult.Failed(new ToolError(
                ToolErrorCodes.Unavailable,
                $"Tool '{toolName}' has no available runtime binding."));
        }

        var invocationContext = new ToolInvocationContext(
            request.ThreadId,
            request.TurnId,
            request.CallId,
            request.Audience,
            toolName,
            registration.Definition.Id,
            registration.Binding.Id,
            snapshot.Revision,
            DateTimeOffset.UtcNow,
            request.Origin);

        ToolBindingLeaseResult lease;
        try
        {
            lease = await registration.Binding.Lease
                .CheckAsync(invocationContext, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Cancelled(toolName);
        }

        if (!lease.IsAvailable)
        {
            return ToolExecutionResult.Failed(
                lease.Error ?? new ToolError(ToolErrorCodes.Unavailable, $"Tool '{toolName}' is unavailable."));
        }

        ToolDispatchDecision decision;
        try
        {
            decision = await _authorityEvaluator
                .CheckAsync(invocationContext, registration, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Cancelled(toolName);
        }

        if (!decision.Allowed)
            return Denied(decision, ToolErrorCodes.Unauthorized, $"Tool '{toolName}' authority was denied.");

        JsonObject inputSchema;
        try
        {
            inputSchema = JsonNode.Parse(registration.Definition.InputSchema.GetRawText())?.AsObject()
                          ?? throw new InvalidOperationException("Input schema was empty.");
        }
        catch (Exception ex)
        {
            return ToolExecutionResult.Failed(new ToolError(
                ToolErrorCodes.InputInvalid,
                $"Tool '{toolName}' has an invalid input schema: {ex.Message}"));
        }

        if (!PluginFunctionSchemaValidator.TryValidateArguments(inputSchema, arguments, out var validationError))
        {
            return ToolExecutionResult.Failed(new ToolError(
                ToolErrorCodes.InputInvalid,
                $"Tool '{toolName}' arguments are invalid: {validationError}"));
        }

        try
        {
            decision = await _policyEvaluator
                .EvaluateAsync(invocationContext, registration, arguments, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Cancelled(toolName);
        }

        if (!decision.Allowed)
            return Denied(decision, ToolErrorCodes.Unauthorized, $"Tool '{toolName}' was denied by policy.");

        try
        {
            decision = await _hookRunner
                .RunPreToolUseAsync(invocationContext, registration, arguments, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Cancelled(toolName);
        }

        if (!decision.Allowed)
            return Denied(decision, ToolErrorCodes.Unauthorized, $"Tool '{toolName}' was denied by PreToolUse.");

        try
        {
            decision = await _approvalEvaluator
                .RequestAsync(invocationContext, registration, arguments, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Cancelled(toolName);
        }

        if (!decision.Allowed)
            return Denied(decision, ToolErrorCodes.ApprovalRejected, $"Tool '{toolName}' approval was rejected.");

        try
        {
            await _recorder.RecordStartedAsync(invocationContext, registration, arguments, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Cancelled(toolName);
        }

        ToolExecutionResult result;
        var stopwatch = Stopwatch.StartNew();
        using var timeoutCts = registration.Binding.Timeout is { } timeout
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : null;
        if (registration.Binding.Timeout is { } configuredTimeout)
            timeoutCts!.CancelAfter(configuredTimeout);
        var runtimeToken = timeoutCts?.Token ?? cancellationToken;
        try
        {
            result = await registration.Binding.Runtime
                .InvokeAsync(invocationContext, arguments, runtimeToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            result = Cancelled(toolName);
        }
        catch (OperationCanceledException) when (timeoutCts?.IsCancellationRequested == true)
        {
            result = ToolExecutionResult.Failed(new ToolError(
                ToolErrorCodes.Timeout,
                $"Tool '{toolName}' exceeded its execution timeout."));
        }
        catch (TimeoutException ex)
        {
            result = ToolExecutionResult.Failed(new ToolError(
                ToolErrorCodes.Timeout,
                $"Tool '{toolName}' timed out: {ex.Message}"));
        }
        catch (Exception ex)
        {
            result = ToolExecutionResult.Failed(new ToolError(
                ToolErrorCodes.ExecutionFailed,
                $"Tool '{toolName}' failed: {ex.Message}"));
        }

        if (result is null)
        {
            result = ToolExecutionResult.Failed(new ToolError(
                ToolErrorCodes.ResultInvalid,
                $"Tool '{toolName}' returned no result contract."));
        }

        try
        {
            result = await _resultNormalizer
                .NormalizeAsync(invocationContext, registration, result, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            result = ToolExecutionResult.Failed(new ToolError(
                ToolErrorCodes.ResultInvalid,
                $"Tool '{toolName}' result normalization failed: {ex.Message}"));
        }

        stopwatch.Stop();
        try
        {
            await _recorder.RecordTerminalAsync(
                    invocationContext,
                    registration,
                    result,
                    stopwatch.Elapsed,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            await _hookRunner.RunTerminalAsync(
                    invocationContext,
                    registration,
                    result,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }

        return result;
    }

    private static ToolExecutionResult Cancelled(ToolName toolName) =>
        ToolExecutionResult.Failed(new ToolError(
            ToolErrorCodes.Cancelled,
            $"Tool '{toolName}' was cancelled by the caller."));

    private static ToolExecutionResult Denied(
        ToolDispatchDecision decision,
        string fallbackCode,
        string fallbackMessage) =>
        ToolExecutionResult.Failed(
            decision.Error ?? new ToolError(fallbackCode, fallbackMessage));
}
