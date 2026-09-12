using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Tools;

namespace DotCraft.RemoteTools;

internal sealed partial class RemoteToolHostMcpHandlers
{
    private sealed class HostDispatchPolicy(RemoteToolHostMcpHandlers owner, RemoteToolHostState state,
        RemoteToolHubPeer peer, RemoteInvocationMeta invocation, HostInvocationApprovalService.Invocation approval,
        HostWorkspaceRuntime runtime) : IToolPolicyEvaluator, IToolApprovalEvaluator
    {
        public async ValueTask<ToolDispatchDecision> EvaluateAsync(ToolInvocationContext context,
            ToolRegistration registration, JsonObject arguments, CancellationToken cancellationToken = default)
        {
            try
            {
                var native = arguments.ToDictionary(pair => pair.Key, pair => JsonSerializer.SerializeToElement(pair.Value));
                await owner.AuthorizeAsync(registration.Definition.Name.ToString(), native, state, approval,
                    runtime.WorkspacePath, cancellationToken).ConfigureAwait(false);
                return ValidateAuthority();
            }
            catch (RemoteToolHostException exception) { return ToolDispatchDecision.Deny(exception.Code, exception.Message); }
        }

        public async ValueTask<ToolDispatchDecision> RequestAsync(ToolInvocationContext context,
            ToolRegistration registration, JsonObject arguments, CancellationToken cancellationToken = default)
        {
            try
            {
                if (registration.Definition.Id.Kind == ToolSourceKind.PluginNative)
                    await approval.RequestAsync("tool", registration.Definition.Name.ToString(), arguments.ToJsonString())
                        .ConfigureAwait(false);
                var decision = ValidateAuthority();
                if (decision.Allowed && registration.Definition.Name.ToString() == "LSP")
                    await runtime.InitializeLspAsync(cancellationToken).ConfigureAwait(false);
                return decision;
            }
            catch (RemoteToolHostException exception) { return ToolDispatchDecision.Deny(exception.Code, exception.Message); }
        }

        private ToolDispatchDecision ValidateAuthority()
        {
            owner.ValidateLease(invocation.LeaseId, invocation.WorkspaceId);
            var current = owner.RequirePeer(owner.RequireState(), peer.PeerId, invocation.WorkspaceId);
            return current.AuthorizationRevision == peer.AuthorizationRevision
                ? ToolDispatchDecision.Allow
                : ToolDispatchDecision.Deny(RemoteToolErrorCodes.RemotePolicyDenied, "Authorization changed.");
        }
    }
}
