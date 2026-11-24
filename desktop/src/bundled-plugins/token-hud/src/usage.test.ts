import type { DesktopPluginHost } from '@dotcraft/plugin'
import { afterEach, describe, expect, it, vi } from 'vitest'

import {
  getUsage,
  startUsageFeed
} from './usage'

afterEach(() => {
  vi.useRealTimers()
})

describe('Token HUD usage', () => {
  it('keeps thread breakdown totals separate from live cache usage and throughput', async () => {
    vi.useFakeTimers()
    const notifications = new Map<string, (params: any) => void>()
    const request = vi.fn().mockResolvedValue({
      threadId: 'thread-1',
      turns: 2,
      totalTokens: 9_000,
      inputTokens: 8_000,
      cachedInputTokens: 6_000,
      groups: []
    })
    const host = {
      session: {
        workspacePath: '/workspace/example',
        threadId: 'thread-1',
        mode: 'agent',
        busy: false,
        onChange: () => () => undefined
      },
      appServer: {
        request,
        onNotification: (method: string, listener: (params: any) => void) => {
          notifications.set(method, listener)
          return () => notifications.delete(method)
        }
      }
    } as unknown as DesktopPluginHost

    const stop = startUsageFeed(host)
    await vi.advanceTimersByTimeAsync(0)
    expect(request).toHaveBeenCalledWith('usage/thread', { workspacePath: '/workspace/example', threadId: 'thread-1' })
    expect(getUsage().threadUsage?.totalTokens).toBe(9_000)
    expect(getUsage().cacheHitRate).toBeCloseTo(0.75)

    notifications.get('turn/started')?.({ turn: { id: 'turn-1', threadId: 'thread-1' } })
    notifications.get('item/usage/delta')?.({
      threadId: 'thread-1', turnId: 'turn-1', turnLlmCalls: 1, outputTokens: 20
    })
    expect(getUsage().tokensPerSecond).toBeNull()
    expect(getUsage().waitingForSample).toBe(true)
    notifications.get('item/usage/delta')?.({
      threadId: 'other-thread',
      turnLlmCalls: 1,
      outputTokens: 999,
      requestTiming: { firstOutputLatencyMs: 1, generationMs: 1_000 }
    })
    notifications.get('item/usage/delta')?.({
      threadId: 'thread-1',
      turnId: 'turn-1',
      turnLlmCalls: 2,
      outputTokens: 60,
      requestTiming: { firstOutputLatencyMs: 1_000, generationMs: 2_000 }
    })
    notifications.get('item/usage/delta')?.({
      threadId: 'thread-1',
      turnId: 'turn-1',
      turnLlmCalls: 3,
      outputTokens: 60,
      requestTiming: { firstOutputLatencyMs: 3_000, generationMs: 1_000 }
    })
    expect(getUsage().tokensPerSecond).toBe(40)
    expect(getUsage().firstTokenLatencyMs).toBe(2_000)
    expect(getUsage().waitingForSample).toBe(false)

    notifications.get('thread/tokenUsage/updated')?.({
      threadId: 'other-thread',
      tokenUsage: { total: { totalTokens: 1, inputTokens: 1, cachedInputTokens: 0 } }
    })
    notifications.get('thread/tokenUsage/updated')?.({
      threadId: 'thread-1',
      tokenUsage: { total: { totalTokens: 12_000, inputTokens: 10_000, cachedInputTokens: 9_000 } }
    })
    expect(getUsage().threadUsage?.totalTokens).toBe(9_000)
    expect(getUsage().cacheHitRate).toBeCloseTo(0.9)

    request.mockResolvedValue({
      threadId: 'thread-1',
      turns: 3,
      totalTokens: 15_000,
      inputTokens: 12_000,
      cachedInputTokens: 6_000,
      groups: [{ model: 'atlas-4', turns: 3, totalTokens: 15_000, costUsd: 0.42 }]
    })
    notifications.get('turn/completed')?.({ turn: { id: 'turn-1', threadId: 'thread-1' } })
    await vi.advanceTimersByTimeAsync(0)
    expect(getUsage().threadUsage?.totalTokens).toBe(15_000)
    expect(getUsage().threadUsage?.groups[0].totalTokens).toBe(15_000)
    expect(getUsage().cacheHitRate).toBeCloseTo(0.9)
    expect(request.mock.calls.map(([method]) => method)).toEqual(['usage/thread', 'usage/thread'])
    stop()
  })

  it('tracks the active thread breakdown and clears it when the thread changes', async () => {
    vi.useFakeTimers()
    let sessionListener: ((session: {
      workspacePath: string
      threadId: string | null
      mode: string
      busy: boolean
    }) => void) | null = null
    const request = vi.fn(async (method: string, params: { threadId?: string }) => method === 'usage/thread'
      ? {
          threadId: params.threadId,
          turns: 3,
          totalTokens: 120,
          costUsd: 0.42,
          groups: [{ model: 'atlas-4', turns: 3, totalTokens: 120, costUsd: 0.42 }]
        }
      : { totalTokens: 10, totalInputTokens: 8, cacheHitRate: 0.5 })
    const host = {
      session: {
        workspacePath: '/workspace/example',
        threadId: 'thread-1',
        mode: 'agent',
        busy: false,
        onChange: (listener: typeof sessionListener) => {
          sessionListener = listener
          return () => { sessionListener = null }
        }
      },
      appServer: {
        request,
        onNotification: () => () => undefined
      }
    } as unknown as DesktopPluginHost

    const stop = startUsageFeed(host)
    await vi.runAllTicks()
    expect(getUsage().threadUsage).toEqual({
      threadId: 'thread-1',
      turns: 3,
      totalTokens: 120,
      costUsd: 0.42,
      groups: [{ model: 'atlas-4', turns: 3, totalTokens: 120, costUsd: 0.42 }]
    })

    sessionListener?.({ workspacePath: '/workspace/example', threadId: null, mode: 'agent', busy: false })
    expect(getUsage().threadUsage).toBeNull()
    stop()
  })

  it('restores timing and compares new readings within the same workspace thread', async () => {
    vi.useFakeTimers()
    const notifications = new Map<string, (params: any) => void>()
    let sessionListener: ((session: {
      workspacePath: string
      threadId: string
      mode: string
      busy: boolean
    }) => void) | null = null
    const host = {
      session: {
        workspacePath: '/workspace/example',
        threadId: 'thread-1',
        mode: 'agent',
        busy: false,
        onChange: (listener: typeof sessionListener) => {
          sessionListener = listener
          return () => { sessionListener = null }
        }
      },
      appServer: {
        request: vi.fn().mockResolvedValue({
          totalTokens: 3_500_000,
          totalInputTokens: 2_000_000,
          cacheHitRate: 0.8162
        }),
        onNotification: (method: string, listener: (params: any) => void) => {
          notifications.set(method, listener)
          return () => notifications.delete(method)
        }
      }
    } as unknown as DesktopPluginHost

    const select = (threadId: string, busy = false, workspacePath = '/workspace/example'): void => {
      sessionListener?.({ workspacePath, threadId, mode: 'agent', busy })
    }
    const turnEvent = (event: string, threadId: string, id: string): void => {
      notifications.get(event)?.({ turn: { id, threadId } })
    }
    const sample = (threadId: string, turnId: string, outputTokens: number, latency?: number): void => {
      notifications.get('item/usage/delta')?.({
        threadId, turnId, turnLlmCalls: 1, outputTokens,
        requestTiming: latency === undefined ? null : { firstOutputLatencyMs: latency, generationMs: 1_000 }
      })
    }

    const stop = startUsageFeed(host)
    await vi.runAllTicks()
    turnEvent('turn/started', 'thread-1', 'turn-1')
    sample('thread-1', 'turn-1', 30, 1_000)

    select('thread-2', true)
    expect(getUsage()).toMatchObject({ tokensPerSecond: null, firstTokenLatencyMs: null, waitingForSample: true })
    turnEvent('turn/started', 'thread-2', 'turn-1')
    sample('thread-2', 'turn-1', 20, 2_000)
    expect(getUsage()).toMatchObject({ tokensPerSecond: 20, firstTokenLatencyMs: 2_000, speedComparison: null })
    turnEvent('turn/completed', 'thread-2', 'turn-1')

    select('thread-1', true)
    expect(getUsage()).toMatchObject({ tokensPerSecond: 30, firstTokenLatencyMs: 1_000, speedComparison: null })
    turnEvent('turn/completed', 'thread-1', 'turn-1')
    turnEvent('turn/started', 'thread-1', 'turn-2')
    sample('thread-1', 'turn-2', 40, 1_500)
    expect(getUsage()).toMatchObject({ tokensPerSecond: 40, speedComparison: { previous: 30, next: 40 } })

    select('thread-1', false, '/workspace/other')
    expect(getUsage()).toMatchObject({ tokensPerSecond: null, firstTokenLatencyMs: null, speedComparison: null })
    select('thread-1')
    expect(getUsage()).toMatchObject({ tokensPerSecond: 40, firstTokenLatencyMs: 1_500, speedComparison: null })

    turnEvent('turn/started', 'thread-1', 'turn-3')
    sample('thread-1', 'turn-3', 120)
    expect(getUsage()).toMatchObject({ tokensPerSecond: null, firstTokenLatencyMs: null })
    turnEvent('turn/completed', 'thread-1', 'turn-3')

    select('thread-2')
    expect(getUsage()).toMatchObject({ tokensPerSecond: 20, firstTokenLatencyMs: 2_000, speedComparison: null })
    select('thread-1')
    expect(getUsage()).toMatchObject({ tokensPerSecond: null, firstTokenLatencyMs: null, speedComparison: null })
    turnEvent('turn/started', 'thread-1', 'turn-4')
    sample('thread-1', 'turn-4', 25, 800)
    expect(getUsage().speedComparison).toEqual({ previous: 40, next: 25 })

    turnEvent('turn/started', 'thread-1', 'turn-5')
    expect(getUsage().tokensPerSecond).toBe(25)
    turnEvent('turn/failed', 'thread-1', 'turn-5')
    expect(getUsage()).toMatchObject({ tokensPerSecond: null, firstTokenLatencyMs: null, waitingForSample: false })
    stop()
  })

})
