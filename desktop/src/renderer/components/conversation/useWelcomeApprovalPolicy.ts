import { useCallback, useState, type RefObject } from 'react'
import type { WelcomeDraft } from '../../stores/uiStore'
import { resolveConcreteApprovalPolicyFromWorkspaceDefault } from '../../utils/workspaceCoreConfig'
import type { VisibleApprovalPolicy } from './ApprovalPolicyPicker'

function explicitPolicy(value: unknown): VisibleApprovalPolicy | null {
  return value === 'prompt' || value === 'autoApprove' ? value : null
}

export function useWelcomeApprovalPolicy(initialDraft: RefObject<WelcomeDraft | null>, workspaceDefault: unknown) {
  const [explicit, setExplicit] = useState(() => explicitPolicy(initialDraft.current?.approvalPolicy))
  const hydrateApprovalPolicy = useCallback((draft: WelcomeDraft): void => {
    const policy = explicitPolicy(draft.approvalPolicy)
    if (policy) setExplicit((current) => current ?? policy)
  }, [])
  return {
    welcomeApprovalPolicy: explicit ?? resolveConcreteApprovalPolicyFromWorkspaceDefault(workspaceDefault),
    welcomeApprovalPolicyDirty: explicit != null,
    setWelcomeApprovalPolicyFromUser: setExplicit,
    hydrateApprovalPolicy
  }
}
