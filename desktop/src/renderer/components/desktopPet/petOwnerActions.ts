import { normalizeLocale, translate } from '../../../shared/locales'
import { useConversationStore } from '../../stores/conversationStore'
import { addToast } from '../../stores/toastStore'
import { approvalRequestKey } from '../../utils/approvalRequest'
import { interruptTurn } from '../../utils/interruptTurn'
import { submitApprovalDecision } from '../../utils/submitApprovalDecision'
import { petDecisionOf } from './petActivity'

const locale = (): ReturnType<typeof normalizeLocale> => normalizeLocale(document.documentElement.lang)
const describe = (error: unknown): string => error instanceof Error ? error.message : String(error)

export async function stopPetTurn(threadId: string | null, turnId: string): Promise<void> {
  if (!threadId) return
  await interruptTurn({
    threadId,
    turnId,
    onError: (error) => addToast(translate(locale(), 'composer.stopFailed', { error: describe(error) }), 'error')
  })
}

/** Resolves a relayed choice against the approval pending right now, or drops it silently. */
export async function decidePetApproval(id: string, value: string): Promise<void> {
  const state = useConversationStore.getState()
  const live = state.pendingApproval ?? state.genericApproval
  if (!live || live.locallySubmittedDecision != null || approvalRequestKey(live) !== id) return
  if (!petDecisionOf(live, locale()).options.some((option) => option.value === value)) return
  try {
    await submitApprovalDecision(live, value)
  } catch (error) {
    addToast(translate(locale(), 'approval.sendFailed', { error: describe(error) }), 'error')
  }
}
