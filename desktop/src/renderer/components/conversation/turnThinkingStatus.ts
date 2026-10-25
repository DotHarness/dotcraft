import type { ConversationItem } from '../../types/conversation'
import { extractThinkingStatus } from './thinkingStatus'

export interface TurnThinkingStatusInput {
  items: ConversationItem[]
  working: boolean
  activeItemId: string | null
  streamingReasoning: string
  waitingForAnswer: boolean
}

export type TurnThinkingLabel =
  | { kind: 'thinking'; title?: string }
  | { kind: 'waitingForAnswer' }

export function turnThinkingLabel(input: TurnThinkingStatusInput): TurnThinkingLabel | null {
  if (!input.working) return null
  if (input.waitingForAnswer) return { kind: 'waitingForAnswer' }
  return {
    kind: 'thinking',
    title: latestThinkingTitle(input.items, input.activeItemId, input.streamingReasoning),
  }
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
