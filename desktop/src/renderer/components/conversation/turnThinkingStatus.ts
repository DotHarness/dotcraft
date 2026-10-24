import { useEffect, useRef, useState } from 'react'
import type { ConversationItem } from '../../types/conversation'
import { extractThinkingStatus } from './thinkingStatus'

const HOLD_MS = 2000

export interface TurnThinkingStatusInput {
  items: ConversationItem[]
  working: boolean
  activeItemId: string | null
  streamingMessage: string
  streamingReasoning: string
  showsReasoning: boolean
  waitingForAnswer: boolean
}

export type TurnThinkingLabel =
  | { kind: 'thinking'; title?: string }
  | { kind: 'waitingForAnswer' }

interface ReasoningStage {
  id: string
  stage: 'live' | 'hold' | 'done'
}

export function useTurnThinkingLabel(input: TurnThinkingStatusInput): TurnThinkingLabel | null {
  const live = input.working
    ? input.items.find((item) => item.type === 'reasoningContent' && item.status === 'streaming' && item.id === input.activeItemId)
    : undefined
  const [span, setSpan] = useState<ReasoningStage | null>(null)
  if (live && span?.id !== live.id) setSpan({ id: live.id, stage: 'live' })
  if (!live && span?.stage === 'live') setSpan({ id: span.id, stage: 'hold' })
  const startedAt = useRef(0)

  useEffect(() => {
    if (span?.stage === 'live') startedAt.current = Date.now()
    if (span?.stage === 'hold') {
      const timer = setTimeout(() => setSpan({ id: span.id, stage: 'done' }), Math.max(0, HOLD_MS - (Date.now() - startedAt.current)))
      return () => clearTimeout(timer)
    }
    return undefined
  }, [span])

  if (!input.working) return null
  if (input.waitingForAnswer) return { kind: 'waitingForAnswer' }
  const title = latestThinkingTitle(input.items, input.activeItemId, input.streamingReasoning)
  if (span?.stage === 'live' || span?.stage === 'hold') return { kind: 'thinking', title }
  return hasVisibleWork(input) ? null : { kind: 'thinking', title }
}

function latestThinkingTitle(
  items: readonly ConversationItem[],
  activeItemId: string | null,
  streamingReasoning: string
): string | undefined {
  for (let index = items.length - 1; index >= 0; index--) {
    const item = items[index]!
    if (item.type !== 'reasoningContent') continue
    const live = item.status === 'streaming' && item.id === activeItemId
    const title = extractThinkingStatus(live ? streamingReasoning : item.reasoning ?? '', !live)
    if (title) return title
  }
  return undefined
}

function hasVisibleWork({ items, activeItemId, streamingMessage, showsReasoning }: TurnThinkingStatusInput): boolean {
  return items.some((item) => {
    if (item.type === 'reasoningContent') return showsReasoning
    if (item.type !== 'agentMessage') return true
    const text = item.status === 'streaming' && item.id === activeItemId ? streamingMessage : item.text ?? ''
    return text.trim().length > 0
  })
}
