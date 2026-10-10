import { afterEach, describe, expect, it } from 'vitest'
import { createStudio } from '../demo/seed'
import { createHarness, waitFor, type Harness } from '../test/harness'
import { chatTasks } from './backgroundTasks'

const harnesses: Harness[] = []

afterEach(() => {
  for (const harness of harnesses.splice(0)) harness.session.dispose()
})

async function openReview(newerRuns = 0) {
  const computer = createStudio(new Date())
  computer.streamDelayMs = 1
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
