import { useCallback, useEffect, useRef, useState } from 'react'
import { useConversationStore } from '../../stores/conversationStore'
import { useThreadStore } from '../../stores/threadStore'
import { isHiddenThread } from '../../../shared/hiddenThreads'
import { getSpawnedFromThreadId } from '../../utils/subAgentThreads'

const MIN_TURNS_SINCE_OPEN = 5
const MIN_MS_SINCE_OPEN = 10 * 60 * 1000

export type FeedbackRating = 'bad' | 'fine' | 'good'
export type FeedbackSurveyState = { phase: 'ask' } | { phase: 'thanks'; rating: FeedbackRating }

export interface FeedbackSurvey {
  state: FeedbackSurveyState | null
  rate: (rating: FeedbackRating) => void
  dismiss: () => void
}

function isOrdinaryDesktopChat(threadId: string): boolean {
  const { activeThread, threadList } = useThreadStore.getState()
  const thread = activeThread?.id === threadId ? activeThread : threadList.find((entry) => entry.id === threadId)
  return thread != null
    && thread.originChannel?.toLowerCase() === 'dotcraft-desktop'
    && !isHiddenThread(thread)
    && getSpawnedFromThreadId(thread) === null
}

/** Mount once per opened thread: turns and elapsed time count from mount. */
export function useFeedbackSurvey(threadId: string): FeedbackSurvey {
  const turnStatus = useConversationStore((s) => s.turnStatus)
  const activeTurnId = useConversationStore((s) => s.activeTurnId)
  const lastTurnStatus = useConversationStore((s) => s.turns.at(-1)?.status)
  const [state, setState] = useState<FeedbackSurveyState | null>(null)
  const [startedTurns] = useState(() => new Set<string>())
  const [startedTurnCount, setStartedTurnCount] = useState(0)
  const openedAt = useRef(Date.now())
  const settled = useRef(false)

  useEffect(() => {
    if (activeTurnId === null || startedTurns.has(activeTurnId)) return
    startedTurns.add(activeTurnId)
    setStartedTurnCount(startedTurns.size)
  }, [activeTurnId, startedTurns])

  useEffect(() => {
    if (
      settled.current
      || turnStatus !== 'idle'
      || lastTurnStatus !== 'completed'
      || startedTurnCount < MIN_TURNS_SINCE_OPEN
      || Date.now() - openedAt.current < MIN_MS_SINCE_OPEN
      || !isOrdinaryDesktopChat(threadId)
    ) return undefined

    settled.current = true
    let cancelled = false
    let stopWaiting = (): void => {}
    const appear = (): void => {
      void window.api.settings.set({ feedbackSurveyShown: true }).catch(() => {})
      setState({ phase: 'ask' })
    }
    void window.api.settings.get().then((settings) => {
      if (cancelled || settings.feedbackSurveyShown === true) return
      if (!document.hidden) {
        appear()
        return
      }
      const onVisible = (): void => {
        if (document.hidden) return
        stopWaiting()
        appear()
      }
      document.addEventListener('visibilitychange', onVisible)
      stopWaiting = () => document.removeEventListener('visibilitychange', onVisible)
    }).catch(() => {})
    return () => {
      cancelled = true
      stopWaiting()
    }
  }, [lastTurnStatus, startedTurnCount, threadId, turnStatus])

  useEffect(() => {
    if (turnStatus !== 'idle') setState((current) => current?.phase === 'thanks' ? null : current)
  }, [turnStatus])

  const rate = useCallback((rating: FeedbackRating) => setState({ phase: 'thanks', rating }), [])
  const dismiss = useCallback(() => setState(null), [])

  return { state: turnStatus === 'idle' ? state : null, rate, dismiss }
}
