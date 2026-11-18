import { useCallback } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { useComposerPreferencesStore } from '../../stores/composerPreferencesStore'
import { addToast } from '../../stores/toastStore'
import { resolveConcreteApprovalPolicyFromWorkspaceDefault } from '../../utils/workspaceCoreConfig'
import type { VisibleApprovalPolicy } from './ApprovalPolicyPicker'

export function useWelcomeApprovalPolicy(configuredDefault: unknown) {
  const t = useT()
  const explicit = useComposerPreferencesStore((state) => state.newChatApprovalPolicy)
  const setWelcomeApprovalPolicyFromUser = useCallback((policy: VisibleApprovalPolicy): void => {
    void useComposerPreferencesStore.getState().saveNewChatApprovalPolicy(policy).catch((error: unknown) => {
      addToast(t('settings.saveFailed', { error: error instanceof Error ? error.message : String(error) }), 'error')
    })
  }, [t])
  return {
    welcomeApprovalPolicy: explicit ?? resolveConcreteApprovalPolicyFromWorkspaceDefault(configuredDefault),
    welcomeApprovalPolicyDirty: explicit != null,
    setWelcomeApprovalPolicyFromUser
  }
}
