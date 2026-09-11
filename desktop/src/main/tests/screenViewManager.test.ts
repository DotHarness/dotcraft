import { describe, expect, it } from 'vitest'
import { SCREEN_VIEW_FRAME_CHANNEL, SCREEN_VIEW_STATE_CHANNEL } from '../../shared/screenView'
import { ScreenViewManager } from '../screenView/screenViewManager'
import type { ScreenViewSessionLike, ScreenViewSessionOptions } from '../screenView/screenViewSession'
import type { ScreenViewTarget } from '../screenView/screenViewTarget'

interface FakeSession extends ScreenViewSessionLike {
  options: ScreenViewSessionOptions
  started: boolean
  closed: boolean
  watchers: number
  maxWidth: number
  acks: number
}

interface FakeTarget extends ScreenViewTarget {
  visible: boolean
  sent: { channel: string; payload: unknown }[]
  notifyVisibilityChange(): void
  notifyGone(): void
}

function fakeTarget(id = 1): FakeTarget {
  const visibilityListeners: (() => void)[] = []
  const goneListeners: (() => void)[] = []
  const target: FakeTarget = {
    id,
    visible: true,
    sent: [],
    isVisible: () => target.visible,
    send: (channel, payload) => {
      target.sent.push({ channel, payload })
    },
    onVisibilityChange: (listener) => {
      visibilityListeners.push(listener)
    },
    onGone: (listener) => {
      goneListeners.push(listener)
    },
    notifyVisibilityChange: () => {
      for (const listener of visibilityListeners) listener()
    },
    notifyGone: () => {
      for (const listener of goneListeners) listener()
    }
  }
  return target
}

function managerHarness(): { manager: ScreenViewManager; sessions: FakeSession[] } {
  const sessions: FakeSession[] = []
  const manager = new ScreenViewManager({
    resolveBridge: async () => ({ url: 'ws://hub/bridge', headers: {} }),
    createSession: (options) => {
      const session: FakeSession = {
        options,
        started: false,
        closed: false,
        watchers: options.watchers,
        maxWidth: options.maxWidth,
        acks: 0,
        start: () => {
          session.started = true
        },
        setMaxWidth: (maxWidth) => {
          session.maxWidth = maxWidth
        },
        setWatchers: (watchers) => {
          session.watchers = watchers
        },
        ack: () => {
          session.acks += 1
        },
        close: () => {
          session.closed = true
        }
      }
      sessions.push(session)
      return session
    }
  })
  return { manager, sessions }
}

describe('ScreenViewManager', () => {
  it('opens one view per window and starts it with the window visible', () => {
    const { manager, sessions } = managerHarness()
    const target = fakeTarget()
    manager.open(target, { viewId: 'view-1', peerId: 'peer-1', maxWidth: 640 })

    expect(sessions).toHaveLength(1)
    expect(sessions[0].started).toBe(true)
    expect(sessions[0].watchers).toBe(1)
    manager.closeAll()
  })

  it('follows the window: hiding pauses capture and showing resumes it', () => {
    const { manager, sessions } = managerHarness()
    const target = fakeTarget()
    manager.open(target, { viewId: 'view-1', peerId: 'peer-1', maxWidth: 640 })

    target.visible = false
    target.notifyVisibilityChange()
    expect(sessions[0].watchers).toBe(0)

    target.visible = true
    target.notifyVisibilityChange()
    expect(sessions[0].watchers).toBe(1)
    manager.closeAll()
  })

  it('replaces the window view when another one opens', () => {
    const { manager, sessions } = managerHarness()
    const target = fakeTarget()
    manager.open(target, { viewId: 'view-1', peerId: 'peer-1', maxWidth: 640 })
    manager.open(target, { viewId: 'view-2', peerId: 'peer-2', maxWidth: 1920 })

    expect(sessions[0].closed).toBe(true)
    expect(sessions[1].maxWidth).toBe(1920)
    manager.closeAll()
  })

  it('closes the view when the window goes away', () => {
    const { manager, sessions } = managerHarness()
    const target = fakeTarget()
    manager.open(target, { viewId: 'view-1', peerId: 'peer-1', maxWidth: 640 })

    target.notifyGone()
    expect(sessions[0].closed).toBe(true)
  })

  it('ignores a tune or an ack naming another view', () => {
    const { manager, sessions } = managerHarness()
    const target = fakeTarget()
    manager.open(target, { viewId: 'view-1', peerId: 'peer-1', maxWidth: 640 })

    manager.tune(target, { viewId: 'stale', maxWidth: 1920 })
    manager.ack(target, { viewId: 'stale' })
    expect(sessions[0].maxWidth).toBe(640)
    expect(sessions[0].acks).toBe(0)

    manager.tune(target, { viewId: 'view-1', maxWidth: 1920 })
    manager.ack(target, { viewId: 'view-1' })
    expect(sessions[0].maxWidth).toBe(1920)
    expect(sessions[0].acks).toBe(1)
    manager.closeAll()
  })

  it('sends frames as a standalone buffer alongside the view state', () => {
    const { manager, sessions } = managerHarness()
    const target = fakeTarget()
    manager.open(target, { viewId: 'view-1', peerId: 'peer-1', maxWidth: 640 })

    const pooled = new Uint8Array([9, 9, 1, 2, 3]).subarray(2)
    sessions[0].options.onFrame({
      sequence: 4,
      width: 640,
      height: 360,
      capturedAtUnixMs: 12,
      jpeg: pooled
    })
    sessions[0].options.onState({ kind: 'reconnecting' })

    const frame = target.sent.find((entry) => entry.channel === SCREEN_VIEW_FRAME_CHANNEL)
    const payload = frame?.payload as { viewId: string; jpeg: ArrayBuffer }
    expect(payload.viewId).toBe('view-1')
    expect(Array.from(new Uint8Array(payload.jpeg))).toEqual([1, 2, 3])

    const state = target.sent.find((entry) => entry.channel === SCREEN_VIEW_STATE_CHANNEL)
    expect(state?.payload).toEqual({ viewId: 'view-1', status: { kind: 'reconnecting' } })
    manager.closeAll()
  })
})
