import { describe, expect, it } from 'vitest'
import { emptyHistory, type ChatHistory, type HistoryItem } from './history'
import { todoPlanOf } from './todoCard'
import { buildTranscript, createTranscriptProjector } from './transcript'

let clock = 0

function item(turnId: string, type: string, payload: Record<string, unknown>): HistoryItem {
  clock += 1
  const at = new Date(Date.UTC(2026, 9, 4, 9, 1, clock)).toISOString()
  return { id: `i${clock}`, turnId, type, status: 'completed', payload, createdAt: at, completedAt: at, partial: false }
}

const read = (turnId: string, callId: string): HistoryItem[] => [
  item(turnId, 'toolCall', { toolName: 'ReadFile', source: { kind: 'CoreNative', sourceId: 'core-native' }, presentation: { presentationId: 'core.read-file', options: { operation: 'read' } }, callId, arguments: { path: 'a.ts' } }),
  item(turnId, 'toolResult', { toolName: 'ReadFile', source: { kind: 'CoreNative', sourceId: 'core-native' }, presentation: { presentationId: 'core.read-file', options: { operation: 'read' } }, callId, result: 'ok' }),
]

const commandEnded = (turnId: string): HistoryItem =>
  item(turnId, 'userMessage', { text: 'Background command finished', deliveryMode: 'queued', triggerKind: 'terminal', triggerLabel: 'npm test', triggerRefId: 'term_1', triggerStatus: 'completed' })

function history(items: HistoryItem[], turns: [id: string, startSecond: number, seconds: number][]): ChatHistory {
  const at = (second: number): string => new Date(Date.UTC(2026, 9, 4, 9, 0, second)).toISOString()
  return {
    ...emptyHistory(),
    items,
    turns: turns.map(([id, start, seconds]) => ({ id, status: 'completed', error: null, startedAt: at(start), completedAt: at(start + seconds) })),
  }
}

describe('responses spanning turns', () => {
  it('folds turns woken by notifications into the response they continue', () => {
    const items = [
      item('t1', 'userMessage', { text: 'Clean up' }),
      ...read('t1', 'r1'),
      item('t2', 'userMessage', { text: 'SubAgent message from /root/helper', deliveryMode: 'queued', triggerKind: 'subagentMailbox', triggerLabel: 'helper', triggerRefId: '/root/helper' }),
      ...read('t2', 'r2'),
      commandEnded('t3'),
      ...read('t3', 'r3'),
      item('t3', 'agentMessage', { text: 'Done.', phase: 'final' }),
    ]

    const transcript = buildTranscript(history(items, [['t1', 0, 10], ['t2', 30, 5], ['t3', 60, 20]]))

    expect(transcript.map((entry) => entry.kind)).toEqual(['user', 'activity', 'assistant'])
    const activity = transcript[1] as Extract<(typeof transcript)[number], { kind: 'activity' }>
    expect(activity).toMatchObject({ status: 'worked', origin: null })
    expect(Date.parse(activity.endedAt!) - Date.parse(activity.startedAt!)).toBeGreaterThanOrEqual(15_000)
    expect(activity.children.filter((entry) => entry.kind === 'wake')).toMatchObject([
      { origin: { task: 'terminal', label: 'npm test', status: 'completed' } },
    ])
    expect(activity.children.filter((entry) => entry.kind === 'tool' || entry.kind === 'toolGroup')).toHaveLength(3)
  })

  it('groups changes by path on the final reply and opens the latest changed turn', () => {
    const items = [
      item('t1', 'userMessage', { text: 'Clean up' }),
      ...read('t1', 'r1'),
      item('t1', 'toolCall', { toolName: 'EditFile', callId: 'e1', arguments: { path: 'a.ts' }, source: { kind: 'CoreNative', sourceId: 'core-native' }, presentation: { presentationId: 'core.file-write', options: { operation: 'edit' } } }),
      item('t1', 'toolResult', { toolName: 'EditFile', callId: 'e1', result: 'ok', structuredContent: { kind: 'fileChange', changes: [{ path: 'a.ts', kind: 'update', additions: 2, deletions: 1 }] } }),
      item('t1', 'agentMessage', { text: 'Waiting for the build.', phase: 'commentary' }),
      commandEnded('t2'),
      item('t2', 'agentMessage', { text: 'Checking the output.', phase: 'commentary' }),
      ...read('t2', 'r2'),
      item('t2', 'toolCall', { toolName: 'EditFile', callId: 'e2', arguments: { path: 'b.ts' }, source: { kind: 'CoreNative', sourceId: 'core-native' }, presentation: { presentationId: 'core.file-write', options: { operation: 'edit' } } }),
      item('t2', 'toolResult', { toolName: 'EditFile', callId: 'e2', result: 'ok', structuredContent: { kind: 'fileChange', changes: [
        { path: 'D:\\Repo\\A.ts', kind: 'update', additions: 1, deletions: 2 },
        { path: 'b.ts', kind: 'update', additions: 4, deletions: 0 },
      ] } }),
      item('t2', 'agentMessage', { text: 'Done.', phase: 'final' }),
    ]
    const transcript = buildTranscript(history(items, [['t1', 0, 10], ['t2', 30, 5]]), 'D:\\repo')
    const activity = transcript.find((entry) => entry.kind === 'activity') as Extract<(typeof transcript)[number], { kind: 'activity' }>
    const messages = [...activity.children, ...transcript].filter((entry) => entry.kind === 'assistant')

    expect(messages.filter((entry) => entry.copy)).toMatchObject([{ text: 'Done.', phase: 'final' }])
    expect(messages.filter((entry) => entry.phase === 'commentary')).toMatchObject([{ copy: false, changes: [] }, { copy: false, changes: [] }])
    expect(messages.find((entry) => entry.copy)).toMatchObject({ changes: [{
      turnId: 't2', added: 7, removed: 3, files: [
        { path: 'a.ts', added: 3, removed: 3 },
        { path: 'b.ts', added: 4, removed: 0 },
      ],
    }] })
  })

  it.each([
    { deliveryMode: 'queued', triggerKind: 'subagentMailbox' },
    { deliveryMode: 'subagentMailbox' },
  ])('names the sender of a mailbox message that opens a response without a user message: %j', (delivery) => {
    const transcript = buildTranscript(history([
      item('t1', 'userMessage', { text: 'SubAgent message from /root/helper', triggerLabel: 'helper', triggerRefId: '/root/helper', ...delivery }),
      ...read('t1', 'r1'),
      item('t1', 'agentMessage', { text: 'Done.', phase: 'final' }),
    ], [['t1', 0, 10]]))

    expect(transcript.some((entry) => entry.kind === 'user' || entry.kind === 'wake')).toBe(false)
    expect(transcript.find((entry) => entry.kind === 'activity')).toMatchObject({ origin: { task: 'subagent', label: 'helper', status: null, message: true } })
  })

  it('names a subagent that woke a continuing turn with the row its ending shows mid-turn', () => {
    const ended = (payload: Record<string, unknown>) =>
      item('t2', 'userMessage', { text: 'Subagent reviewer finished', triggerKind: 'subagent', triggerLabel: 'reviewer', triggerRefId: '/root/reviewer', triggerStatus: 'completed', ...payload })
    const build = (wake: HistoryItem) => buildTranscript(history(
      [item('t1', 'userMessage', { text: 'Clean up' }), ...read('t1', 'r1'), wake, item('t2', 'agentMessage', { text: 'Done.', phase: 'final' })],
      [['t1', 0, 10], ['t2', 30, 5]],
    ))
    const process = (transcript: ReturnType<typeof build>) => (transcript[1] as Extract<(typeof transcript)[number], { kind: 'activity' }>).children.at(-1)

    expect(process(build(ended({ deliveryMode: 'queued' })))).toMatchObject({ kind: 'tool', verb: 'agentFinished', subject: 'reviewer' })
    expect(process(build(ended({ deliveryMode: 'queued', triggerStatus: 'failed' })))).toMatchObject({ kind: 'tool', verb: 'agentInterrupted', subject: 'reviewer' })
  })

  it('continues a response whose earlier turn already replied', () => {
    const items = [
      item('t1', 'userMessage', { text: 'Clean up' }),
      ...read('t1', 'r1'),
      item('t1', 'toolCall', { toolName: 'EditFile', callId: 'e1', arguments: { path: 'a.ts' }, source: { kind: 'CoreNative', sourceId: 'core-native' }, presentation: { presentationId: 'core.file-write', options: { operation: 'edit' } } }),
      item('t1', 'toolResult', { toolName: 'EditFile', callId: 'e1', result: 'ok', structuredContent: { kind: 'fileChange', changes: [{ path: 'a.ts', kind: 'update', additions: 2, deletions: 1 }] } }),
      item('t1', 'agentMessage', { text: 'Started the build.', phase: 'final' }),
      commandEnded('t2'),
      ...read('t2', 'r2'),
      item('t2', 'agentMessage', { text: 'The build passed.', phase: 'final' }),
    ]

    const transcript = buildTranscript(history(items, [['t1', 0, 10], ['t2', 30, 5]]))

    expect(transcript.map((entry) => entry.kind)).toEqual(['user', 'activity', 'assistant'])
    const activity = transcript[1] as Extract<(typeof transcript)[number], { kind: 'activity' }>
    expect(activity.children.find((entry) => entry.kind === 'assistant')).toMatchObject({ text: 'Started the build.', copy: false, changes: [] })
    expect(activity.children.filter((entry) => entry.kind === 'wake')).toMatchObject([{ origin: { task: 'terminal', label: 'npm test' } }])
    expect(transcript[2]).toMatchObject({ text: 'The build passed.', copy: true, changes: [{ turnId: 't1', added: 2, removed: 1 }] })
  })

  it.each(['failed', 'cancelled'])('retains earlier successful changes when a continuation is %s', (status) => {
    const edit = (turnId: string, path: string) => item(turnId, 'toolResult', {
      structuredContent: { kind: 'fileChange', changes: [{ path, additions: 1, deletions: 0 }] },
    })
    const chat = history([
      item('t1', 'userMessage', { text: 'Update the files' }),
      edit('t1', 'a.ts'),
      item('t1', 'agentMessage', { text: 'Waiting for verification.', phase: 'final' }),
      commandEnded('t2'),
      edit('t2', 'b.ts'),
      item('t2', 'agentMessage', { text: 'Verification interrupted.', phase: 'final' }),
    ], [['t1', 0, 10], ['t2', 30, 5]])
    chat.turns[1] = { ...chat.turns[1], status }

    const transcript = buildTranscript(chat, null, { hasBackgroundWork: true })

    expect(transcript.filter((entry) => entry.kind === 'changes')).toMatchObject([{
      changes: { turnId: 't1', added: 1, removed: 0, files: [{ path: 'a.ts' }] },
    }])
    expect(transcript.some((entry) => entry.kind === 'assistant' && entry.copy)).toBe(false)
  })

  it('retains the latest response checklist across notification turns until background work ends', () => {
    const todoPlan = todoPlanOf({ todos: [{ id: 'verify', content: 'Verify', activeForm: 'Verifying', status: 'in_progress' }] })
    const presentation = { presentationId: 'core.todo', options: { operation: 'todoWrite' } }
    const opening = [
      item('t1', 'userMessage', { text: 'Clean up' }),
      item('t1', 'toolCall', { toolName: 'TodoWrite', callId: 'todos', presentation }),
      item('t1', 'toolResult', { callId: 'todos', success: true }),
      item('t1', 'toolResult', { structuredContent: { kind: 'fileChange', changes: [{ path: 'a.ts', additions: 1, deletions: 0 }] } }),
      item('t1', 'agentMessage', { text: 'Waiting for a worker.', phase: 'final' }),
    ]
    const wake = [commandEnded('t2'), ...read('t2', 'r2')]
    const projector = createTranscriptProjector()
    const options = { todoPlan, hasBackgroundWork: true }
    const activeTodo = { live: true, todos: [{ id: 'verify', status: 'in_progress', activeForm: 'Verifying' }] }

    const waiting = projector.project(history(opening, [['t1', 0, 10]]), null, options)
    expect(waiting.find((entry) => entry.kind === 'assistant')).toMatchObject({ copy: false, changes: [], todos: activeTodo })

    const resumed = history([...opening, ...wake], [['t1', 0, 10], ['t2', 30, 5]])
    resumed.turns[1] = { ...resumed.turns[1], status: 'inProgress' }
    expect(projector.project(resumed, null, options).find((entry) => entry.kind === 'todos'))
      .toMatchObject({ card: activeTodo })

    const finished = history([...opening, ...wake, item('t2', 'agentMessage', { text: 'Done.', phase: 'final' })], [['t1', 0, 10], ['t2', 30, 5]])
    const waitingFinished = projector.project(finished, null, options)
    expect(waitingFinished.find((entry) => entry.kind === 'activity')).toMatchObject({ children: [] })
    expect(waitingFinished.findLast((entry) => entry.kind === 'assistant'))
      .toMatchObject({ copy: false, changes: [], todos: activeTodo })
    const settled = projector.project(finished, null, { ...options, hasBackgroundWork: false })
    expect((settled.find((entry) => entry.kind === 'activity') as Extract<(typeof settled)[number], { kind: 'activity' }>).children.length).toBeGreaterThan(0)
    const reply = settled.find((entry) => entry.kind === 'assistant')
    expect(reply).toMatchObject({ copy: true, changes: [{ turnId: 't1' }] })
    expect(reply).not.toHaveProperty('todos')

    const other = history([...opening, item('t2', 'userMessage', { text: 'New request' }), ...read('t2', 'r2')], [['t1', 0, 10], ['t2', 30, 5]])
    other.turns[1] = { ...other.turns[1], status: 'inProgress' }
    expect(projector.project(other, null, options).some((entry) => entry.kind === 'todos' || (entry.kind === 'assistant' && entry.todos))).toBe(false)
  })

  it('keeps the earlier turns expanded while the response has no reply', () => {
    const items = [item('t1', 'userMessage', { text: 'Clean up' }), ...read('t1', 'r1'), commandEnded('t2'), ...read('t2', 'r2')]

    const transcript = buildTranscript(history(items, [['t1', 0, 10], ['t2', 30, 5]]))

    expect(transcript.filter((entry) => entry.kind === 'activity')).toMatchObject([{ children: [] }])
    expect(transcript.map((entry) => entry.kind)).toEqual(['user', 'activity', 'tool', 'wake', 'tool'])
  })

  it('keeps a single turn expanded while background work is running and folds once it ends', () => {
    const chat = history([
      item('t1', 'userMessage', { text: 'Verify build' }),
      ...read('t1', 'r1'),
      item('t1', 'agentMessage', { text: 'Subagent is verifying.', phase: 'final' }),
    ], [['t1', 0, 10]])
    const projector = createTranscriptProjector()

    const waiting = projector.project(chat, null, { hasBackgroundWork: true })
    expect(waiting.map((entry) => entry.kind)).toEqual(['user', 'activity', 'tool', 'assistant'])
    expect(waiting[1]).toMatchObject({ children: [] })

    const settled = projector.project(chat, null, { hasBackgroundWork: false })
    expect(settled.map((entry) => entry.kind)).toEqual(['user', 'activity', 'assistant'])
    expect((settled[1] as Extract<(typeof settled)[number], { kind: 'activity' }>).children.map((entry) => entry.kind)).toEqual(['tool'])
  })
})
