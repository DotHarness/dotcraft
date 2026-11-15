import { afterEach, describe, expect, it } from 'vitest'
import { createStudio } from '../demo/seed'
import { createHarness, waitFor, type Harness } from '../test/harness'
import { chatTasks } from './backgroundTasks'
import { chatKey } from './state'
import { chatTaskTree } from './taskTree'

const harnesses: Harness[] = []
const parent = 'thread_release_review'
const child = 'thread_task_owner'
const grandchild = 'thread_nested_owner'

afterEach(() => {
  for (const harness of harnesses.splice(0)) harness.session.dispose()
})

async function openTree() {
  const computer = createStudio(new Date())
  computer.streamDelayMs = 1
  const { project, thread } = computer.thread(parent)
  const at = new Date().toISOString()
  for (const [id, parentThreadId, nickname, running] of [
    [child, parent, 'Builder', false],
    [grandchild, child, 'Checker', true],
  ] as const) {
    computer.background.agents.push({ childThreadId: id, parentThreadId, nickname, role: 'worker', running, createdAt: at, updatedAt: at })
    project.threads.push({ ...thread, id, source: 'subagent', displayName: nickname, items: [], turns: [], pending: [] })
  }
  computer.background.terminals.push(
    { sessionId: 'term_child', isBackground: true, threadId: child, command: 'npm run dev', status: 'running', startedAt: at, output: '' },
    { sessionId: 'term_grandchild', isBackground: true, threadId: grandchild, command: 'npm run check', status: 'running', startedAt: at, output: '' },
  )
  computer.background.workflows.push({ runId: 'run_grandchild', threadId: grandchild, name: 'Nested check', description: 'Check', status: 'running', createdAt: at, agentCount: 1 })
  const harness = createHarness([computer])
  harnesses.push(harness)
  const state = () => harness.computerState()
  await harness.session.boot()
  await waitFor(() => state().link === 'online' && !state().syncing)
  const key = chatKey(project.id, parent)
  harness.link().openChat(key)
  const tasks = () => chatTaskTree(state(), key)
  await waitFor(() => tasks().running.some((task) => task.id === 'run_grandchild'))
  return { computer, harness, state, key, project, tasks }
}

describe('background task trees', () => {
  it('counts active families through idle Agents without opening descendants', async () => {
    const { computer, harness, state, project, tasks, key } = await openTree()
    expect(state().details[chatKey(project.id, grandchild)]).toBeUndefined()
    expect(tasks().running).toEqual(expect.arrayContaining([
      expect.objectContaining({ id: 'term_child', ownerThreadId: child }),
      expect.objectContaining({ id: grandchild, kind: 'agent', ownerThreadId: child }),
      expect.objectContaining({ id: 'term_grandchild', ownerThreadId: grandchild }),
      expect.objectContaining({ id: 'run_grandchild', ownerThreadId: grandchild }),
    ]))
    expect(tasks().running.find((task) => task.id === child)).toMatchObject({ stop: null, activeTurnId: null })
    expect(tasks().running.filter((task) => task.id === child || [child, grandchild].includes(task.ownerThreadId))).toHaveLength(5)

    await harness.link().stopTask(key, tasks().running.find((task) => task.id === grandchild)!)
    await waitFor(() => tasks().running.some((task) => task.id === grandchild && task.stop === null))
    expect(tasks().running.some((task) => task.id === 'term_grandchild')).toBe(true)

    computer.background.finishTerminal('term_grandchild')
    computer.background.finishWorkflow('run_grandchild')
    await waitFor(() => !tasks().running.some((task) => task.ownerThreadId === grandchild || task.id === grandchild))
    expect(tasks().completed.find((task) => task.id === 'term_grandchild')?.status).toBe('completed')
  })

  it('stops exactly one descendant terminal and routes workflow controls and state to their owner', async () => {
    const { computer, harness, state, project, key, tasks } = await openTree()
    const ownerKey = chatKey(project.id, child)
    harness.link().openChat(ownerKey)
    await waitFor(() => state().details[ownerKey]?.loading === false)
    expect(chatTaskTree(state(), ownerKey).running).toHaveLength(4)
    expect(chatTaskTree(state(), ownerKey).running.some((task) => task.id === 'run_grandchild')).toBe(true)
    harness.link().closeChat(ownerKey)

    await harness.link().stopTask(key, tasks().running.find((task) => task.id === 'term_child')!)
    expect(computer.calls.filter((call) => call.method === 'terminal/stop').map((call) => call.params)).toEqual([{ sessionId: 'term_child' }])
    expect(chatTasks(state().tasks[chatKey(project.id, child)]).completed.find((task) => task.id === 'term_child')?.status).toBe('stopped')
    expect(tasks().running.some((task) => task.id === 'term_grandchild')).toBe(true)

    await harness.link().stopTask(key, tasks().running.find((task) => task.id === 'run_grandchild')!)
    expect(computer.calls.filter((call) => call.method === 'workflow/run/stop').map((call) => call.params)).toEqual([{ threadId: grandchild, runId: 'run_grandchild' }])
    expect(chatTasks(state().tasks[chatKey(project.id, grandchild)]).completed.find((task) => task.id === 'run_grandchild')?.status).toBe('stopped')
  })

  it('reconciles unopened descendants when reopening the parent and reconnecting', async () => {
    const { computer, harness, state, tasks, key } = await openTree()
    harness.link().closeChat(key)
    const terminal = computer.background.terminals.find((entry) => entry.sessionId === 'term_grandchild')!
    Object.assign(terminal, { status: 'completed', completedAt: new Date().toISOString() })
    harness.link().openChat(key)
    await waitFor(() => !tasks().running.some((task) => task.id === 'term_grandchild'))
    expect(tasks().running.some((task) => task.id === 'run_grandchild')).toBe(true)

    computer.dropConnections()
    await waitFor(() => state().link === 'connecting')
    computer.background.finishWorkflow('run_grandchild')
    harness.timers.runAll()
    await waitFor(() => state().link === 'online' && !state().syncing)
    await waitFor(() => !tasks().running.some((task) => task.ownerThreadId === grandchild))
    expect(tasks().running.some((task) => task.id === 'term_child')).toBe(true)
    expect(tasks().completed.find((task) => task.id === 'run_grandchild')?.status).toBe('completed')
  })

  it('discovers graph changes below unopened children and removes closed branches from the parent view', async () => {
    const { computer, state, project, tasks } = await openTree()
    const at = new Date().toISOString()
    const nested = 'thread_later_owner'
    const template = computer.thread(parent).thread
    project.threads.push({ ...template, id: nested, source: 'subagent', items: [], turns: [], pending: [] })
    computer.background.agents.push({ childThreadId: nested, parentThreadId: child, nickname: 'Later', role: 'worker', running: false, createdAt: at, updatedAt: at })
    computer.background.terminals.push({ sessionId: 'term_later', isBackground: true, threadId: nested, command: 'npm run serve', status: 'running', startedAt: at, output: '' })
    computer.background.touchAgent(nested)
    await waitFor(() => tasks().running.some((task) => task.id === 'term_later'))
    expect(state().details[chatKey(project.id, nested)]).toBeUndefined()

    computer.background.agents.find((agent) => agent.childThreadId === child)!.closed = true
    computer.background.touchAgent(child)
    await waitFor(() => !tasks().running.some((task) => [child, grandchild, nested].includes(task.ownerThreadId)))
  })
})
