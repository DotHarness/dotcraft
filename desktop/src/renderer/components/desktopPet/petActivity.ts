import { translate, type AppLocale } from '../../../shared/locales'
import type { PetDecision, PetLineTone, PetStatus, PetStatusInfo } from '../../../shared/desktopPet'
import { isToolLikeItemType, type ConversationTurn, type ItemType } from '../../types/conversation'
import type { PendingApproval, PendingUserInputRequest } from '../../stores/conversationStore'
import type { FileDiff } from '../../types/toolCall'
import { approvalQuestionKey, approvalRequestKey } from '../../utils/approvalRequest'
import { formatCollapsedToolLabel, getStreamingToolDisplay } from '../../utils/toolCallDisplay'
import { isToolItemLive } from '../../utils/toolCallAggregation'

export const PET_LINE_MAX = 60
export const PET_TITLE_MAX = 48
export const PET_DECISION_OPTION_MAX = 6
const DEFAULT_DECISIONS = ['accept', 'acceptForSession', 'acceptAlways', 'decline', 'cancel'] as const

export function petDecisionOf(approval: PendingApproval, locale: AppLocale): PetDecision {
  const options = approval.options
    ?? DEFAULT_DECISIONS.map((value) => ({ value, label: translate(locale, `approval.option.${value}.label`) }))
  return {
    id: approvalRequestKey(approval),
    question: clipPetText(approval.question ?? translate(locale, approvalQuestionKey(approval.approvalType)), 120),
    operation: clipPetText(approval.operation, 160),
    target: clipPetText(approval.target, 160),
    reason: clipPetText(approval.reason, 240),
    options: options.slice(0, PET_DECISION_OPTION_MAX).map((option) => ({ value: option.value, label: clipPetText(option.label, 80) })),
    declineValue: approval.declineValue ?? 'decline'
  }
}

export interface PetActivityInput {
  locale: AppLocale
  threadId: string | null
  threadTitle: string
  turns: ConversationTurn[]
  turnStatus: 'idle' | 'running' | 'waitingApproval' | 'waitingInput'
  activeTurnId: string | null
  interruptingTurnId: string | null
  /** Turn-bound approval first, then a turn-less one (browser use, UI tools). */
  approval: PendingApproval | null
  pendingUserInput: PendingUserInputRequest | null
  streamingMessage: string
  streamingReasoning: string
  changedFiles: Map<string, FileDiff>
  /** Last finished turn the person has already looked at; a newer one reads as Ready. */
  readTurnId: string | null
}

export function clipPetText(value: string, max: number): string {
  const chars = Array.from(value.replace(/\s+/g, ' ').trim())
  return chars.length > max ? `${chars.slice(0, max - 1).join('')}…` : chars.join('')
}

export function flattenPetLine(source: string, max = PET_LINE_MAX): string {
  const flat = source
    .replace(/```[\s\S]*?```/g, ' ')
    .replace(/`([^`]+)`/g, '$1')
    .replace(/!\[([^\]]*)\]\([^)]*\)/g, '$1')
    .replace(/\[([^\]]+)\]\([^)]*\)/g, '$1')
    .replace(/^\s*>+\s?/gm, '')
    .replace(/^\s*(?:[-*+]|\d+[.)])\s+/gm, '')
    .replace(/^\s*#{1,6}\s+/gm, '')
    .replace(/\s+#+\s*$/gm, '')
    .replace(/\*\*(.+?)\*\*/g, '$1')
    .replace(/__(.+?)__/g, '$1')
    .replace(/~~(.+?)~~/g, '$1')
    .replace(/\*(.+?)\*/g, '$1')
    .replace(/(?<![\w])_(.+?)_(?![\w])/g, '$1')
    .replace(/\s+/g, ' ')
    .trim()
    .replace(/[.?!。！？…]+$/, '')
  return clipPetText(flat, max)
}

function firstLine(source: string): string {
  return source.split(/\r?\n/).map((line) => line.trim()).find((line) => line.length > 0) ?? ''
}

function firstSentence(source: string, max: number): string {
  const line = flattenPetLine(firstLine(source), Number.POSITIVE_INFINITY)
  const sentence = /^(.+?[.!?。！？])(?:\s|$)/.exec(line)
  return flattenPetLine(sentence ? sentence[1] : line, max)
}

function newest(turn: ConversationTurn, type: ItemType, field: 'text' | 'reasoning'): string {
  for (let index = turn.items.length - 1; index >= 0; index -= 1) {
    const item = turn.items[index]
    const value = item.type === type ? item[field] : undefined
    if (value && value.trim()) return value
  }
  return ''
}

function liveToolLine(turn: ConversationTurn, locale: AppLocale): string {
  const live = turn.items.filter((item) => isToolLikeItemType(item.type) && isToolItemLive(item, { turnRunning: true }))
  const item = live[live.length - 1]
  if (!item) return ''
  const preview = item.argumentsPreview || (item.arguments ? JSON.stringify(item.arguments) : '')
  const label = getStreamingToolDisplay(item.toolName ?? '', preview, locale).label
  return live.length > 1 ? `${label} ${translate(locale, 'desktopPet.status.more', { count: live.length - 1 })}` : label
}

function runningLine(input: PetActivityInput, turn: ConversationTurn): string {
  const { locale } = input
  const live = liveToolLine(turn, locale)
  if (live) return flattenPetLine(live)
  if (input.streamingMessage.trim()) return firstSentence(input.streamingMessage, PET_LINE_MAX)
  if (input.streamingReasoning.trim()) return firstSentence(input.streamingReasoning, PET_LINE_MAX)
  for (let index = turn.items.length - 1; index >= 0; index -= 1) {
    const item = turn.items[index]
    if (isToolLikeItemType(item.type)) return flattenPetLine(formatCollapsedToolLabel(item.toolName ?? '', item.arguments, locale))
    if (item.type === 'reasoningContent' && item.reasoning?.trim()) return firstSentence(item.reasoning, PET_LINE_MAX)
    if (item.type === 'agentMessage' && item.text?.trim()) return firstSentence(item.text, PET_LINE_MAX)
  }
  return translate(locale, 'desktopPet.status.thinking')
}

function resolveLine(
  input: PetActivityInput,
  status: PetStatus,
  turn: ConversationTurn | undefined,
  decision: PendingApproval | null
): { line: string; lineTone: PetLineTone } {
  const { locale } = input
  if (decision) {
    const detail = decision.reason.trim() || `${decision.operation} ${decision.target}`.trim() || decision.question?.trim() || ''
    return { line: flattenPetLine(detail) || translate(locale, 'desktopPet.status.needsApproval'), lineTone: 'warning' }
  }
  if (input.turnStatus === 'waitingInput') {
    const question = input.pendingUserInput?.questions[0]?.question ?? ''
    return { line: flattenPetLine(question) || translate(locale, 'desktopPet.status.needsInput'), lineTone: 'warning' }
  }
  if (status === 'waiting') return { line: translate(locale, 'desktopPet.status.needsApproval'), lineTone: 'warning' }
  if (status === 'failed') {
    const error = turn?.error?.trim() || (turn ? newest(turn, 'error', 'text') : '')
    return { line: flattenPetLine(error) || translate(locale, 'desktopPet.status.failedFallback'), lineTone: 'danger' }
  }
  if (status === 'running') {
    return { line: turn ? runningLine(input, turn) : translate(locale, 'desktopPet.status.thinking'), lineTone: 'neutral' }
  }
  if (turn?.status === 'cancelled') return { line: translate(locale, 'desktopPet.status.stopped'), lineTone: 'neutral' }
  if (turn?.status === 'completed') {
    const summary = firstSentence(newest(turn, 'agentMessage', 'text'), PET_LINE_MAX)
    return status === 'review'
      ? { line: summary || translate(locale, 'desktopPet.status.review'), lineTone: 'success' }
      : { line: summary, lineTone: 'neutral' }
  }
  return { line: '', lineTone: 'neutral' }
}

export function derivePetActivity(input: PetActivityInput): PetStatusInfo {
  const title = clipPetText(input.threadTitle, PET_TITLE_MAX)
  if (!input.threadId) return { status: 'idle', title, line: '', lineTone: 'neutral', turnId: '', canStop: false }
  const { turnStatus } = input
  const turn = input.turns[input.turns.length - 1]
  const decision = input.approval && input.approval.locallySubmittedDecision == null ? input.approval : null
  const running = turnStatus === 'running'
  const status: PetStatus = decision || turnStatus === 'waitingApproval' || turnStatus === 'waitingInput'
    ? 'waiting'
    : turn?.status === 'failed'
      ? 'failed'
      : turnStatus === 'idle' && turn?.status === 'completed' && turn.id !== input.readTurnId
        ? 'review'
        : running ? 'running' : 'idle'
  const turnId = input.activeTurnId ?? turn?.id ?? ''
  const stopping = running && !!input.activeTurnId && input.interruptingTurnId === input.activeTurnId
  const canStop = running && !!input.activeTurnId && !input.activeTurnId.startsWith('local-turn-') && !stopping
  const info: PetStatusInfo = { status, title, ...resolveLine(input, status, turn, decision), turnId, canStop }
  if (stopping) info.stopping = true
  if (decision) info.decision = petDecisionOf(decision, input.locale)

  let additions = 0
  let deletions = 0
  let files = 0
  for (const diff of input.changedFiles.values()) {
    if (!turnId || !diff.turnIds?.includes(turnId)) continue
    additions += diff.additions
    deletions += diff.deletions
    files += 1
  }
  if (files > 0) info.patch = { additions, deletions, files }
  return info
}
