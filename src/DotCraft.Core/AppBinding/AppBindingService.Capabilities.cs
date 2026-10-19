using System.Text.Json;

namespace DotCraft.AppBinding;

public sealed partial class AppBindingService
{
    public AppBindingSnapshot CompleteSync(
        string workspaceCraftPath,
        string bindingId,
        IReadOnlyList<AppBindingToolCapability> tools)
    {
        ValidateCapabilities(tools);
        var orderedTools = tools.OrderBy(tool => tool.SourceToolName, StringComparer.Ordinal).ToList();
        var now = DateTimeOffset.UtcNow;
        return Store(workspaceCraftPath).Update(state =>
        {
            var binding = RequireLiveBinding(state, bindingId);
            var nextRevision = Math.Max(binding.ApprovedCapabilityRevision,
                binding.CandidateCapabilityRevision ?? 0) + 1;
            if (binding.ApprovedCapabilityRevision == 0)
            {
                binding.ApprovedTools = orderedTools;
                binding.ApprovedCapabilityRevision = nextRevision;
                binding.State = AppBindingStates.Active;
                binding.PendingChanges.Clear();
                Audit(state, "capabilities.initial-approved", binding.PrincipalId, binding.AppId,
                    binding.ThreadId, binding.BindingId, binding.AuthorityRevision, nextRevision);
            }
            else
            {
                var changes = AppBindingCapabilityDiffer.FindExpansions(binding.ApprovedTools, orderedTools);
                if (changes.Count == 0)
                {
                    binding.ApprovedTools = orderedTools;
                    binding.ApprovedCapabilityRevision = nextRevision;
                    binding.CandidateTools.Clear();
                    binding.CandidateCapabilityRevision = null;
                    binding.PendingChanges.Clear();
                    binding.State = AppBindingStates.Active;
                    Audit(state, "capabilities.auto-accepted", binding.PrincipalId, binding.AppId,
                        binding.ThreadId, binding.BindingId, binding.AuthorityRevision, nextRevision);
                }
                else
                {
                    binding.CandidateTools = orderedTools;
                    binding.CandidateCapabilityRevision = nextRevision;
                    binding.PendingChanges = changes;
                    binding.State = AppBindingStates.NeedsConfirmation;
                    Audit(state, "capabilities.confirmation-required", binding.PrincipalId, binding.AppId,
                        binding.ThreadId, binding.BindingId, binding.AuthorityRevision, nextRevision);
                }
            }
            binding.FailureReason = null;
            binding.UpdatedAt = now;
            return ToWire(binding);
        });
    }

    public AppBindingSnapshot ConfirmCapabilities(
        string workspaceCraftPath,
        ThreadAppBindingConfirmCapabilitiesCommand parameters,
        string actor)
    {
        var accept = string.Equals(parameters.Decision, "accept", StringComparison.OrdinalIgnoreCase);
        var reject = string.Equals(parameters.Decision, "reject", StringComparison.OrdinalIgnoreCase);
        if (!accept && !reject)
            throw AppBindingErrors.InvalidInput("'decision' must be 'accept' or 'reject'.");
        return Store(workspaceCraftPath).Update(state =>
        {
            var binding = RequireLiveBinding(state, parameters.BindingId);
            if (!string.Equals(binding.ThreadId, parameters.ThreadId, StringComparison.Ordinal)
                || binding.State != AppBindingStates.NeedsConfirmation
                || binding.CandidateCapabilityRevision != parameters.CandidateRevision)
                throw AppBindingErrors.Conflict("The candidate capability revision is stale.");
            if (accept)
            {
                binding.ApprovedTools = binding.CandidateTools;
                binding.ApprovedCapabilityRevision = parameters.CandidateRevision;
            }
            binding.State = AppBindingStates.Active;
            binding.CandidateTools = [];
            binding.CandidateCapabilityRevision = null;
            binding.PendingChanges = [];
            binding.UpdatedAt = DateTimeOffset.UtcNow;
            Audit(state, accept ? "capabilities.accepted" : "capabilities.rejected", actor, binding.AppId,
                binding.ThreadId, binding.BindingId, binding.AuthorityRevision, parameters.CandidateRevision);
            return ToWire(binding);
        });
    }

    private static void ValidateCapabilities(IReadOnlyList<AppBindingToolCapability> tools)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tool in tools)
        {
            if (string.IsNullOrWhiteSpace(tool.SourceToolName) || !names.Add(tool.SourceToolName))
                throw new ArgumentException("Binding tools must have non-empty, unique source names.", nameof(tools));
            if (string.IsNullOrWhiteSpace(tool.Description))
                throw new ArgumentException("Binding tools must have descriptions.", nameof(tools));
            if (tool.InputSchema["type"]?.GetValue<string>() != "object")
                throw new ArgumentException("Binding tool input schemas must describe objects.", nameof(tools));
            if (tool.Visibility.Any(static value => value is not ("model" or "app")))
                throw new ArgumentException("Binding tool visibility must be model or app.", nameof(tools));
            foreach (var key in new[] { "requiresApproval", "readOnly", "destructive", "openWorld" })
                if (tool.Annotations[key] is { } value && value.GetValueKind() is not (JsonValueKind.True or JsonValueKind.False))
                    throw new ArgumentException("Binding risk annotations must be boolean.", nameof(tools));
            if (tool.Ui is { } ui && (!Uri.TryCreate(ui.ResourceUri, UriKind.Absolute, out var uri) || uri.Scheme != "ui"))
                throw new ArgumentException("Binding UI resources must use an absolute ui URI.", nameof(tools));
        }
    }
}
