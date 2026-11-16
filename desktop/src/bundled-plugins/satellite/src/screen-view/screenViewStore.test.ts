import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { DesktopPluginScreenSession, DesktopPluginScreenStatus } from '@dotcraft/plugin'
import { disposeScreenView, useScreenViewStore } from './screenViewStore'

const fixture = vi.hoisted(() => ({ open: vi.fn(), getLayout: vi.fn(), setLayout: vi.fn() }))
vi.mock('../runtime', () => ({ satelliteHost: () => ({ screenView: fixture }) }))

beforeEach(() => {
  vi.stubGlobal('window', { devicePixelRatio: 1 })
  fixture.open.mockReset()
  fixture.getLayout.mockResolvedValue({})
  fixture.setLayout.mockResolvedValue(undefined)
})

afterEach(() => {
  disposeScreenView()
  vi.unstubAllGlobals()
})

function screenSession(viewId: string) {
  const states = new Set<(status: DesktopPluginScreenStatus) => void>()
  const close = vi.fn(() => states.clear())
  const session: DesktopPluginScreenSession = {
    viewId, close, tune: vi.fn(async () => {}), ack: vi.fn(), onFrame: () => () => {},
    onState(listener) { states.add(listener); return () => { states.delete(listener) } }
  }
  return { session, close, states }
}

describe('screen view lifecycle', () => {
  it('keeps one session and remembers each thread mode while closing on navigation', () => {
    const a = screenSession('view-a')
    const b = screenSession('view-b')
    fixture.open.mockReturnValueOnce(a.session).mockReturnValueOnce(b.session)
    const store = useScreenViewStore.getState()
    store.open('thread-a', 'peer-a', 'dock')
    store.setMode('theater')
    expect(fixture.open).toHaveBeenCalledTimes(1)
    store.syncThread('thread-b', 'peer-b', true)
    expect(a.close).toHaveBeenCalledTimes(1)
    expect(a.states.size).toBe(0)
    expect(useScreenViewStore.getState().session).toBeNull()
    store.syncThread('thread-a', 'peer-a', true)
    expect(useScreenViewStore.getState().mode).toBe('theater')
    expect(useScreenViewStore.getState().session).toBe(b.session)
    store.close()
    store.syncThread('thread-a', 'peer-a', true)
    expect(fixture.open).toHaveBeenCalledTimes(2)
    expect(b.close).toHaveBeenCalledTimes(1)
  })

  it('does not end an open view on temporary offline state, but disposes it with the generation', () => {
    const a = screenSession('view-a')
    fixture.open.mockReturnValue(a.session)
    const store = useScreenViewStore.getState()
    store.open('thread-a', 'peer-a', 'dock')
    for (const notify of a.states) notify({ kind: 'offline' })
    store.syncThread('thread-a', 'peer-a', false)
    expect(useScreenViewStore.getState().status).toEqual({ kind: 'offline' })
    expect(a.close).not.toHaveBeenCalled()
    disposeScreenView()
    expect(a.close).toHaveBeenCalledTimes(1)
    expect(a.states.size).toBe(0)
    expect(useScreenViewStore.getState().session).toBeNull()
  })
})
