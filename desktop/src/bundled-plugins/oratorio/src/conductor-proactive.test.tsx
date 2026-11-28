import { afterEach, describe, expect, it, vi } from 'vitest'
import type { DesktopPluginHost, DesktopPluginOwnedThread } from '@dotcraft/plugin'
import {
  AWAY_THRESHOLD_MS,
  buildConductorProactiveSignal,
  createConductorProactiveController
} from './conductor-proactive'

function createMockHost(initialConductor: DesktopPluginOwnedThread | null) {
  let conductor = initialConductor
  let sessionThreadId: string | null = null
  const sessionListeners = new Set<(snapshot: { workspacePath: string | null; threadId: string | null; mode: 'agent' | 'plan'; busy: boolean }) => void>()
  const projectListeners = new Set<(project: { id: string; workspacePath: string }) => void>()
  const listRunningThreads = vi.fn(async () => ({
    activeThreads: [{ id: 't-active', projectId: 'repo', activeState: 'working' as const }],
    recentFinished: []
  }))
  const linked = vi.fn(async () => [])
  const startBackgroundCheckIn = vi.fn(async () => ({ started: true, turnId: 'turn-1' }))

  const host = {
    environment: { locale: 'en' },
    session: {
      get threadId() { return sessionThreadId },
      onChange(listener: (snapshot: { workspacePath: string | null; threadId: string | null; mode: 'agent' | 'plan'; busy: boolean }) => void) {
        sessionListeners.add(listener)
        return () => sessionListeners.delete(listener)
      }
    },
    threads: {
      findOwned: () => conductor,
      linked,
      listRunningThreads,
      startBackgroundCheckIn
    },
    workspaces: {
      onProjectConnected(listener: (project: { id: string; workspacePath: string }) => void) {
        projectListeners.add(listener)
        return () => projectListeners.delete(listener)
      }
    }
  } as unknown as DesktopPluginHost

  return {
    host,
    listRunningThreads,
    startBackgroundCheckIn,
    setConductor(next: DesktopPluginOwnedThread | null) {
      conductor = next
    },
    emitSession(threadId: string | null) {
      sessionThreadId = threadId
      for (const listener of sessionListeners) {
        listener({ workspacePath: '/chats', threadId, mode: 'agent', busy: false })
      }
    },
    emitProjectConnected(id = 'repo') {
      for (const listener of projectListeners) {
        listener({ id, workspacePath: `/${id}` })
      }
    }
  }
}

describe('conductor proactive check-in', () => {
  afterEach(() => {
    vi.useRealTimers()
  })

  it('flags unfollowed active chats, waiting linked chats, and newly finished unread chats while deduplicating fingerprints', () => {
    const idle = buildConductorProactiveSignal([], [], 'conductor-1')
    expect(idle.noteworthy).toBe(false)

    const unfollowed = buildConductorProactiveSignal(
      [{ id: 't-1', projectId: 'p-1', activeState: 'working' }],
      [],
      'conductor-1'
    )
    expect(unfollowed.noteworthy).toBe(true)

    const followedWorking = buildConductorProactiveSignal(
      [{ id: 't-1', projectId: 'p-1', activeState: 'working' }],
      [{
        threadId: 't-1',
        projectId: 'p-1',
        projectName: 'p-1',
        workspacePath: '/p-1',
        title: 'Task',
        state: 'working',
        createdAt: '2026-11-28T00:00:00Z'
      }],
      'conductor-1'
    )
    expect(followedWorking.noteworthy).toBe(false)

    const finished = buildConductorProactiveSignal(
      [],
      [],
      'conductor-1',
      [{ id: 't-done', projectId: 'repo', lastActiveAt: '2026-11-28T13:00:00Z' }]
    )
    expect(finished.noteworthy).toBe(true)

    const repeated = buildConductorProactiveSignal(
      [],
      [],
      'conductor-1',
      [{ id: 't-done', projectId: 'repo', lastActiveAt: '2026-11-28T13:00:00Z' }]
    )
    expect(repeated.fingerprint).toBe(finished.fingerprint)

    const selfFinished = buildConductorProactiveSignal(
      [],
      [],
      'conductor-1',
      [{ id: 'conductor-1', projectId: 'chats', lastActiveAt: '2026-11-28T13:00:00Z' }]
    )
    expect(selfFinished.noteworthy).toBe(false)
  })

  it('triggers on project connect, entering conductor, window return, and resume while respecting pause', async () => {
    vi.useFakeTimers()
    const fixture = createMockHost({ threadId: 'conductor-1', workspacePath: '/chats' })
    const controller = createConductorProactiveController(fixture.host)
    const stop = controller.start()

    fixture.emitProjectConnected('repo')
    await vi.advanceTimersByTimeAsync(1_500)
    expect(fixture.startBackgroundCheckIn).toHaveBeenCalledTimes(1)

    fixture.setConductor({ threadId: 'conductor-1', workspacePath: '/chats', paused: true })
    await vi.advanceTimersByTimeAsync(50_000)
    fixture.emitSession('conductor-1')
    await vi.advanceTimersByTimeAsync(0)
    expect(fixture.startBackgroundCheckIn).toHaveBeenCalledTimes(1)

    fixture.setConductor({ threadId: 'conductor-1', workspacePath: '/chats' })
    await controller.runCheckIn('resume')
    expect(fixture.startBackgroundCheckIn).toHaveBeenCalledTimes(2)

    controller.reset()
    fixture.emitSession(null)
    fixture.emitSession('conductor-1')
    await vi.advanceTimersByTimeAsync(0)
    expect(fixture.startBackgroundCheckIn).toHaveBeenCalledTimes(3)

    fixture.listRunningThreads.mockResolvedValueOnce({
      activeThreads: [{ id: 't-active-2', projectId: 'repo', activeState: 'working' }],
      recentFinished: []
    })
    window.dispatchEvent(new Event('blur'))
    await vi.advanceTimersByTimeAsync(AWAY_THRESHOLD_MS + 1_000)
    window.dispatchEvent(new Event('focus'))
    await vi.advanceTimersByTimeAsync(0)
    expect(fixture.startBackgroundCheckIn).toHaveBeenCalledTimes(4)

    stop()
  })
})
