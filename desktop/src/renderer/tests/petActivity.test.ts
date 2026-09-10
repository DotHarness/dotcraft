import { describe, expect, it } from 'vitest'
import { derivePetActivity, flattenPetLine, type PetActivityInput } from '../components/desktopPet/petActivity'
import type { ConversationItem, ConversationTurn } from '../types/conversation'
import type { PendingApproval } from '../stores/conversationStore'

let counter = 0
function item(partial: Partial<ConversationItem> & { type: ConversationItem['type'] }): ConversationItem {
  counter += 1
  return { id: `item-${counter}`, status: 'completed', createdAt: new Date(counter * 1000).toISOString(), ...partial }
}
function turn(status: ConversationTurn['status'], items: ConversationItem[], extra: Partial<ConversationTurn> = {}): ConversationTurn {
  return { id: 'turn-1', threadId: 't1', status, items, startedAt: '2026-01-01T00:00:00Z', ...extra }
}
const approval: PendingApproval = {
  bridgeId: 'b1', threadId: 't1', turnId: 'turn-1', requestId: 'r1', locallySubmittedDecision: null,
  itemId: 'i1', approvalType: 'shell', operation: 'run', target: 'npm test', reason: ''
}
function input(partial: Partial<PetActivityInput>): PetActivityInput {
  return {
    locale: 'en', threadId: 't1', threadTitle: 'Fix the build', turns: [], turnStatus: 'idle', activeTurnId: null,
    interruptingTurnId: null, approval: null, pendingUserInput: null, streamingMessage: '', streamingReasoning: '',
    changedFiles: new Map(), readTurnId: null, ...partial
  }
}
const running = (items: ConversationItem[], extra: Partial<PetActivityInput> = {}): PetActivityInput =>
  input({ turns: [turn('running', items)], turnStatus: 'running', activeTurnId: 'turn-1', ...extra })

describe('derivePetActivity', () => {
  it('is idle on the welcome surface', () => {
    expect(derivePetActivity(input({ threadId: null }))).toMatchObject({ status: 'idle', line: '', turnId: '', canStop: false })
  })
  it('names the live tool while it runs, from a partial or a complete argument set', () => {
    const streaming = derivePetActivity(running([item({ type: 'toolCall', toolName: 'Exec', status: 'streaming', argumentsPreview: '{"command":"npm test' })]))
    expect(streaming.status).toBe('running')
    expect(streaming.line).toContain('npm test')
    const started = derivePetActivity(running([item({ type: 'toolCall', toolName: 'Exec', status: 'started', arguments: { command: 'npm test' } })]))
    expect(started.line).toContain('npm test')
  })
  it('counts additional live tools', () => {
    const info = derivePetActivity(running([
      item({ type: 'toolCall', toolName: 'ReadFile', status: 'started', arguments: { path: 'src/a.ts' } }),
      item({ type: 'toolCall', toolName: 'ReadFile', status: 'started', arguments: { path: 'src/b.ts' } })
    ]))
    expect(info.line).toContain('b.ts')
    expect(info.line).toContain('+1 more')
  })
  it('falls back to streaming text, then the newest item, then thinking', () => {
    expect(derivePetActivity(running([], { streamingMessage: "I'll read the file. Then edit." })).line).toBe("I'll read the file")
    expect(derivePetActivity(running([], { streamingReasoning: '**Checking the config**\nMore detail' })).line).toBe('Checking the config')
    expect(derivePetActivity(running([item({ type: 'reasoningContent', reasoning: '## Plan\nDo things' })])).line).toBe('Plan')
    expect(derivePetActivity(running([])).line).toBe('Thinking')
  })
  it('puts a pending approval ahead of everything else', () => {
    const info = derivePetActivity(running(
      [item({ type: 'toolCall', toolName: 'Exec', status: 'started', arguments: { command: 'rm -rf x' } })],
      { turnStatus: 'waitingApproval', approval }
    ))
    expect(info).toMatchObject({ status: 'waiting', line: 'run npm test', lineTone: 'warning' })
    expect(info.decision).toMatchObject({ id: 'tool:r1', declineValue: 'decline', operation: 'run', target: 'npm test' })
    expect(info.decision?.options.map((option) => option.value)).toEqual(['accept', 'acceptForSession', 'acceptAlways', 'decline', 'cancel'])
    expect(info.decision?.question.length).toBeGreaterThan(0)
    const submitted = derivePetActivity(input({
      turnStatus: 'waitingApproval', approval: { ...approval, locallySubmittedDecision: 'accept' }, turns: [turn('waitingApproval', [])]
    }))
    expect(submitted).toMatchObject({ status: 'waiting', line: 'Waiting for your approval' })
    expect(submitted.decision).toBeUndefined()
  })
  it('quotes the first user-input question', () => {
    const info = derivePetActivity(input({
      turnStatus: 'waitingInput', turns: [turn('waitingInput', [])],
      pendingUserInput: {
        bridgeId: 'b', threadId: 't1', requestId: 'r', turnId: 'turn-1', isBlocking: true,
        questions: [{ id: 'q', header: 'Port', question: 'Which port?', isOther: false, options: [] }]
      }
    }))
    expect(info).toMatchObject({ status: 'waiting', line: 'Which port', lineTone: 'warning' })
  })
  it('reports a failed turn with its error as danger, unless a decision is pending', () => {
    expect(derivePetActivity(input({ turns: [turn('failed', [], { error: 'spawn ENOENT' })] })))
      .toMatchObject({ status: 'failed', line: 'spawn ENOENT', lineTone: 'danger' })
    expect(derivePetActivity(input({ turns: [turn('failed', [])] })).line).toBe('Something went wrong')
    expect(derivePetActivity(input({ turns: [turn('failed', [])], approval })).status).toBe('waiting')
  })
  it('reads a completed turn as Ready until it has been seen', () => {
    const turns = [turn('completed', [item({ type: 'agentMessage', text: 'Done: **3 files** changed. Next step is tests.' })])]
    expect(derivePetActivity(input({ turns })))
      .toMatchObject({ status: 'review', line: 'Done: 3 files changed', lineTone: 'success', turnId: 'turn-1' })
    expect(derivePetActivity(input({ turns, readTurnId: 'turn-1' })))
      .toMatchObject({ status: 'idle', line: 'Done: 3 files changed', lineTone: 'neutral' })
    expect(derivePetActivity(input({ turns: [turn('completed', [])] })).line).toBe('Ready')
    expect(derivePetActivity(input({ turns: [turn('cancelled', [])] }))).toMatchObject({ status: 'idle', line: 'Stopped' })
  })
  it('only offers Stop for a real, uninterrupted active turn, and reports an interruption in flight', () => {
    expect(derivePetActivity(running([]))).toMatchObject({ canStop: true })
    expect(derivePetActivity(running([]))).not.toHaveProperty('stopping')
    expect(derivePetActivity(running([], { activeTurnId: 'local-turn-1', turns: [turn('running', [], { id: 'local-turn-1' })] })).canStop).toBe(false)
    expect(derivePetActivity(running([], { interruptingTurnId: 'turn-1' }))).toMatchObject({ canStop: false, stopping: true })
  })
  it('carries patch totals for the current turn and a clipped title', () => {
    const diff = { diffHunks: [], status: 'written' as const, isNewFile: false }
    const changedFiles = new Map([
      ['a.ts', { ...diff, filePath: 'a.ts', turnId: 'turn-1', turnIds: ['turn-1'], additions: 7, deletions: 2 }],
      ['b.ts', { ...diff, filePath: 'b.ts', turnId: 'turn-0', turnIds: ['turn-0'], additions: 5, deletions: 5 }],
      ['c.ts', { ...diff, filePath: 'c.ts', turnId: 'turn-1', additions: 1, deletions: 1 } as never]
    ])
    const info = derivePetActivity(running([item({ type: 'agentMessage', text: 'On it.' })], { changedFiles, threadTitle: 'x'.repeat(60) }))
    expect(info.patch).toEqual({ additions: 7, deletions: 2, files: 1 })
    expect(Array.from(info.title)).toHaveLength(48)
    expect(info.title.endsWith('…')).toBe(true)
  })
})

describe('flattenPetLine', () => {
  it('collapses markdown into one plain line', () => {
    expect(flattenPetLine('## Plan\n\n- run `npm i`\n> quoted **bold** and _em_ [link](http://x) ![img](y)'))
      .toBe('Plan run npm i quoted bold and em link img')
  })
  it('drops fenced code and nested emphasis', () => {
    expect(flattenPetLine('Before ```js\nconst a = 1\n``` after **_both_**!')).toBe('Before after both')
  })
  it('keeps identifiers with underscores and clips by code point', () => {
    expect(flattenPetLine('Renamed snake_case_name.')).toBe('Renamed snake_case_name')
    expect(flattenPetLine('好'.repeat(70))).toBe(`${'好'.repeat(59)}…`)
    expect(flattenPetLine('句子结束。')).toBe('句子结束')
  })
})
