import { useEffect, useState } from 'react'
import type { ConversationItem } from '../../types/conversation'
import { isToolLikeItemType } from '../../types/conversation'
import { extractThinkingStatus } from './thinkingStatus'

const STREAMING_MESSAGE_STALL_MS = 2000

export interface TurnThinkingStatusInput {
  items: ConversationItem[]
  /** The block may show the status at all: the active running turn, not sleeping. */
  enabled: boolean
  activeItemId: string | null
  streamingMessage: string
  streamingMessageLastDeltaAt: number | null
  streamingReasoning: string
  waitingForAnswer: boolean
}

export interface TurnThinkingStatus {
  /** The slot keeps its height while the turn runs. */
  reserved: boolean
  visible: boolean
  waitingForAnswer: boolean
  /** Latest readable reasoning title in the turn, or undefined for the generic label. */
  heading?: string
}

export function useTurnThinkingStatus(input: TurnThinkingStatusInput): TurnThinkingStatus {
  const stalled = useStreamingMessageStall({
    enabled: input.enabled
      && input.streamingMessage.trim().length > 0
      && hasActiveStreamingAgentMessage(input.items, input.activeItemId),
    lastDeltaAt: input.streamingMessageLastDeltaAt
  })
  if (!input.enabled) return { reserved: false, visible: false, waitingForAnswer: false }
  if (input.waitingForAnswer) return { reserved: true, visible: true, waitingForAnswer: true }
  return {
    reserved: true,
    visible: !isOtherProgressVisible({ ...input, streamingMessageStalled: stalled }),
    waitingForAnswer: false,
    heading: latestThinkingHeading(input.items, input.activeItemId, input.streamingReasoning)
  }
}

function latestThinkingHeading(
  items: readonly ConversationItem[],
  activeItemId: string | null,
  streamingReasoning: string
): string | undefined {
  for (let index = items.length - 1; index >= 0; index--) {
    const item = items[index]!
    if (item.type !== 'reasoningContent') continue
    const text = item.status === 'streaming' && item.id === activeItemId ? streamingReasoning : item.reasoning ?? ''
    const heading = extractThinkingStatus(text)
    if (heading) return heading
  }
  return undefined
}

function isOtherProgressVisible({
  items,
  activeItemId,
  streamingMessage,
  streamingMessageStalled
}: TurnThinkingStatusInput & { streamingMessageStalled: boolean }): boolean {
  if (isAnswerSettled(items)) return true
  return items.some((item) => {
    if (item.type === 'agentMessage' && item.status === 'streaming' && item.id === activeItemId) {
      return streamingMessage.trim().length > 0 && !streamingMessageStalled
    }
    if (item.type === 'imageGeneration' && (item.imageGenerationStatus === 'inProgress' || item.status === 'started')) {
      return true
    }
    return item.type === 'approvalCard' && item.approvalState === 'pending'
  })
}

function isAnswerSettled(items: ConversationItem[]): boolean {
  for (let index = items.length - 1; index >= 0; index--) {
    const item = items[index]!
    if (item.type === 'agentMessage') return item.status === 'completed' && item.deliveryMode !== 'async'
    if (
      item.type === 'reasoningContent'
      || item.type === 'userMessage'
      || isToolLikeItemType(item.type)
      || item.type === 'webSearch'
      || item.type === 'imageGeneration'
      || item.type === 'approvalCard'
    ) {
      return false
    }
  }
  return false
}

function hasActiveStreamingAgentMessage(items: ConversationItem[], activeItemId: string | null): boolean {
  return items.some((item) => item.type === 'agentMessage' && item.status === 'streaming' && item.id === activeItemId)
}

function useStreamingMessageStall({ enabled, lastDeltaAt }: { enabled: boolean; lastDeltaAt: number | null | undefined }): boolean {
  const [, forceTick] = useState(0)

  useEffect(() => {
    if (!enabled || lastDeltaAt == null) return undefined
    const elapsedMs = Date.now() - lastDeltaAt
    if (elapsedMs >= STREAMING_MESSAGE_STALL_MS) return undefined
    const timeoutId = setTimeout(() => forceTick((value) => value + 1), STREAMING_MESSAGE_STALL_MS - elapsedMs)
    return () => clearTimeout(timeoutId)
  }, [enabled, lastDeltaAt])

  if (!enabled) return false
  if (lastDeltaAt == null) return true
  return Date.now() - lastDeltaAt >= STREAMING_MESSAGE_STALL_MS
}
