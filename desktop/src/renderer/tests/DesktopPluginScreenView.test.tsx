import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { ScreenViewFramePayload, ScreenViewStatePayload } from '../../shared/screenView'
import { createDesktopPluginScreenView } from '../plugins/desktopPluginScreenView'
import { installDesktopApiMock } from './desktopApiMock'

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (error: unknown) => void
  const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no })
  return { promise, resolve, reject }
}

function fixture(open = vi.fn(async () => ({ ok: true }))) {
  const frames = new Set<(payload: ScreenViewFramePayload) => void>()
  const states = new Set<(payload: ScreenViewStatePayload) => void>()
  const close = vi.fn(async () => ({ ok: true }))
  const tune = vi.fn(async () => ({ ok: true }))
  const ack = vi.fn()
  installDesktopApiMock({
    screenView: {
      open, close, tune, ack,
      onFrame: (listener) => { frames.add(listener); return () => { frames.delete(listener) } },
      onState: (listener) => { states.add(listener); return () => { states.delete(listener) } }
    }
  })
  const scope = { active: true, cleanups: new Set<() => void>() }
  const api = createDesktopPluginScreenView(scope)
  const dispose = (): void => {
    scope.active = false
    for (const cleanup of [...scope.cleanups]) cleanup()
  }
  return { api, scope, dispose, open, close, tune, ack, frames, states }
}

beforeEach(() => vi.restoreAllMocks())

describe('Desktop plugin screen sessions', () => {
  it('routes state and frames to the matching handle and releases its subscriptions on close', async () => {
    const f = fixture()
    const a = f.api.open({ peerId: 'peer-a', maxWidth: 640 })
    const b = f.api.open({ peerId: 'peer-b', maxWidth: 960 })
    const state = vi.fn()
    const frame = vi.fn()
    a.onState(state)
    a.onFrame(frame)
    for (const notify of f.states) notify({ viewId: b.viewId, status: { kind: 'paused' } })
    expect(state).not.toHaveBeenCalled()
    for (const notify of f.states) notify({ viewId: a.viewId, status: { kind: 'live' } })
    expect(state).toHaveBeenCalledWith({ kind: 'live' })
    const payload = { viewId: a.viewId, sequence: 7, width: 640, height: 360, capturedAtUnixMs: 1, jpeg: new ArrayBuffer(1) }
    for (const notify of f.frames) notify(payload)
    expect(frame).toHaveBeenCalledWith(payload)
    await a.tune(1280)
    a.ack(7)
    expect(f.tune).toHaveBeenCalledWith({ viewId: a.viewId, maxWidth: 1280 })
    expect(f.ack).toHaveBeenCalledWith({ viewId: a.viewId, sequence: 7 })
    a.close()
    expect(f.frames.size).toBe(1)
    expect(f.states.size).toBe(1)
    expect(f.scope.cleanups.size).toBe(1)
    f.dispose()
    expect(f.frames.size).toBe(0)
    expect(f.states.size).toBe(0)
  })

  it('closes a late open without publishing into a disposed generation', async () => {
    const opening = deferred<{ ok: boolean }>()
    const f = fixture(vi.fn(() => opening.promise))
    const session = f.api.open({ peerId: 'peer-a', maxWidth: 640 })
    const state = vi.fn()
    session.onState(state)
    f.dispose()
    opening.resolve({ ok: true })
    await opening.promise
    await Promise.resolve()
    expect(f.close).toHaveBeenLastCalledWith({ viewId: session.viewId })
    expect(f.frames.size).toBe(0)
    expect(f.states.size).toBe(0)
    expect(state).not.toHaveBeenCalled()
    session.ack(9)
    await session.tune(1280)
    expect(f.ack).not.toHaveBeenCalled()
    expect(f.tune).not.toHaveBeenCalled()
    expect(() => f.api.open({ peerId: 'peer-a', maxWidth: 640 })).toThrow('inactive')
  })

  it('reports an open rejection as an unavailable state, including for a later subscriber', async () => {
    const opening = deferred<{ ok: boolean }>()
    const f = fixture(vi.fn(() => opening.promise))
    const session = f.api.open({ peerId: 'peer-a', maxWidth: 640 })
    const state = vi.fn()
    session.onState(state)
    opening.reject(new Error('Bridge unavailable'))
    await opening.promise.catch(() => {})
    expect(state).toHaveBeenLastCalledWith({ kind: 'unavailable', reason: 'hubRejected', detail: 'Bridge unavailable' })
    const late = vi.fn()
    session.onState(late)
    expect(late).toHaveBeenLastCalledWith({ kind: 'unavailable', reason: 'hubRejected', detail: 'Bridge unavailable' })
    f.dispose()
  })

  it('rejects a preferences read that completes after disposal', async () => {
    const f = fixture()
    const settings = deferred<{ screenViewDockWidth: number }>()
    installDesktopApiMock({ settings: { get: vi.fn(() => settings.promise) as never } })
    const pending = f.api.getLayout()
    f.dispose()
    settings.resolve({ screenViewDockWidth: 360 })
    await expect(pending).rejects.toThrow('inactive')
  })
})
