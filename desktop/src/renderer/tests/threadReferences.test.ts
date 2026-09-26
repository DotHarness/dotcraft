import { describe, expect, it } from 'vitest'
import { buildComposerInputParts } from '../utils/composeInputParts'
import { buildComposerHistory } from '../utils/composerHistory'
import { parseUserMessageSegments } from '../components/conversation/parseUserMessageSegments'
import { createOptimisticUserMessage, projectInputParts } from '../utils/inputPresentation'
import { searchThreadMentions } from '../utils/threadReferences'
import type { ConversationTurn } from '../types/conversation'
import type { ThreadSummary } from '../types/thread'

const segments = [
  { type: 'text' as const, value: 'Compare ' },
  { type: 'thread' as const, threadId: 'thread_a', title: 'Fix [login]' },
  { type: 'text' as const, value: ' with ' },
  { type: 'thread' as const, threadId: 'thread_b', title: 'Retry' },
  { type: 'text' as const, value: ' and ' },
  { type: 'thread' as const, threadId: 'thread_a', title: 'Fix [login]' }
]

describe('thread references', () => {
  it('sends each mention inline and every referenced thread once in a threadReferences context', () => {
    const { inputParts } = buildComposerInputParts({ text: '', segments })

    const contexts = inputParts.filter((part) => part.type === 'contextRef')
    expect(contexts).toHaveLength(1)
    const context = contexts[0]!.context
    expect(context.kind).toBe('threadReferences')
    const block = context.kind === 'threadReferences' ? context.text : ''
    expect(JSON.parse(block.split('\n').at(-1)!)).toEqual([{ threadId: 'thread_a' }, { threadId: 'thread_b' }])
    expect(block).toContain('`ReadThread`')
    expect(inputParts.filter((part) => part.type === 'text').map((part) => part.text).join('')).toBe(
      'Compare [@Fix [login\\]](thread://thread_a) with [@Retry](thread://thread_b) and [@Fix [login\\]](thread://thread_a)'
    )
  })

  it('leaves the thread being sent to out of the reference block', () => {
    const referencesTo = (threadId: string, mentions: typeof segments) => buildComposerInputParts({ text: '', threadId, segments: mentions })
      .inputParts.filter((part) => part.type === 'contextRef')

    const [context] = referencesTo('thread_a', segments)
    expect(context?.context.kind === 'threadReferences' && JSON.parse(context.context.text.split('\n').at(-1)!))
      .toEqual([{ threadId: 'thread_b' }])
    expect(referencesTo('thread_a', segments.slice(0, 2))).toEqual([])
  })

  it('shows sent mentions as thread chips and never surfaces the reference block', () => {
    const { inputParts } = buildComposerInputParts({ text: '', segments })
    const projected = projectInputParts(inputParts)

    expect(projected.contexts).toEqual([])
    const text = projected.parts.map((part) => (part.type === 'text' ? part.text : '')).join('')
    expect(parseUserMessageSegments(text).filter((segment) => segment.type === 'threadRef')).toEqual([
      { type: 'threadRef', threadId: 'thread_a', title: 'Fix [login]' },
      { type: 'threadRef', threadId: 'thread_b', title: 'Retry' },
      { type: 'threadRef', threadId: 'thread_a', title: 'Fix [login]' }
    ])
  })

  it('restores the chips from history without the reference block', () => {
    const { inputParts } = buildComposerInputParts({ text: '', segments })
    const turn: ConversationTurn = {
      id: 'turn',
      threadId: 'thread_c',
      status: 'completed',
      startedAt: '2026-09-26T00:00:00Z',
      items: [createOptimisticUserMessage(inputParts, '', 'message')]
    }

    const [entry] = buildComposerHistory([turn], 'thread_c')

    expect(entry!.segments).toEqual(segments)
    expect(entry!.contexts).toEqual([])
  })
})

describe('thread mention search', () => {
  const chat = (id: string, displayName: string, extra: Partial<ThreadSummary> = {}): ThreadSummary => ({
    id,
    displayName,
    status: 'active',
    originChannel: 'dotcraft-desktop',
    createdAt: '2026-09-26T00:00:00Z',
    lastActiveAt: '2026-09-26T00:00:00Z',
    ...extra
  })

  it('lists loaded title matches before content matches, each chat once, never an excluded one', () => {
    const loaded = [chat('thread_billing', 'Billing'), chat('thread_login', 'Fix login'), chat('thread_current', 'Login current')]
    const contentMatches = [
      chat('thread_billing', 'Billing'),
      chat('thread_login', 'Fix login'),
      chat('thread_unloaded', 'Session cookies'),
      chat('thread_current', 'Login current'),
      chat('thread_mentioned', 'Retry budget')
    ]

    expect(searchThreadMentions(loaded, 'login', ['thread_current', 'thread_mentioned'], contentMatches).map((thread) => thread.id))
      .toEqual(['thread_login', 'thread_billing', 'thread_unloaded'])
  })
})
