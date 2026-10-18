import { afterEach, describe, expect, it } from 'vitest'
import { createStudio } from '../demo/seed'
import { createHarness, waitFor, type Harness } from '../test/harness'
import { chatTasks } from './backgroundTasks'

const harnesses: Harness[] = []

afterEach(() => {
  for (const harness of harnesses.splice(0)) harness.session.dispose()
})

async function openReview(newerRuns = 0, prepare: (computer: ReturnType<typeof createStudio>) => void = () => undefined) {
  const computer = createStudio(new Date())
  computer.streamDelayMs = 1
  prepare(computer)
  const review = computer.background.workflows.find((run) => run.runId === 'run_review')!
  for (let index = 0; index < newerRuns; index += 1) {
    computer.background.workflows.push({
      ...review,
      runId: `run_old_${index}`,
      status: 'succeeded',
      createdAt: new Date(Date.parse(review.createdAt) + 1_000 + index).toISOString(),
      completedAt: review.createdAt,
    })
  }
  const harness = createHarness([computer])
  harnesses.push(harness)
  const state = () => harness.computerState()
  await harness.session.boot()
  await waitFor(() => state().link === 'online' && !state().syncing)
  const key = Object.values(state().chats).find((chat) => chat.title === 'Review the release branch')!.key
  harness.link().openChat(key)
  await waitFor(() => Object.keys(state().tasks[key] ?? {}).length === 3)
  const tasks = () => chatTasks(state().tasks[key])
  return { computer, harness, key, tasks }
}

describe('background tasks', () => {
  it('gathers background terminals, agents, and workflow runs of a chat into running and completed', async () => {
    const { tasks } = await openReview()
    expect(tasks().running.map((task) => [task.kind, task.id])).toEqual([
      ['workflow', 'run_review'],
      ['shell', 'term_docs_dev'],
    ])
    expect(tasks().completed.map((task) => [task.kind, task.id, task.status])).toEqual([
      ['workflow', 'run_changelog', 'completed'],
      ['agent', 'thread_link_checker', 'completed'],
      ['shell', 'term_test_watch', 'stopped'],
      ['shell', 'term_docs_build', 'completed'],
    ])
  })

  it('lists a command that outlived its turn and leaves out one that finished inside it', async () => {
    const { tasks } = await openReview(0, (computer) => {
      const threadId = 'thread_release_review'
      const startedAt = new Date().toISOString()
      computer.background.terminals.push(
        { sessionId: 'term_yielded', threadId, command: 'npm run build', status: 'running', startedAt, output: '', turnId: 'turn_earlier', backgroundReason: null },
        { sessionId: 'term_quick', threadId, command: 'git status', status: 'completed', startedAt, completedAt: startedAt, output: '', turnId: 'turn_earlier', backgroundReason: null },
      )
    })
    expect(tasks().running.map((task) => task.id)).toContain('term_yielded')
    expect([...tasks().running, ...tasks().completed].map((task) => task.id)).not.toContain('term_quick')
  })

  it('leaves a running command of the active turn out until the turn ends', () => {
    const shell = [
      { kind: 'shell' as const, id: 'term_1', title: 'npm run dev', status: 'running' as const, startedAt: null, endedAt: null, stop: 'term_1', turnId: 'turn_1', output: null },
    ]
    expect(chatTasks({ shell }, 'turn_1').running).toEqual([])
    expect(chatTasks({ shell }, null).running.map((task) => task.id)).toEqual(['term_1'])
  })

  it('shows a command handed over after its yield time and how each subagent stands', async () => {
    const computer = createStudio(new Date())
    computer.streamDelayMs = 1
    const harness = createHarness([computer])
    harnesses.push(harness)
    const state = () => harness.computerState()
    await harness.session.boot()
    await waitFor(() => state().link === 'online' && !state().syncing)
    const key = Object.values(state().chats).find((chat) => chat.title === 'Check the API against its docs')!.key
    harness.link().openChat(key)
    await waitFor(() => Object.keys(state().tasks[key] ?? {}).length === 3)
    const { running, completed } = chatTasks(state().tasks[key])
    expect(running.map((task) => task.id)).toEqual(expect.arrayContaining(['term_api_dev', 'thread_schema_auditor']))
    expect(completed.map((task) => [task.id, task.status])).toEqual(
      expect.arrayContaining([
        ['thread_docs_reviewer', 'failed'],
        ['thread_changelog_checker', 'stopped'],
        ['term_integration', 'completed'],
      ]),
    )

    const auditor = running.find((task) => task.id === 'thread_schema_auditor')!
    expect(auditor.kind === 'agent' && auditor.tokens).toBe(275_300)
    await harness.link().stopTask(key, auditor)
    await waitFor(() => chatTasks(state().tasks[key]).completed.some((task) => task.id === 'thread_schema_auditor'))
    expect(chatTasks(state().tasks[key]).completed.find((task) => task.id === 'thread_schema_auditor')?.status).toBe('stopped')
    expect(computer.calls.filter((call) => call.method === 'turn/interrupt').map((call) => call.params)).toEqual([
      { threadId: 'thread_schema_auditor', turnId: 'turn_thread_schema_auditor' },
    ])
    expect(computer.calls.some((call) => call.method === 'subagent/close')).toBe(false)
  })

  it('keeps a running workflow that falls past the first page of runs', async () => {
    const { tasks } = await openReview(250)
    expect(tasks().running.map((task) => task.id)).toContain('run_review')
    expect(tasks().completed.filter((task) => task.kind === 'workflow')).toHaveLength(251)
  })

  it('follows terminal and workflow notifications and stops a task through its own method', async () => {
    const { computer, harness, key, tasks } = await openReview()
    computer.background.finishTerminal('term_docs_dev')
    await waitFor(() => tasks().running.length === 1)
    expect(tasks().completed.find((task) => task.id === 'term_docs_dev')?.status).toBe('completed')

    await harness.link().stopTask(key, tasks().running[0])
    await waitFor(() => tasks().running.length === 0)
    expect(tasks().completed.find((task) => task.id === 'run_review')?.status).toBe('stopped')
    expect(computer.calls.filter((call) => call.method === 'workflow/run/stop').map((call) => call.params)).toEqual([
      { threadId: harness.computerState().chats[key].threadId, runId: 'run_review' },
    ])
  })
})
