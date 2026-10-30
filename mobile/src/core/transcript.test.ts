import { describe, expect, it } from 'vitest'
import { emptyHistory, type ChatHistory, type HistoryItem } from './history'
import { buildTranscript, thinkingStatus, type ToolEntry, type TranscriptEntry } from './transcript'

// The trusted source and presentation the server declares on first-party tools.
const CORE_PRESENTATIONS: Record<string, { presentationId: string; options?: { operation: string } }> = {
  ReadFile: { presentationId: 'core.read-file', options: { operation: 'read' } },
  EditFile: { presentationId: 'core.file-write', options: { operation: 'edit' } },
  CreatePlan: { presentationId: 'core.create-plan' },
  Exec: { presentationId: 'core.shell', options: { operation: 'exec' } },
  SpawnAgent: { presentationId: 'core.subagent', options: { operation: 'spawn' } },
  TaskStop: { presentationId: 'core.task', options: { operation: 'stop' } },
  SendUserMessageAsync: { presentationId: 'core.send-user-message-async' },
}

function core(toolName: string) {
  return { toolName, source: { kind: 'CoreNative', sourceId: 'core-native' }, presentation: CORE_PRESENTATIONS[toolName] }
}

let clock = 0

function item(type: string, payload: Record<string, unknown>, extra: Partial<HistoryItem> = {}): HistoryItem {
  clock += 1
  return {
    id: `i${clock}`,
    turnId: 't1',
    type,
    status: 'completed',
    payload,
    createdAt: `2026-10-04T08:00:${String(clock).padStart(2, '0')}Z`,
    completedAt: `2026-10-04T08:00:${String(clock).padStart(2, '0')}Z`,
    partial: false,
    ...extra,
  }
}

function read(path: string, callId: string): HistoryItem[] {
  return [
    item('toolCall', { ...core('ReadFile'), callId, arguments: { path } }),
    item('toolResult', { ...core('ReadFile'), callId, result: 'ok' }),
  ]
}

function history(items: HistoryItem[], status: string): ChatHistory {
  return {
    ...emptyHistory(),
    items,
    turns: [{ id: 't1', status, error: null, startedAt: '2026-10-04T08:00:00Z', completedAt: status === 'running' ? null : '2026-10-04T08:01:00Z' }],
  }
}

function tools(entries: TranscriptEntry[]): ToolEntry[] {
  return entries.flatMap((entry) =>
    entry.kind === 'tool' ? [entry] : entry.kind === 'toolGroup' || entry.kind === 'activity' ? tools(entry.children) : [],
  )
}

describe('turn layout', () => {
  it('shows neither coordination calls nor Code mode waits, only the calls a script makes', () => {
    const items = [
      item('userMessage', { text: 'Check it' }),
      item('toolCall', { ...core('SendUserMessageAsync'), callId: 'm1', arguments: { message: 'Question?' } }),
      item('toolResult', { ...core('SendUserMessageAsync'), callId: 'm1', result: '{"accepted":true}' }),
      item('toolCall', { toolName: 'CodeModeWait', source: { kind: 'CoreNative', sourceId: 'code-mode' }, callId: 'w1', arguments: { cell_id: 'c1' } }),
      ...read('a.ts', 'r1'),
      item('toolResult', { toolName: 'CodeModeWait', source: { kind: 'CoreNative', sourceId: 'code-mode' }, callId: 'w1', result: 'Script completed' }),
    ]

    expect(tools(buildTranscript(history(items, 'completed'))).map((entry) => entry.path)).toEqual(['a.ts'])
  })

  it('folds a finished turn behind one Worked row, keeps the last plan in view, and offers Copy only on the final reply', () => {
    const items = [
      item('userMessage', { text: 'Plan the release' }),
      item('agentMessage', { text: 'Looking around first.', phase: 'commentary' }),
      ...read('a.ts', 'c1'),
      ...read('b.ts', 'c2'),
      item('toolCall', { ...core('CreatePlan'), callId: 'p1', arguments: { plan: '# Release' } }),
      item('toolResult', { ...core('CreatePlan'), callId: 'p1', result: 'ok', success: true }),
      item('agentMessage', { text: 'Here is the plan.', phase: 'final' }),
    ]
    const transcript = buildTranscript(history(items, 'completed'))
    expect(transcript.map((entry) => entry.kind)).toEqual(['user', 'activity', 'plan', 'assistant'])
    const [, activity, , final] = transcript
    expect(activity).toMatchObject({ status: 'worked', startedAt: '2026-10-04T08:00:00Z' })
    expect(activity.kind === 'activity' && activity.children.map((entry) => entry.kind)).toEqual(['assistant', 'toolGroup'])
    expect(activity.kind === 'activity' && activity.children[1]).toMatchObject({ label: [{ part: 'explored', many: true }], active: false })
    expect(final).toMatchObject({ text: 'Here is the plan.', copy: true })
    expect(activity.kind === 'activity' && activity.children[0]).toMatchObject({ copy: false })
  })

  it('ends a running turn with one active row for its trailing run and one Thinking status that keeps the latest reasoning title', () => {
    const items = [
      item('userMessage', { text: 'Check the build' }),
      item('reasoningContent', { text: '**Reading the config**' }),
      ...read('a.ts', 'c1'),
      ...read('b.ts', 'c2'),
    ]
    const transcript = buildTranscript(history(items, 'running'))
    expect(transcript.map((entry) => entry.kind)).toEqual(['user', 'activity', 'toolGroup', 'thinking'])
    expect(transcript[1]).toMatchObject({ status: 'working', endedAt: null, children: [] })
    expect(transcript[2]).toMatchObject({ active: true, label: [{ part: 'explored', many: true }] })
    expect(transcript[3]).toMatchObject({ heading: 'Reading the config' })

    const calling = buildTranscript(history([...items, item('toolCall', { ...core('ReadFile'), callId: 'c3', arguments: { path: 'c.ts' } })], 'running'))
    expect(calling.at(-1)).toMatchObject({ kind: 'thinking', heading: 'Reading the config' })

    const rethinking = buildTranscript(history([...items, item('reasoningContent', { text: '' }, { status: 'started', completedAt: null })], 'running'))
    expect(rethinking.at(-1)).toMatchObject({ kind: 'thinking', heading: 'Reading the config' })
  })

  it('groups consecutive calls of different kinds into one row naming each kind of work', () => {
    const items = [
      item('userMessage', { text: 'Fix the config' }),
      ...read('a.ts', 'c1'),
      item('toolCall', { ...core('EditFile'), callId: 'e1', arguments: { path: 'a.ts' } }),
      item('toolResult', { ...core('EditFile'), callId: 'e1', result: 'ok', structuredContent: { changes: [{ kind: 'update', additions: 1, deletions: 1 }] } }),
      item('agentMessage', { text: 'Fixed.', phase: 'final' }),
    ]
    const [, activity] = buildTranscript(history(items, 'completed'))
    expect(activity.kind === 'activity' && activity.children[0]).toMatchObject({
      kind: 'toolGroup',
      label: [{ part: 'edited', many: false }, { part: 'explored', many: false }],
    })
  })

  it('leaves background task notices out of the user messages', () => {
    const items = [
      item('userMessage', { text: 'Run the build' }),
      item('userMessage', { text: 'Background command finished (exit code 0): npm test', deliveryMode: 'queued', triggerKind: 'terminal', triggerRefId: 'term-1' }),
      item('userMessage', { text: 'Subagent reviewer finished', deliveryMode: 'queued', triggerKind: 'subagent', triggerRefId: 'agent-1' }),
      item('userMessage', { text: 'Workflow review finished.', deliveryMode: 'queued', triggerKind: 'workflow', triggerRefId: 'run-1', triggerStatus: 'succeeded' }),
      item('userMessage', { text: 'Run the review workflow', triggerKind: 'workflow', triggerRefId: 'run-2' }),
      item('agentMessage', { text: 'All done.', phase: 'final' }),
    ]
    const transcript = buildTranscript(history(items, 'completed'))
    expect(transcript.filter((entry) => entry.kind === 'user')).toMatchObject([{ text: 'Run the build' }, { text: 'Run the review workflow' }])
  })

  it('opens a turn woken by a background task with how the task ended', () => {
    const items = [
      item('userMessage', { text: 'Background command finished', deliveryMode: 'queued', triggerKind: 'terminal', triggerLabel: 'npm test', triggerRefId: 'term_1', triggerStatus: 'completed' }),
      item('userMessage', { text: 'Subagent reviewer finished', deliveryMode: 'queued', triggerKind: 'subagent', triggerLabel: 'reviewer', triggerRefId: '/root/reviewer', triggerStatus: 'failed' }),
      item('agentMessage', { text: 'The tests pass.', phase: 'final' }),
    ]
    const transcript = buildTranscript(history(items, 'completed'))
    expect(transcript.map((entry) => entry.kind)).toEqual(['activity', 'assistant'])
    expect(transcript[0]).toMatchObject({ origin: { task: 'terminal', label: 'npm test', status: 'completed' } })

    const guided = [
      item('userMessage', { text: 'Go' }),
      item('userMessage', { text: 'Done', deliveryMode: 'guidance', triggerKind: 'terminal', triggerStatus: 'completed' }),
      item('agentMessage', { text: 'Done.', phase: 'final' }),
    ]
    expect(buildTranscript(history(guided, 'completed'))).toMatchObject([{ kind: 'user' }, { kind: 'activity', origin: null }, { kind: 'assistant' }])
  })

  it('marks where a running turn learned that a subagent ended, and nothing for other drained notices', () => {
    const notice = (triggerKind: string, triggerLabel: string, triggerStatus: string) =>
      item('userMessage', { text: 'Ended', deliveryMode: 'guidance', triggerKind, triggerLabel, triggerRefId: `/root/${triggerLabel}`, triggerStatus })
    const items = [
      item('userMessage', { text: 'Review in parallel' }),
      ...read('a.ts', 'r1'),
      notice('subagent', 'reviewer', 'completed'),
      notice('terminal', 'npm test', 'completed'),
      ...read('b.ts', 'r2'),
      notice('subagent', 'linter', 'failed'),
    ]
    const entries = tools(buildTranscript(history(items, 'running')))
    expect(entries.filter((entry) => entry.icon === 'agent')).toMatchObject([
      { verb: 'agentFinished', subject: 'reviewer', live: false },
      { verb: 'agentInterrupted', subject: 'linter', live: false },
    ])
    expect(entries.map((entry) => entry.verb)).toEqual(['read', 'agentFinished', 'read', 'agentInterrupted'])
  })

  it('names a command that kept running after its result as a background terminal once its turn ended', () => {
    const exec = (status: string) => [
      item('userMessage', { text: 'Start the server' }),
      item('toolCall', { ...core('Exec'), callId: 'c1', arguments: { command: 'npm run dev' } }),
      item('commandExecution', { command: 'npm run dev', status, sessionId: 'term_1', callId: 'c1' }, { status: status === 'inProgress' ? 'started' : 'completed' }),
      item('toolResult', { ...core('Exec'), callId: 'c1', result: 'Process running with session ID term_1', success: true }),
    ]
    expect(tools(buildTranscript(history(exec('inProgress'), 'running')))[0]).toMatchObject({ verb: 'ran', live: true })
    expect(tools(buildTranscript(history(exec('inProgress'), 'completed')))[0]).toMatchObject({ verb: 'backgroundStarted', subject: 'npm run dev', live: false })
    expect(tools(buildTranscript(history(exec('cancelled'), 'completed')))[0]).toMatchObject({ verb: 'backgroundStopped', subject: 'npm run dev' })
    expect(tools(buildTranscript(history(exec('completed'), 'completed')))[0]).toMatchObject({ verb: 'ran' })
  })

  it('names what TaskStop stopped from its result and what a spawn started', () => {
    const items = [
      item('userMessage', { text: 'Stop the reviewer' }),
      item('toolCall', { ...core('SpawnAgent'), callId: 's1', arguments: { taskName: 'reviewer', agentNickname: 'Reviewer', message: 'Review the diff' } }),
      item('toolResult', { ...core('SpawnAgent'), callId: 's1', result: '{"agentPath":"/root/reviewer","status":"running"}', success: true }),
      item('toolCall', { ...core('TaskStop'), callId: 'k1', arguments: { taskId: '/root/reviewer' } }),
      item('toolResult', { ...core('TaskStop'), callId: 'k1', result: '{"taskId":"/root/reviewer","kind":"agent","label":"Reviewer","status":"stopped","lastAgentMessage":"Half done"}', success: true }),
      item('toolCall', { ...core('TaskStop'), callId: 'k2', arguments: { taskId: 'term_9' } }),
    ]
    const [spawn, stopped, stopping] = tools(buildTranscript(history(items, 'running')))
    expect(spawn).toMatchObject({ verb: 'spawned', subject: 'Reviewer', icon: 'agent', details: [{ target: 'Review the diff', output: null }] })
    expect(stopped).toMatchObject({ verb: 'stoppedAgent', subject: 'Reviewer', icon: 'stopped', details: [{ target: 'Half done', output: null }] })
    expect(stopping).toMatchObject({ verb: 'stoppingShell', subject: null, live: true })
  })

  it('marks a stopped turn and drops reasoning that never finished', () => {
    const items = [item('userMessage', { text: 'Go' }), item('reasoningContent', { text: 'Hmm' }, { status: 'started', completedAt: null })]
    expect(buildTranscript(history(items, 'cancelled'))).toMatchObject([{ kind: 'user' }, { kind: 'activity', status: 'stopped' }])
  })
})

function edit(path: string, callId: string): HistoryItem[] {
  const structuredContent = { kind: 'fileChange', changes: [{ path, kind: 'update', additions: 2, deletions: 1 }] }
  return [
    item('toolCall', { ...core('EditFile'), callId, arguments: { path } }),
    item('toolResult', { ...core('EditFile'), callId, result: 'ok', structuredContent }),
  ]
}

describe('turn changes', () => {
  it('ends a completed turn that changed files with its changes on the final reply, outside Worked for', () => {
    const items = [item('userMessage', { text: 'Fix it' }), ...edit('a.ts', 'c1'), ...edit('b.ts', 'c2'), item('agentMessage', { text: 'Fixed.' })]
    const transcript = buildTranscript(history(items, 'completed'))
    expect(transcript.map((entry) => entry.kind)).toEqual(['user', 'activity', 'assistant'])
    expect(transcript[2]).toMatchObject({ copy: true, changes: { turnId: 't1', added: 4, removed: 2 } })
  })

  it('gives a completed turn without a reply its own changes entry, and a stopped or failed turn none', () => {
    const items = [item('userMessage', { text: 'Fix it' }), ...edit('a.ts', 'c1')]
    expect(buildTranscript(history(items, 'completed')).at(-1)).toMatchObject({ kind: 'changes', changes: { turnId: 't1' } })
    for (const status of ['cancelled', 'failed', 'running']) {
      expect(buildTranscript(history(items, status)).some((entry) => entry.kind === 'changes')).toBe(false)
    }
  })

  it('keeps the changes entry of a summarized turn without a reply', () => {
    const items = [item('userMessage', { text: 'Fix it' }), ...edit('a.ts', 'c1')]
    const summarized = history(items, 'completed')
    summarized.turns[0].summary = { itemCount: items.length + 2 }
    const transcript = buildTranscript(summarized)
    expect(transcript.find((entry) => entry.kind === 'activity')).toMatchObject({ pendingTurnId: 't1' })
    expect(transcript.at(-1)).toMatchObject({ kind: 'changes', changes: { turnId: 't1' } })
  })
})

describe('thinking status', () => {
  it('reads the latest heading line, else the first line of the latest written paragraph, without emphasis', () => {
    expect(thinkingStatus('Plan first.\n\n**Checking the lockfile**\n<!-- note -->')).toBe('Checking the lockfile')
    expect(thinkingStatus('## Reading the config\nThe lockfile pins an old version')).toBe('Reading the config')
    expect(thinkingStatus('**Checking** the lockfile next\n\nmore')).toBe('Checking the lockfile next')
    expect(thinkingStatus('The user wants release notes')).toBeNull()
    expect(thinkingStatus('The user wants release notes', true)).toBe('The user wants release notes')
    expect(thinkingStatus('The user wants release notes.\n\nLet me start')).toBe('The user wants release notes.')
    expect(thinkingStatus('The user wants release notes.\n\nLet me run `git log`.\n')).toBe('Let me run git log.')
    expect(thinkingStatus('  ')).toBeNull()
  })
})
