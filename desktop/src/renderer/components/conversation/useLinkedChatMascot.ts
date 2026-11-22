import { useEffect, useMemo, useState } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { useLinkedChats, useLinkedChatsStore } from '../../stores/linkedChatsStore'
import type { LinkedChatStatus, LinkedChatSummary } from '../../../shared/linkedChats'
import type { ComposerMascotInteraction } from './ComposerShell'
import { openThread } from '../sidebar/projectConnectionActions'

const REACTION_MS = 2400
const REPORTED: ReadonlySet<LinkedChatStatus> = new Set(['completed', 'failed', 'stopped', 'waitingForInput'])

const REACTIONS: Partial<Record<LinkedChatStatus, ComposerMascotInteraction>> = {
  completed: { expression: 'happy', light: 'success' },
  failed: { expression: 'operator', light: 'error' }
}

export function useLinkedChatMascot(threadId: string | null): { interaction?: ComposerMascotInteraction; bounce: number } {
  const t = useT()
  const chats = useLinkedChats(threadId)
  const active = chats.length > 0
  const [reaction, setReaction] = useState<ComposerMascotInteraction | null>(null)
  const [bounce, setBounce] = useState(0)
  const [dismissed, setDismissed] = useState<ReadonlySet<string>>(new Set())

  useEffect(() => {
    if (!threadId) return
    let timer = 0
    const unsubscribe = useLinkedChatsStore.subscribe((state, previous) => {
      const earlier = previous.byParent[threadId]
      for (const chat of state.byParent[threadId] ?? []) {
        const before = earlier?.find((candidate) => candidate.threadId === chat.threadId)?.status
        if (!before || before === chat.status || !REPORTED.has(chat.status)) continue
        setBounce((count) => count + 1)
        const next = REACTIONS[chat.status]
        if (!next) continue
        setReaction(next)
        window.clearTimeout(timer)
        timer = window.setTimeout(() => setReaction(null), REACTION_MS)
      }
    })
    return () => { unsubscribe(); window.clearTimeout(timer) }
  }, [threadId])

  const waiting: LinkedChatSummary | undefined = chats
    .find((chat) => chat.status === 'waitingForInput' && !dismissed.has(chat.threadId))

  const interaction = useMemo<ComposerMascotInteraction | undefined>(() => {
    if (!active) return undefined
    if (waiting) {
      return {
        expression: 'operator',
        bubble: {
          tone: 'warning',
          title: t('mascot.linkedChat.needsYou', { project: waiting.projectName }),
          body: waiting.title,
          actions: [
            { label: t('threadActionCard.openChat'), primary: true, onClick: () => void openThread(waiting.threadId, waiting.projectId) },
            { label: t('mascot.action.notNow'), onClick: () => setDismissed((set) => new Set(set).add(waiting.threadId)) }
          ]
        }
      }
    }
    return reaction ?? undefined
  }, [active, waiting, reaction, t])

  return { interaction, bounce: active ? bounce : 0 }
}
