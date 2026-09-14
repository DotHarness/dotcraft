import { describe, expect, it, vi } from 'vitest'
import { BrowserTurnSubscriptions } from '../browserTurnSubscriptions'

function fixture() {
  const raw = vi.fn(async (_method: string, _params: unknown) => ({}))
  const leases = new BrowserTurnSubscriptions(raw)
  const send = (method: string) => leases.request(method, { threadId: 'thread' }, () => raw(method, { threadId: 'thread' }))
  return { raw, leases, send }
}

describe('browser turn subscriptions', () => {
  it('defers both demotion and renderer unsubscribe until the terminal turn', async () => {
    const { raw, leases, send } = fixture()
    await send('thread/subscribe')
    const retained = leases.retain('thread', 'one')
    await send('thread/unsubscribe')
    await send('thread/unsubscribe')
    expect(raw.mock.calls.filter(([method]) => method === 'thread/unsubscribe')).toHaveLength(0)
    await retained
    await leases.release('thread', 'one')
    await leases.release('thread', 'one')
    expect(raw.mock.calls.filter(([method]) => method === 'thread/unsubscribe')).toHaveLength(1)
  })

  it('preserves a newer turn and ignores an old terminal notification', async () => {
    const { raw, leases } = fixture()
    await leases.retain('thread', 'one')
    await leases.retain('thread', 'two')
    await leases.release('thread', 'one')
    expect(raw).toHaveBeenCalledTimes(1)
    await leases.release('thread', 'two')
    expect(raw).toHaveBeenLastCalledWith('thread/unsubscribe', { threadId: 'thread' })
  })

  it('keeps a stream still wanted by the renderer and releases a retainer-only subscription', async () => {
    const { raw, leases, send } = fixture()
    await leases.retain('thread', 'one')
    await send('thread/subscribe')
    await leases.release('thread', 'one')
    expect(raw.mock.calls.filter(([method]) => method === 'thread/unsubscribe')).toHaveLength(0)
    await send('thread/unsubscribe')
    await leases.retain('thread', 'two')
    await leases.release('thread', 'two')
    expect(raw.mock.calls.filter(([method]) => method === 'thread/unsubscribe')).toHaveLength(2)
  })

  it('does not unsubscribe a newer turn acquired while the older retain is pending', async () => {
    let resolve!: () => void
    const raw = vi.fn(() => new Promise<void>((done) => { resolve = done }))
    const leases = new BrowserTurnSubscriptions(raw)
    const first = leases.retain('thread', 'one')
    const release = leases.release('thread', 'one')
    const second = leases.retain('thread', 'two')
    resolve()
    await Promise.all([first, release, second])
    expect(raw).toHaveBeenCalledTimes(1)
    leases.clear()
    await leases.release('thread', 'two')
    expect(raw).toHaveBeenCalledTimes(1)
  })
})
