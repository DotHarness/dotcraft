import { useCallback, useRef } from 'react'
import { useT } from '../../../contexts/LocaleContext'
import { useConversationStore } from '../../../stores/conversationStore'
import { useThreadStore } from '../../../stores/threadStore'
import { addToast } from '../../../stores/toastStore'
import { useUIStore } from '../../../stores/uiStore'
import { startTurnWithOptimisticUI } from '../../../utils/startTurn'
import { useConfirmDialog } from '../../ui/ConfirmDialog'
import { openExternalLinkAfterConfirmation } from '../visualizationHost'
import type { GenerativeUiAction } from './generativeUiProtocol'

export function useGenerativeUiHostActions(threadId: string | undefined): (action: GenerativeUiAction) => void {
  const t = useT()
  const confirm = useConfirmDialog()
  const linkPendingRef = useRef(false)
  return useCallback((action: GenerativeUiAction) => {
    if (action.kind === 'sendMessage') {
      const thread = useThreadStore.getState().activeThread
      const conversation = useConversationStore.getState()
      if (!threadId || thread?.id !== threadId || conversation.turnStatus !== 'idle') {
        addToast(t('generativeUi.sendBlocked'), 'info', 3000)
        return
      }
      void startTurnWithOptimisticUI({
        threadId,
        workspacePath: conversation.workspacePath || thread.workspacePath,
        identityWorkspacePath: thread.workspacePath || conversation.workspacePath,
        text: action.text,
        fallbackThreadName: action.text,
        renameThreadFromText: false
      })
    } else if (action.kind === 'prefillComposer') {
      useUIStore.getState().setComposerPrefill(action.text)
    } else if (action.kind === 'copy') {
      void navigator.clipboard.writeText(action.text).then(
        () => addToast(t('toast.copied'), 'success', 2000),
        () => addToast(t('generativeUi.copyFailed'), 'error', 3000)
      )
    } else if (action.kind === 'openUrl' && !linkPendingRef.current) {
      linkPendingRef.current = true
      void openExternalLinkAfterConfirmation(action.url, confirm, t).finally(() => { linkPendingRef.current = false })
    }
  }, [confirm, t, threadId])
}
