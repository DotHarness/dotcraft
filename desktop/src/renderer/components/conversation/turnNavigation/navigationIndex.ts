import type { ConversationItem, ConversationTurn } from '../../../types/conversation'
import type { TurnDiff } from '../../../types/turnDiff'
import type { ListedTurnPosition } from '../../../stores/threadHistoryStore'
import { isOptimisticUserMessage } from '../../../stores/optimisticMessages'
import { isVisibleUserMessage } from '../../../utils/visibleUserMessage'
import { stripSystemReminderBlocks } from '../../../utils/systemReminderText'
import { deriveTurnOutputs, type TurnOutput } from './turnOutputs'

const PREVIEW_TEXT_LIMIT = 240

export interface EntryContent {
  userItemId: string
  label: string
  response: string
  automation: boolean
  bookmarkable: boolean
}

export interface TurnNavigationEntry {
  id: string
  /** Stays the same while a Turn goes from placeholder to preview to loaded, so its button keeps focus. */
  key: string
  turnId: string
  /** Null once the Turn is loaded. */
  position: ListedTurnPosition | null
  content: EntryContent | null
  previewFailed: boolean
  outputs: readonly TurnOutput[]
}

export interface ListedTurn {
  id: string
  position: ListedTurnPosition
}

export type TurnPreview =
  | { status: 'failed' }
  | { status: 'loaded'; entries: EntryContent[] }

export function deriveTurnEntries(items: readonly ConversationItem[]): EntryContent[] {
  const entries: EntryContent[] = []
  for (const item of items) {
    if (isVisibleUserMessage(item)) {
      entries.push({
        userItemId: item.id,
        label: stripSystemReminderBlocks(item.text ?? '').trim(),
        response: '',
        automation: item.triggerKind === 'automation',
        bookmarkable: !isOptimisticUserMessage(item)
      })
    } else if (item.type === 'agentMessage' && entries.length > 0) {
      entries[entries.length - 1].response = item.text ?? ''
    }
  }
  return entries
}

function trimPreviewText(text: string): string {
  return text.length > PREVIEW_TEXT_LIMIT ? `${text.slice(0, PREVIEW_TEXT_LIMIT)}…` : text
}

export function derivePreviewEntries(items: readonly ConversationItem[]): EntryContent[] {
  return deriveTurnEntries(items).map((entry) => ({
    ...entry,
    label: trimPreviewText(entry.label),
    response: trimPreviewText(entry.response)
  }))
}

const NO_OUTPUTS: readonly TurnOutput[] = []
const loadedEntriesCache = new WeakMap<ConversationTurn, EntryContent[]>()
const outputsCache = new WeakMap<ConversationTurn, { diff: TurnDiff | undefined; outputs: TurnOutput[] }>()

function loadedTurnEntries(turn: ConversationTurn): EntryContent[] {
  let entries = loadedEntriesCache.get(turn)
  if (!entries) {
    entries = deriveTurnEntries(turn.items)
    loadedEntriesCache.set(turn, entries)
  }
  return entries
}

function loadedTurnOutputs(turn: ConversationTurn, turnDiffs: ReadonlyMap<string, TurnDiff>): TurnOutput[] {
  const diff = turnDiffs.get(turn.id)
  let cached = outputsCache.get(turn)
  if (!cached || cached.diff !== diff) {
    cached = { diff, outputs: deriveTurnOutputs(turn, turnDiffs) }
    outputsCache.set(turn, cached)
  }
  return cached.outputs
}

function entriesFor(
  turnId: string,
  contents: readonly EntryContent[],
  position: ListedTurnPosition | null,
  outputs: readonly TurnOutput[] = NO_OUTPUTS
): TurnNavigationEntry[] {
  return contents.map((content, ordinal) => ({
    id: `${turnId}:${content.userItemId}`,
    key: `${turnId}#${ordinal}`,
    turnId,
    position,
    content,
    previewFailed: false,
    outputs: ordinal === contents.length - 1 ? outputs : NO_OUTPUTS
  }))
}

function loadedEntries(turn: ConversationTurn, turnDiffs: ReadonlyMap<string, TurnDiff>): TurnNavigationEntry[] {
  return entriesFor(turn.id, loadedTurnEntries(turn), null, loadedTurnOutputs(turn, turnDiffs))
}

/**
 * Walks the listing oldest to newest; loaded Turns missing from it are newer than the
 * listing and follow in conversation order.
 */
export function buildNavigationEntries(
  loadedTurns: readonly ConversationTurn[],
  turnDiffs: ReadonlyMap<string, TurnDiff>,
  listing: readonly ListedTurn[] | null,
  previews: ReadonlyMap<string, TurnPreview>
): TurnNavigationEntry[] {
  if (!listing) return loadedTurns.flatMap((turn) => loadedEntries(turn, turnDiffs))
  const loadedById = new Map(loadedTurns.map((turn) => [turn.id, turn]))
  const listedIds = new Set<string>()
  const entries: TurnNavigationEntry[] = []
  for (const listed of listing) {
    listedIds.add(listed.id)
    const loaded = loadedById.get(listed.id)
    if (loaded) {
      entries.push(...loadedEntries(loaded, turnDiffs))
      continue
    }
    const preview = previews.get(listed.id)
    if (preview?.status === 'loaded') {
      entries.push(...entriesFor(listed.id, preview.entries, listed.position))
      continue
    }
    entries.push({
      id: listed.id,
      key: `${listed.id}#0`,
      turnId: listed.id,
      position: listed.position,
      content: null,
      previewFailed: preview?.status === 'failed',
      outputs: NO_OUTPUTS
    })
  }
  for (const turn of loadedTurns) {
    if (!listedIds.has(turn.id)) entries.push(...loadedEntries(turn, turnDiffs))
  }
  return entries
}
