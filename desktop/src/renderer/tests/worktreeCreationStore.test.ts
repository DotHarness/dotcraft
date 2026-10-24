import { describe, expect, it, vi } from 'vitest'
import {
  WorktreeCreationCancelledError,
  createWorktreeCreationStore,
  type WorktreeCreationResult
} from '../stores/worktreeCreationStore'

interface Thread {
  id: string
}

const ENVIRONMENT = { id: '.craft/environments/environment.json', name: 'web', path: 'C:\\repo\\.craft\\environments\\environment.json' }

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (error: unknown) => void
  const promise = new Promise<T>((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

function harness() {
  let listener: ((method: string, params: unknown) => void) | null = null
  let counter = 0
  const sendRequest = vi.fn(async (_method: string, _params: unknown, _timeoutMs?: number | null): Promise<unknown> => ({}))
  const store = createWorktreeCreationStore({
    sendRequest,
    onNotification: (next) => {
      listener = next
      return () => {
        listener = null
      }
    },
    newOperationId: () => `op-${++counter}`
  })
  const creation = deferred<WorktreeCreationResult<Thread>>()
  const createWithoutWorktree = vi.fn(async (): Promise<Thread> => ({ id: 'local-thread' }))
  const create = vi.fn((_operationId: string) => creation.promise)
  let settled: { value?: Thread; error?: unknown } | null = null
  const run = store.getState().run<Thread>({
    anchorId: 'request-1',
    workspacePath: 'C:\\repo',
    environment: ENVIRONMENT,
    create,
    createWithoutWorktree
  })
  run.then((value) => { settled = { value } }, (error) => { settled = { error } })
  return {
    store,
    sendRequest,
    creation,
    create,
    createWithoutWorktree,
    run,
    notify: (method: string, params: unknown) => listener?.(method, params),
    settled: () => settled,
    current: () => store.getState().creations['request-1']
  }
}

async function flush(): Promise<void> {
  for (let index = 0; index < 5; index += 1) await Promise.resolve()
}

describe('worktree creation', () => {
  it('reports steps and output for its operation and releases the held message once setup succeeds', async () => {
    const h = harness()
    expect(h.create).toHaveBeenCalledWith('op-1')

    h.notify('worktree/createProgress', { operationId: 'op-1', step: 'createWorktree', status: 'completed' })
    h.notify('worktree/createProgress', { operationId: 'other', step: 'copyFiles', status: 'failed' })
    h.notify('worktree/setupOutput', { operationId: 'op-1', delta: 'installing\n' })
    expect(h.current()?.steps).toMatchObject({ createWorktree: 'completed', copyFiles: 'pending' })
    expect(h.current()?.output).toBe('installing\n')

    h.creation.resolve({ thread: { id: 'thread-1' }, setup: { status: 'succeeded', outputTail: '' } })
    await expect(h.run).resolves.toEqual({ id: 'thread-1' })
    expect(h.current()).toBeUndefined()
  })

  it('holds the message after a failed setup until the user continues', async () => {
    const h = harness()
    h.creation.resolve({ thread: { id: 'thread-1' }, setup: { status: 'failed', exitCode: 1, outputTail: 'boom' } })
    await flush()

    expect(h.settled()).toBeNull()
    expect(h.current()).toMatchObject({ phase: 'failed', threadId: 'thread-1', setup: { exitCode: 1 } })

    h.store.getState().continueAnyway('request-1')
    await expect(h.run).resolves.toEqual({ id: 'thread-1' })
    expect(h.current()).toBeUndefined()
  })

  it('reruns setup in the same worktree with a fresh operation and sends once it succeeds', async () => {
    const h = harness()
    h.creation.resolve({ thread: { id: 'thread-1' }, setup: { status: 'failed', exitCode: 1, outputTail: 'boom' } })
    await flush()
    const rerun = deferred<unknown>()
    h.sendRequest.mockImplementation(async (method) => (method === 'worktree/runSetup' ? rerun.promise : {}))

    const retry = h.store.getState().retry('request-1')
    expect(h.sendRequest).toHaveBeenCalledWith('worktree/runSetup', { threadId: 'thread-1', operationId: 'op-2' }, null)
    expect(h.current()).toMatchObject({ phase: 'retrying', operationId: 'op-2', output: '' })
    h.notify('worktree/setupOutput', { operationId: 'op-2', delta: 'second try' })
    expect(h.current()?.output).toBe('second try')

    rerun.resolve({ setup: { status: 'failed', exitCode: 3, outputTail: 'still broken' } })
    await retry
    expect(h.settled()).toBeNull()
    expect(h.current()).toMatchObject({ phase: 'failed', setup: { exitCode: 3 } })

    h.sendRequest.mockImplementation(async (method) => (
      method === 'worktree/runSetup' ? { setup: { status: 'succeeded', outputTail: '' } } : {}
    ))
    await h.store.getState().retry('request-1')
    await expect(h.run).resolves.toEqual({ id: 'thread-1' })
  })

  it('cancels the running creation and rejects so the composer gets its draft back', async () => {
    const h = harness()

    await h.store.getState().cancel('request-1')
    expect(h.sendRequest).toHaveBeenCalledWith('worktree/cancelCreate', { operationId: 'op-1' })
    expect(h.current()?.phase).toBe('cancelling')

    h.creation.reject(Object.assign(new Error('cancelled'), { data: { code: 'creationCancelled' } }))
    await expect(h.run).rejects.toBeInstanceOf(WorktreeCreationCancelledError)
    expect(h.createWithoutWorktree).not.toHaveBeenCalled()
    expect(h.current()).toBeUndefined()
  })

  it('starts the thread locally with the same message when the worktree is not used', async () => {
    const h = harness()

    await h.store.getState().createWithoutWorktree('request-1')
    expect(h.sendRequest).toHaveBeenCalledWith('worktree/cancelCreate', { operationId: 'op-1' })

    h.creation.reject(Object.assign(new Error('cancelled'), { data: { code: 'creationCancelled' } }))
    await expect(h.run).resolves.toEqual({ id: 'local-thread' })
    expect(h.createWithoutWorktree).toHaveBeenCalledTimes(1)
  })

  it('passes other creation failures through', async () => {
    const h = harness()
    const failure = new Error('environment invalid')

    h.creation.reject(failure)
    await expect(h.run).rejects.toBe(failure)
    expect(h.current()).toBeUndefined()
  })
})
