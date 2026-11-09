import { describe, expect, it, vi } from 'vitest'
import { BrowserTurnSubscriptions } from '../browserTurnSubscriptions'

function fixture() {
  let snapshot: unknown = { currentState: { turnId: 'one', turnStatus: 'running' } }
  const raw = vi.fn(async (_method: string, _params: unknown) => snapshot)
  const ended = vi.fn()
  const subscriptions = new BrowserTurnSubscriptions(async (method, params, onResponse) => {
    const result = await raw(method, params)
    onResponse?.(result)
    return result
  }, ended)
  const send = <T>(method: string, params: unknown, result: T, owner?: string, onResponse?: (value: T) => void) =>
    subscriptions.request(method, params, async hook => { hook?.(result); return result }, owner, onResponse)
  const resume = (owner?: string) => send('thread/resume', { threadId: 'thread' }, snapshot, owner)
  const detach = (owner?: string) => send('thread/unsubscribe', { threadId: 'thread' }, {}, owner)
  return { raw, ended, subscriptions, send, resume, detach, snapshot: (value: unknown) => { snapshot = value } }
}

describe('Desktop thread attachments', () => {
  it('keeps one attachment for view, transcript and native owners until the last leaves', async () => {
    const f = fixture()
    await f.resume()
    await f.resume('transcript:panel')
    await expect(f.subscriptions.retain('thread', 'one')).resolves.toBe(true)
    expect(f.raw).not.toHaveBeenCalled()
    await f.detach()
    await f.subscriptions.release('thread', 'one')
    expect(f.raw).not.toHaveBeenCalled()
    await f.detach('transcript:panel')
    await f.detach('transcript:panel')
    expect(f.raw).toHaveBeenCalledExactlyOnceWith('thread/unsubscribe', { threadId: 'thread' })
  })

  it.each(['thread/start', 'thread/fork', 'worktree/createAndStart'])('accounts for %s autoattach before response application', async method => {
    const f = fixture()
    let detached!: Promise<unknown>
    await f.send(method, { threadId: 'source' }, { thread: { id: 'child' } }, 'transcript:child', () => {
      detached = f.send('thread/unsubscribe', { threadId: 'child' }, {}, 'transcript:child')
    })
    await detached
    expect(f.raw).toHaveBeenCalledExactlyOnceWith('thread/unsubscribe', { threadId: 'child' })
  })

  it('uses a no-override metadata resume when native ownership has no current attachment', async () => {
    const f = fixture()
    await expect(f.subscriptions.retain('thread', 'one')).resolves.toBe(true)
    await f.subscriptions.retain('thread', 'one')
    expect(f.raw).toHaveBeenCalledExactlyOnceWith('thread/resume', { threadId: 'thread' })
    await f.subscriptions.releaseViews()
    expect(f.subscriptions.retained).toBe(true)
    await f.subscriptions.release('thread', 'one')
    expect(f.raw).toHaveBeenLastCalledWith('thread/unsubscribe', { threadId: 'thread' })
  })

  it.each(['running', 'waitingApproval', 'waitingInput'])('retains a %s turn after its view detaches', async turnStatus => {
    const f = fixture()
    await f.resume()
    await f.subscriptions.releaseViews()
    f.raw.mockClear()
    f.snapshot({ currentState: { turnId: 'one', turnStatus } })

    await expect(f.subscriptions.retain('thread', 'one')).resolves.toBe(true)
    await f.resume()
    await f.subscriptions.releaseViews()

    expect(f.ended).not.toHaveBeenCalled()
    expect(f.subscriptions.retained).toBe(true)
    expect(f.raw).toHaveBeenCalledExactlyOnceWith('thread/resume', { threadId: 'thread' })
  })

  it('cleans an already terminal or no longer retained native turn immediately', async () => {
    const f = fixture()
    f.snapshot({ currentState: { turnId: 'one', turnStatus: 'completed' } })
    await expect(f.subscriptions.retain('thread', 'one')).resolves.toBe(false)
    expect(f.ended).toHaveBeenCalledWith('thread', 'one')
    expect(f.subscriptions.retained).toBe(false)
    expect(f.raw).toHaveBeenLastCalledWith('thread/unsubscribe', { threadId: 'thread' })
    f.snapshot({ currentState: {} })
    await expect(f.subscriptions.retain('thread', 'gone')).resolves.toBe(false)
    expect(f.ended).toHaveBeenLastCalledWith('thread', 'gone')
  })

  it('ignores an old terminal after a new turn acquires the pin', async () => {
    const f = fixture()
    await f.resume()
    await f.subscriptions.retain('thread', 'one')
    f.subscriptions.observeTurn('thread', 'two', false)
    await f.subscriptions.retain('thread', 'two')
    await f.detach()
    await f.subscriptions.release('thread', 'one')
    expect(f.raw).not.toHaveBeenCalled()
    await f.subscriptions.release('thread', 'two')
    expect(f.raw).toHaveBeenCalledExactlyOnceWith('thread/unsubscribe', { threadId: 'thread' })
  })

  it('serializes detach after an inflight resume and invalidates demoted view ownership', async () => {
    const f = fixture()
    let finish!: () => void
    const resumed = f.subscriptions.request('thread/resume', { threadId: 'thread' }, hook => new Promise(resolve => {
      finish = () => { const result = { currentState: {} }; hook?.(result); resolve(result) }
    }))
    await Promise.resolve()
    const demote = f.subscriptions.releaseViews()
    finish()
    await Promise.all([resumed, demote])
    expect(f.raw).toHaveBeenCalledExactlyOnceWith('thread/unsubscribe', { threadId: 'thread' })
    await f.resume()
    await f.detach()
    expect(f.raw).toHaveBeenCalledTimes(2)
  })

  it('pins a created task until its turn terminates without treating UI detach as cancellation', async () => {
    const f = fixture()
    await f.send('thread/start', {}, { thread: { id: 'thread' } }, 'task')
    expect(f.subscriptions.retained).toBe(true)
    await f.send('turn/start', { threadId: 'thread' }, { turn: { id: 'one' } }, 'task')
    await f.detach()
    expect(f.raw).not.toHaveBeenCalled()
    await f.subscriptions.release('thread', 'one')
    expect(f.subscriptions.retained).toBe(false)
    expect(f.raw).toHaveBeenCalledExactlyOnceWith('thread/unsubscribe', { threadId: 'thread' })
  })
})
