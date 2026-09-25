import type { PetVoice, PetVoiceAction } from '../../../shared/desktopPet'
import { normalizeLocale, translate } from '../../../shared/locales'
import { hasVoiceTranscriptionRoute } from '../../../shared/voice'
import { useConversationStore } from '../../stores/conversationStore'
import { addToast } from '../../stores/toastStore'
import { approvalRequestKey } from '../../utils/approvalRequest'
import { interruptTurn } from '../../utils/interruptTurn'
import { submitApprovalDecision } from '../../utils/submitApprovalDecision'
import { isBlockedMicrophonePermission } from '../../voice/microphoneAccess'
import { sessionForThread, useVoiceStore } from '../../voice/voiceStore'
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

export function petVoiceOf(origin: string | undefined): PetVoice | undefined {
  if (!origin) return undefined
  const { recording, finalizing, snapshot, microphonePermission } = useVoiceStore.getState()
  if (recording?.threadId === origin) return 'recording'
  if (finalizing?.threadId === origin) return 'processing'
  const session = sessionForThread(snapshot, origin)
  if (session?.phase === 'retryable') return 'retryable'
  if (session) return 'processing'
  if (recording || finalizing || snapshot.sessions.length >= snapshot.capacity) return undefined
  if (!hasVoiceTranscriptionRoute(snapshot) || isBlockedMicrophonePermission(microphonePermission)) return undefined
  return 'idle'
}

export async function relayPetVoice(origin: string, action: PetVoiceAction): Promise<void> {
  const voice = useVoiceStore.getState()
  if (action === 'start') {
    if (petVoiceOf(origin) === 'idle') await voice.startRecording(origin)
  } else if (action === 'retry') {
    const session = sessionForThread(voice.snapshot, origin)
    if (session?.phase === 'retryable') await voice.retry(session.sessionId)
  } else if (voice.recording?.threadId === origin) {
    await (action === 'stop' ? voice.stopRecording('insert') : voice.abortRecording())
  }
}
