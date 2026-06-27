/**
 * Resolves the approval policy to write onto a thread created from the Welcome
 * (new-chat) composer, where the choice is made before the thread exists and is
 * carried in the pending welcome-turn payload.
 *
 * `default` (and unset) means "inherit the workspace default", so it is left
 * unwritten. `prompt` and `autoApprove` are explicit per-thread overrides that must
 * be preserved through the welcome handoff — otherwise an explicit "Ask for approval"
 * would be dropped to `default` and silently inherit a full-access workspace default.
 *
 * Returns the value to write, or `undefined` when nothing should be written.
 */
export function welcomeApprovalPolicyToWrite(
  raw: 'default' | 'prompt' | 'autoApprove' | undefined
): 'prompt' | 'autoApprove' | undefined {
  return raw === 'autoApprove' || raw === 'prompt' ? raw : undefined
}
