import { describe, expect, it } from 'vitest'
import {
  buildNavigationEntries,
  derivePreviewEntries,
  deriveTurnEntries,
  type ListedTurn,
  type TurnNavigationEntry,
  type TurnPreview
} from '../components/conversation/turnNavigation/navigationIndex'
import { groupEntries } from '../components/conversation/turnNavigation/entryGroups'
import { pickAdjacentEntry } from '../components/conversation/turnNavigation/turnJump'
import type { ConversationItem, ConversationTurn } from '../types/conversation'
import type { TurnDiff } from '../types/turnDiff'

const CREATED_AT = '2026-01-01T00:00:00.000Z'

function user(id: string, text: string, extra: Partial<ConversationItem> = {}): ConversationItem {
  return { id, type: 'userMessage', status: 'completed', text, createdAt: CREATED_AT, ...extra }
}

function agent(id: string, text: string): ConversationItem {
  return { id, type: 'agentMessage', status: 'completed', text, createdAt: CREATED_AT }
}

function turn(id: string, items: ConversationItem[], status: ConversationTurn['status'] = 'completed'): ConversationTurn {
  return { id, threadId: 'thread-1', status, startedAt: CREATED_AT, items }
}

function writtenFiles(turnId: string, ...filePaths: string[]): [string, TurnDiff] {
  return [turnId, {
    turnId,
    source: 'history',
    files: filePaths.map((filePath) => ({
      key: `${turnId}::${filePath}`,
      turnId,
      diff: { filePath, additions: 1, deletions: 0, diffHunks: [], status: 'written', isNewFile: true },
      patchText: '',
      truncated: false
    }))
  }]
}

const NO_DIFFS = new Map<string, TurnDiff>()

function listed(...ids: string[]): ListedTurn[] {
  return ids.map((id, index) => ({ id, position: { backwardsCursor: 'page', offset: ids.length - 1 - index } }))
}

function summary(entries: TurnNavigationEntry[]): Array<[string, number | null, string | null]> {
  return entries.map((entry) => [entry.id, entry.position?.offset ?? null, entry.content?.label ?? null])
}

describe('turn navigation index', () => {
  it('gives each visible user message an entry answered by the last reply before the next one', () => {
    const entries = deriveTurnEntries([
      agent('a0', 'before any user message'),
      user('u1', '  First ask  '),
      agent('a1', 'draft'),
      user('g1', 'steer', { deliveryMode: 'guidance' }),
      agent('a2', 'final for first'),
      user('u2', 'Second ask', { triggerKind: 'automation' }),
      user('m1', 'mailbox', { deliveryMode: 'subagentMailbox' }),
      user('m2', 'mailbox trigger', { triggerKind: 'subagentMailbox' }),
      agent('a3', 'final for second'),
      user('u3', '')
    ])

    expect(entries).toEqual([
      { userItemId: 'u1', label: 'First ask', response: 'final for first', automation: false, bookmarkable: true },
      { userItemId: 'u2', label: 'Second ask', response: 'final for second', automation: true, bookmarkable: true },
      { userItemId: 'u3', label: '', response: '', automation: false, bookmarkable: true }
    ])
  })

  it('trims preview labels and responses to 240 characters', () => {
    const [entry] = derivePreviewEntries([user('u1', 'x'.repeat(300)), agent('a1', 'y'.repeat(240))])

    expect(entry.label).toBe(`${'x'.repeat(240)}…`)
    expect(entry.response).toBe('y'.repeat(240))
  })

  it('orders listed turns oldest first with placeholders for unloaded ones and newer live turns last', () => {
    const loaded = [
      turn('t3', [user('t3-u', 'three'), agent('t3-a', 'reply')]),
      turn('t4', [user('t4-a', 'four a'), user('t4-b', 'four b')]),
      turn('t5', [user('t5-u', 'live')])
    ]

    const entries = buildNavigationEntries(loaded, NO_DIFFS, listed('t1', 't2', 't3', 't4'), new Map())

    expect(summary(entries)).toEqual([
      ['t1', 3, null],
      ['t2', 2, null],
      ['t3:t3-u', null, 'three'],
      ['t4:t4-a', null, 'four a'],
      ['t4:t4-b', null, 'four b'],
      ['t5:t5-u', null, 'live']
    ])
  })

  it('replaces a previewed placeholder with its entries and drops a turn with no visible user message', () => {
    const previews = new Map<string, TurnPreview>([
      ['t1', { status: 'loaded', entries: derivePreviewEntries([user('t1-a', 'one a'), user('t1-b', 'one b')]) }],
      ['t2', { status: 'loaded', entries: derivePreviewEntries([agent('t2-a', 'only a reply')]) }],
      ['t3', { status: 'failed' }]
    ])

    const entries = buildNavigationEntries([], NO_DIFFS, listed('t1', 't2', 't3'), previews)

    expect(summary(entries)).toEqual([
      ['t1:t1-a', 2, 'one a'],
      ['t1:t1-b', 2, 'one b'],
      ['t3', 0, null]
    ])
    expect(entries[2].previewFailed).toBe(true)
  })

  it('builds entries from loaded turns alone when there is no listing', () => {
    const loaded = [turn('t1', [user('t1-u', 'one')]), turn('t2', [agent('t2-a', 'reply only')])]

    expect(summary(buildNavigationEntries(loaded, NO_DIFFS, null, new Map()))).toEqual([['t1:t1-u', null, 'one']])
  })

  it('makes an entry bookmarkable once its user message has a server Item id', () => {
    const previews = new Map<string, TurnPreview>([
      ['t2', { status: 'loaded', entries: derivePreviewEntries([user('t2-u', 'previewed')]) }]
    ])
    const loaded = [
      turn('t3', [user('t3-u', 'sent')]),
      turn('local-turn-c1', [user('local-c1', 'sending', { clientUserMessageId: 'c1' })], 'running')
    ]

    const entries = buildNavigationEntries(loaded, NO_DIFFS, listed('t1', 't2', 't3'), previews)

    expect(entries.map((entry) => [entry.id, entry.content?.bookmarkable ?? false])).toEqual([
      ['t1', false],
      ['t2:t2-u', true],
      ['t3:t3-u', true],
      ['local-turn-c1:local-c1', false]
    ])
  })

  it('gives outputs only to the last entry of a completed loaded turn', () => {
    const loaded = [
      turn('t2', [user('t2-a', 'two a'), user('t2-b', 'two b')]),
      turn('t3', [user('t3-u', 'three')], 'running'),
      turn('t4', [user('t4-u', 'four')], 'failed')
    ]
    const diffs = new Map([
      writtenFiles('t1', 'notes.md'),
      writtenFiles('t2', 'notes.md'),
      writtenFiles('t3', 'notes.md'),
      writtenFiles('t4', 'notes.md')
    ])

    const entries = buildNavigationEntries(loaded, diffs, listed('t1', 't2', 't3', 't4'), new Map())

    expect(entries.map((entry) => [entry.id, entry.outputs.length])).toEqual([
      ['t1', 0],
      ['t2:t2-a', 0],
      ['t2:t2-b', 1],
      ['t3:t3-u', 0],
      ['t4:t4-u', 0]
    ])
  })
})

describe('turn navigation groups', () => {
  const keys = (count: number, prefix = 'k'): string[] => Array.from({ length: count }, (_unused, index) => `${prefix}${index}`)

  it('splits entries into groups of 64', () => {
    expect(groupEntries(keys(130)).map((group) => group.entryKeys.length)).toEqual([64, 64, 2])
  })

  it('keeps unchanged entries in their groups when entries are inserted or replaced', () => {
    const before = groupEntries(keys(130))
    const next = keys(130)
    next.splice(10, 1, 'new-a', 'new-b', 'new-c')

    const after = groupEntries(next, before)

    expect(after.map((group) => group.key)).toEqual(before.map((group) => group.key))
    expect(after[0].entryKeys).toContain('new-b')
    expect(after[1].entryKeys).toEqual(before[1].entryKeys)
    expect(after[2].entryKeys).toEqual(before[2].entryKeys)
  })
})

describe('adjacent entry for Alt+Arrow navigation', () => {
  const entries = buildNavigationEntries(
    ['t2', 't3', 't4'].map((id) => turn(id, [user(`${id}-u`, id)])),
    NO_DIFFS,
    listed('t1', 't2', 't3', 't4', 't5'),
    new Map()
  )
  const rendered = (tops: number[]): Array<{ index: number; top: number }> =>
    tops.map((top, offset) => ({ index: offset + 1, top }))

  it('moves above an entry that sits at the viewport top within the tolerance', () => {
    expect(pickAdjacentEntry(entries, rendered([-400, 20, 400]), 0, 'previous')?.id).toBe('t2:t2-u')
    expect(pickAdjacentEntry(entries, rendered([-400, -30, 400]), 0, 'previous')?.id).toBe('t3:t3-u')
  })

  it('moves to the entry after the last one at or above the viewport top', () => {
    expect(pickAdjacentEntry(entries, rendered([-400, 20, 400]), 0, 'next')?.id).toBe('t4:t4-u')
    expect(pickAdjacentEntry(entries, rendered([-400, 30, 400]), 0, 'next')?.id).toBe('t3:t3-u')
  })

  it('reaches unloaded entries beyond the rendered ones', () => {
    expect(pickAdjacentEntry(entries, rendered([0, 400, 800]), 0, 'previous')?.id).toBe('t1')
    expect(pickAdjacentEntry(entries, rendered([-800, -400, 0]), 0, 'next')?.id).toBe('t5')
  })

  it('falls back to the ends when nothing is rendered', () => {
    expect(pickAdjacentEntry(entries, [], 0, 'previous')?.id).toBe('t5')
    expect(pickAdjacentEntry(entries, [], 0, 'next')?.id).toBe('t1')
  })
})
