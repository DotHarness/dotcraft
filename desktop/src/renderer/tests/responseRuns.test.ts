import { describe, expect, it } from 'vitest'
import type { ConversationItem, ConversationTurn } from '../types/conversation'
import { placeResponses, responseStartedAt } from '../components/conversation/responseRuns'
import { getTaskWake } from '../components/conversation/turnActivityModel'

const at = (seconds: number): string => new Date(Date.UTC(2026, 0, 1, 12, 0, seconds)).toISOString()
const prompt: ConversationItem = { id: 'prompt', type: 'userMessage', status: 'completed', text: 'Clean up', createdAt: at(0) }
const tool: ConversationItem = { id: 'tool', type: 'toolCall', status: 'completed', toolName: 'Exec', createdAt: at(1) }
const reply: ConversationItem = { id: 'reply', type: 'agentMessage', status: 'completed', phase: 'final', text: 'Done', createdAt: at(2) }
const mailbox: ConversationItem = { id: 'mail', type: 'userMessage', status: 'completed', deliveryMode: 'queued', triggerKind: 'subagentMailbox', triggerLabel: 'helper', triggerRefId: '/root/helper', text: 'SubAgent message from /root/helper' }
const commandEnded: ConversationItem = { id: 'ended', type: 'userMessage', status: 'completed', deliveryMode: 'queued', triggerKind: 'terminal', triggerLabel: 'npm test', triggerStatus: 'completed', triggerRefId: 'call-1' }

function turn(id: string, items: ConversationItem[], startSeconds: number, seconds: number, status: ConversationTurn['status'] = 'completed'): ConversationTurn {
  return { id, threadId: 'thread', status, startedAt: at(startSeconds), completedAt: at(startSeconds + seconds), items }
}

const all = { shown: () => true, separated: () => false }

describe('response placement', () => {
  it('folds turns woken by notifications into the response they continue', () => {
    const asked = turn('asked', [prompt, tool], 0, 10)
    const mailed = turn('mailed', [mailbox, tool], 20, 5)
    const answered = turn('answered', [commandEnded, tool, reply], 30, 8)

    const placements = placeResponses([asked, mailed, answered], all)

    expect(placements.get('asked')).toEqual({ leading: [], absorbed: true })
    expect(placements.get('mailed')).toEqual({ leading: [], absorbed: true })
    expect(placements.get('answered')).toEqual({ leading: [asked, mailed], absorbed: false })
  })

  it('continues a response whose earlier turn already replied', () => {
    const asked = turn('asked', [prompt, tool, reply], 0, 10)
    const later = turn('later', [commandEnded, tool], 60, 4)

    const placements = placeResponses([asked, later], all)

    expect(placements.get('asked')).toEqual({ leading: [], absorbed: true })
    expect(placements.get('later')).toEqual({ leading: [asked], absorbed: false })
  })

  it('starts a new response at a user message, an unfinished turn, or unloaded history', () => {
    const asked = turn('asked', [prompt, tool], 0, 10)
    const askedAgain = turn('again', [{ ...prompt, id: 'prompt-2' }, tool], 20, 5)
    const stopped = turn('stopped', [commandEnded, tool], 30, 5, 'cancelled')
    const woken = turn('woken', [commandEnded, tool], 40, 5)
    const afterGap = turn('gap', [commandEnded, tool], 50, 5)

    const placements = placeResponses([asked, askedAgain, stopped, woken, afterGap], {
      shown: () => true,
      separated: (candidate) => candidate.id === 'gap'
    })

    expect(placements.get('asked')).toEqual({ leading: [], absorbed: false })
    expect(placements.get('stopped')).toEqual({ leading: [askedAgain], absorbed: false })
    expect(placements.get('woken')).toEqual({ leading: [], absorbed: false })
    expect(placements.get('gap')).toEqual({ leading: [], absorbed: false })
  })

  it('starts a response at a handoff even without a user bubble', () => {
    const asked = turn('asked', [prompt, tool], 0, 10)
    const resumed = turn('resumed', [
      commandEnded,
      {
        id: 'handoff', type: 'systemNotice', status: 'completed',
        systemNotice: {
          kind: 'contextHandoff',
          contextHandoff: {
            from: { providerId: 'a', model: 'one' },
            to: { providerId: 'b', model: 'one' }
          }
        }
      },
      reply
    ], 20, 5)

    const placements = placeResponses([asked, resumed], all)
    expect(placements.get('asked')).toEqual({ leading: [], absorbed: false })
    expect(placements.get('resumed')).toEqual({ leading: [], absorbed: false })
  })

  it('keeps a response joined across a turn that is not shown', () => {
    const asked = turn('asked', [prompt, tool], 0, 10)
    const silent = turn('silent', [mailbox], 20, 5)
    const answered = turn('answered', [commandEnded, reply], 30, 8)

    const placements = placeResponses([asked, silent, answered], { shown: (candidate) => candidate.id !== 'silent', separated: () => false })

    expect(placements.has('silent')).toBe(false)
    expect(placements.get('answered')).toEqual({ leading: [asked], absorbed: false })
  })

  it('times a response by the work of its turns, not the waits between them', () => {
    const asked = turn('asked', [prompt, tool], 0, 10)
    const mailed = turn('mailed', [mailbox, tool], 20, 5)

    expect(responseStartedAt([asked, mailed], at(30))).toBe(at(15))
    expect(responseStartedAt([], at(30))).toBe(at(30))
  })

  it.each([
    mailbox,
    { ...mailbox, deliveryMode: 'subagentMailbox', triggerKind: undefined }
  ])('names the sender of a mailbox message that continues a response', (input) => {
    const asked = turn('asked', [prompt, tool], 0, 10)
    const mailed = turn('mailed', [input, tool], 20, 5)

    expect(getTaskWake(mailed)).toEqual({ kind: 'subagent', label: 'helper', taskId: '/root/helper', message: true })
    expect(placeResponses([asked, mailed], all).get('mailed')).toEqual({ leading: [asked], absorbed: false })
  })
})
