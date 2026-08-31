import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  USER_INPUT_AUTO_RESOLUTION_MS,
  USER_INPUT_INACTIVITY_MS,
  UserInputAutoResolutionCoordinator
} from '../userInputAutoResolution'

describe('UserInputAutoResolutionCoordinator', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    vi.setSystemTime(1_000)
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  function createCoordinator(): {
    coordinator: UserInputAutoResolutionCoordinator
    resolved: string[]
  } {
    const resolved: string[] = []
    return {
      coordinator: new UserInputAutoResolutionCoordinator({
        onChanged: () => {},
        onResolve: (bridgeId) => resolved.push(bridgeId)
      }),
      resolved
    }
  }

  it('does not schedule blocking requests', () => {
    const { coordinator, resolved } = createCoordinator()
    coordinator.track({
      bridgeId: '1',
      threadId: 'thread_1',
      requestId: 'request_1',
      isBlocking: true
    })

    vi.runAllTimers()

    expect(coordinator.getSnapshot()).toEqual([])
    expect(resolved).toEqual([])
  })

  it('waits for foreground inactivity before starting auto-resolution', () => {
    const { coordinator, resolved } = createCoordinator()
    coordinator.setWindowFocused(true)
    coordinator.setPresentedThread('thread_1')
    coordinator.track({
      bridgeId: '1',
      threadId: 'thread_1',
      requestId: 'request_1',
      isBlocking: false
    })

    expect(coordinator.getSnapshot()[0]).toMatchObject({
      phase: 'waitingForInactivity',
      deadlineAt: null
    })
    vi.advanceTimersByTime(USER_INPUT_INACTIVITY_MS)
    expect(coordinator.getSnapshot()[0]).toMatchObject({
      phase: 'scheduled',
      deadlineAt: 1_000 + USER_INPUT_INACTIVITY_MS + USER_INPUT_AUTO_RESOLUTION_MS
    })
    vi.advanceTimersByTime(USER_INPUT_AUTO_RESOLUTION_MS)

    expect(resolved).toEqual(['1'])
    expect(coordinator.getSnapshot()).toEqual([])
  })

  it('starts auto-resolution immediately for a background conversation', () => {
    const { coordinator, resolved } = createCoordinator()
    coordinator.track({
      bridgeId: '2',
      threadId: 'thread_2',
      requestId: 'request_2',
      isBlocking: false
    })

    expect(coordinator.getSnapshot()[0]).toMatchObject({
      phase: 'scheduled',
      deadlineAt: 1_000 + USER_INPUT_AUTO_RESOLUTION_MS
    })
    vi.advanceTimersByTime(USER_INPUT_AUTO_RESOLUTION_MS)
    expect(resolved).toEqual(['2'])
  })

  it('restarts only the foreground inactivity phase on conversation activity', () => {
    const { coordinator, resolved } = createCoordinator()
    coordinator.setWindowFocused(true)
    coordinator.setPresentedThread('thread_1')
    coordinator.track({
      bridgeId: '3',
      threadId: 'thread_1',
      requestId: 'request_3',
      isBlocking: false
    })

    vi.advanceTimersByTime(USER_INPUT_INACTIVITY_MS - 1_000)
    coordinator.recordConversationActivity('thread_1')
    vi.advanceTimersByTime(1_000)
    expect(coordinator.getSnapshot()[0]?.phase).toBe('waitingForInactivity')
    vi.advanceTimersByTime(USER_INPUT_INACTIVITY_MS - 1_000)
    expect(coordinator.getSnapshot()[0]?.phase).toBe('scheduled')
    expect(resolved).toEqual([])
  })

  it('permanently snoozes after request-card interaction', () => {
    const { coordinator, resolved } = createCoordinator()
    coordinator.track({
      bridgeId: '4',
      threadId: 'thread_1',
      requestId: 'request_4',
      isBlocking: false
    })

    coordinator.snooze('thread_1', 'request_4')
    coordinator.setWindowFocused(true)
    coordinator.setPresentedThread('thread_1')
    vi.runAllTimers()

    expect(coordinator.getSnapshot()).toEqual([])
    expect(resolved).toEqual([])
  })

  it('cancels timers when a request is manually resolved or the connection closes', () => {
    const { coordinator, resolved } = createCoordinator()
    coordinator.track({
      bridgeId: '5',
      threadId: 'thread_1',
      requestId: 'request_5',
      isBlocking: false
    })
    coordinator.remove('5')
    coordinator.track({
      bridgeId: '6',
      threadId: 'thread_2',
      requestId: 'request_6',
      isBlocking: false
    })
    coordinator.clear()

    vi.runAllTimers()

    expect(coordinator.getSnapshot()).toEqual([])
    expect(resolved).toEqual([])
  })
})
