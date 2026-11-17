import { mkdtempSync, rmSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { ThreadTaskConnection } from '../threadTasks/childThreads'
import { DesktopThreadTasks } from '../threadTasks/threadTaskService'
import { ThreadTaskStore } from '../threadTasks/threadTaskStore'

const THREAD = 'thread-parent'
const QUESTION = { turnId: 'turn-1', callId: 'call-1' }
const directories: string[] = []
afterEach(() => directories.splice(0).forEach((directory) => rmSync(directory, { recursive: true, force: true })))

function service(store = new ThreadTaskStore(null)) {
  return new DesktopThreadTasks({ store, getNewTaskPrompt: () => 'ask' })
}

function connection(request: ReturnType<typeof vi.fn>): ThreadTaskConnection {
  return { workspacePath: '/workspace', request: request as ThreadTaskConnection['request'] }
}

describe('waiting question recovery', () => {
  it('clears a recovered question after a newer turn completed while Desktop was offline', async () => {
    const directory = mkdtempSync(join(tmpdir(), 'question-recovery-'))
    directories.push(directory)
    const filePath = join(directory, 'tasks.json')
    new ThreadTaskStore(filePath).setQuestion(THREAD, QUESTION)
    const store = new ThreadTaskStore(filePath)
    const changed = vi.fn()
    store.onChanged(changed)
    const tasks = service(store)
    const request = vi.fn().mockResolvedValue({ data: [{ id: 'turn-2', status: 'completed' }] })

    await tasks.reconcileQuestion(connection(request), THREAD)

    expect(request).toHaveBeenCalledWith('thread/turns/list', {
      threadId: THREAD, limit: 1, sortDirection: 'descending', itemsView: 'none'
    })
    expect(tasks.snapshot(THREAD).question).toBeUndefined()
    expect(changed).toHaveBeenCalledWith({ threadId: THREAD, tasks: [] })
    expect(new ThreadTaskStore(filePath).snapshot(THREAD).question).toBeUndefined()
  })

  it.each(['running', 'completed'])('retains an unanswered question from its own %s turn', async (status) => {
    const store = new ThreadTaskStore(null)
    store.setQuestion(THREAD, QUESTION)
    const request = vi.fn().mockResolvedValue({ data: [{ id: QUESTION.turnId, status }] })

    await service(store).reconcileQuestion(connection(request), THREAD)

    expect(store.snapshot(THREAD).question).toEqual(QUESTION)
  })

  it.each(['failed', 'cancelled'])('clears a question whose own turn is %s', async (status) => {
    const store = new ThreadTaskStore(null)
    store.setQuestion(THREAD, QUESTION)
    const request = vi.fn().mockResolvedValue({ data: [{ id: QUESTION.turnId, status }] })

    await service(store).reconcileQuestion(connection(request), THREAD)

    expect(store.snapshot(THREAD).question).toBeUndefined()
  })

  it.each([
    { name: 'unavailable server', response: () => Promise.reject(new Error('disconnected')) },
    { name: 'empty history', response: async () => ({ data: [] }) }
  ])('retains waiting state with $name', async ({ response }) => {
    const store = new ThreadTaskStore(null)
    store.setQuestion(THREAD, QUESTION)

    await service(store).reconcileQuestion(connection(vi.fn(response)), THREAD)

    expect(store.snapshot(THREAD).question).toEqual(QUESTION)
  })

  it.each([
    { turnId: 'turn-2', callId: 'call-2' },
    { turnId: QUESTION.turnId, callId: 'call-2' }
  ])('does not clear a replacement question $turnId/$callId when an old query finishes', async (replacement) => {
    const store = new ThreadTaskStore(null)
    store.setQuestion(THREAD, QUESTION)
    let finish!: (result: unknown) => void
    const request = vi.fn(() => new Promise((resolve) => { finish = resolve }))
    const recovery = service(store).reconcileQuestion(connection(request), THREAD)
    store.setQuestion(THREAD, replacement)

    finish({ data: [{ id: 'turn-2', status: 'completed' }] })
    await recovery

    expect(store.snapshot(THREAD).question).toEqual(replacement)
  })

  it('does not load history for a chat without a waiting question', async () => {
    const request = vi.fn()

    await service().reconcileQuestion(connection(request), THREAD)

    expect(request).not.toHaveBeenCalled()
  })
})
