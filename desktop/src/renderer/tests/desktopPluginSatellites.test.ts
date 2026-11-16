// @vitest-environment jsdom

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createDesktopPluginRemoteToolHost, createDesktopPluginSatellites } from '../plugins/desktopPluginSatellites'
import { useThreadRouteStore } from '../stores/threadRouteStore'
import { useConnectionStore } from '../stores/connectionStore'
import { installDesktopApiMock } from './desktopApiMock'

function scope() {
  const value = { active: true, cleanups: new Set<() => void>() }
  return {
    value,
    dispose() {
      value.active = false
      for (const cleanup of [...value.cleanups]) cleanup()
    }
  }
}

beforeEach(() => {
  useThreadRouteStore.getState().resetForConnection()
  useConnectionStore.setState({ capabilities: { remoteToolHost: true } })
})
afterEach(() => vi.useRealTimers())

describe('Desktop satellite generation resources', () => {
  it('withdraws owned pending choices without interrupting a confirmed route or someone else’s selection', () => {
    const first = scope()
    const second = scope()
    const one = createDesktopPluginRemoteToolHost(first.value)
    const two = createDesktopPluginRemoteToolHost(second.value)
    const confirmed = { threadId: 'running', hostId: 'host', workspaceId: 'folder', status: 'connected' as const }
    useThreadRouteStore.setState({ routes: { running: confirmed } })
    one.setPendingRoute({ hostId: 'first', workspaceId: 'folder' })
    one.deferRoute('running', null)
    two.setPendingRoute({ hostId: 'second', workspaceId: 'folder' })
    two.deferRoute('running', null)
    first.dispose()
    expect(useThreadRouteStore.getState().pendingRoute?.hostId).toBe('second')
    expect(useThreadRouteStore.getState().deferredRoutes).toEqual({ running: null })
    expect(useThreadRouteStore.getState().routes.running).toBe(confirmed)
    second.dispose()
    expect(useThreadRouteStore.getState().pendingRoute).toBeNull()
    expect(useThreadRouteStore.getState().deferredRoutes).toEqual({})
    expect(() => two.setPendingRoute(null)).toThrow('not active')
  })

  it('does not restore a route after a generation is disposed while reading its preference', async () => {
    vi.useFakeTimers()
    let finish!: (value: unknown) => void
    const get = vi.fn(() => new Promise(resolve => { finish = resolve }))
    const request = vi.fn()
    installDesktopApiMock({ settings: { get }, appServer: { sendRequest: request } })
    const current = scope()
    const host = createDesktopPluginRemoteToolHost(current.value)
    host.maybeReapply('visible')
    await vi.advanceTimersByTimeAsync(450)
    expect(get).toHaveBeenCalledOnce()
    current.dispose()
    finish({ satelliteRouteByThread: { '::visible': { hostId: 'host', workspaceId: 'folder' } } })
    await Promise.resolve()
    await Promise.resolve()
    expect(request).not.toHaveBeenCalled()
  })

  it('does not cancel another generation’s replacement restoration', async () => {
    vi.useFakeTimers()
    const get = vi.fn(async () => ({}))
    installDesktopApiMock({ settings: { get } })
    const first = scope()
    const second = scope()
    createDesktopPluginRemoteToolHost(first.value).maybeReapply('visible')
    createDesktopPluginRemoteToolHost(second.value).maybeReapply('visible')
    first.dispose()
    await vi.advanceTimersByTimeAsync(450)
    expect(get).toHaveBeenCalledOnce()
    second.dispose()
  })

  it('owns fleet subscriptions and reports only invitations created by this Desktop', async () => {
    const release = vi.fn()
    let emit!: (event: never) => void
    const listener = vi.fn()
    installDesktopApiMock({
      satellites: { onEvent: callback => { emit = callback; return release } },
      settings: { get: async () => ({ createdSatelliteInviteIds: [{ inviteId: 'ours', expiresAt: '2099-01-01' }] }) }
    })
    const current = scope()
    const host = createDesktopPluginSatellites(current.value)
    host.onEvent(listener)
    emit({ kind: 'joined' } as never)
    expect(listener).toHaveBeenCalledOnce()
    expect(await host.wasInvitedHere('ours')).toBe(true)
    expect(await host.wasInvitedHere('someone-else')).toBe(false)
    current.dispose()
    emit({ kind: 'joined' } as never)
    expect(listener).toHaveBeenCalledOnce()
    expect(release).toHaveBeenCalledOnce()
  })
})
