import type { DesktopPluginHost } from '@dotcraft/plugin'
import { afterEach, describe, expect, it, vi } from 'vitest'

import {
  formatLatency,
  formatTokens,
  formatTokensPerSecond
} from './TokenHud'
import {
  getUsage,
  startUsageFeed
} from './usage'

afterEach(() => {
  vi.useRealTimers()
})

describe('Token HUD usage', () => {
  it('aggregates host-measured throughput and scopes totals to the current chat', async () => {
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
    expect(getUsage().totalTokens).toBe(9_000)
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
    expect(getUsage().totalTokens).toBe(12_000)
    expect(getUsage().cacheHitRate).toBeCloseTo(0.9)

    notifications.get('turn/completed')?.({ turn: { id: 'turn-1', threadId: 'thread-1' } })
    await vi.advanceTimersByTimeAsync(0)
    expect(getUsage().totalTokens).toBe(12_000)
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
          groups: [{ model: 'atlas-4', turns: 3, totalTokens: 120 }]
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
      groups: [{ model: 'atlas-4', turns: 3, totalTokens: 120 }]
    })

    sessionListener?.({ workspacePath: '/workspace/example', threadId: null, mode: 'agent', busy: false })
    expect(getUsage().threadUsage).toBeNull()
    stop()
  })

  it('keeps the last throughput across task changes until a new turn sample replaces it', async () => {
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

    const stop = startUsageFeed(host)
    await vi.runAllTicks()
    notifications.get('turn/started')?.({ turn: { id: 'turn-1', threadId: 'thread-1' } })
    notifications.get('item/usage/delta')?.({
      threadId: 'thread-1',
      turnId: 'turn-1',
      turnLlmCalls: 1,
      outputTokens: 60,
      requestTiming: { firstOutputLatencyMs: 1_000, generationMs: 2_000 }
    })
    expect(getUsage().tokensPerSecond).toBe(30)
    expect(getUsage().firstTokenLatencyMs).toBe(1_000)

    sessionListener?.({
      workspacePath: '/workspace/example', threadId: 'thread-2', mode: 'agent', busy: true
    })
    expect(getUsage().tokensPerSecond).toBe(30)
    expect(getUsage().firstTokenLatencyMs).toBe(1_000)
    expect(getUsage().waitingForSample).toBe(true)

    notifications.get('turn/started')?.({ turn: { id: 'turn-2', threadId: 'thread-2' } })
    expect(getUsage().tokensPerSecond).toBe(30)
    notifications.get('item/usage/delta')?.({
      threadId: 'thread-2',
      turnId: 'turn-2',
      turnLlmCalls: 1,
      outputTokens: 20,
      requestTiming: { firstOutputLatencyMs: 2_000, generationMs: 1_000 }
    })
    expect(getUsage().tokensPerSecond).toBe(20)
    expect(getUsage().firstTokenLatencyMs).toBe(2_000)
    expect(getUsage().waitingForSample).toBe(false)

    notifications.get('turn/completed')?.({ turn: { id: 'turn-2', threadId: 'thread-2' } })
    expect(getUsage().tokensPerSecond).toBe(20)
    stop()
  })

  it('uses compact stable formatting', () => {
    expect(formatTokens(517)).toBe('517')
    expect(formatTokens(12_200)).toBe('12.2K')
    expect(formatTokens(517_000)).toBe('517K')
    expect(formatTokens(1_200_000)).toBe('1.2M')
    expect(formatTokensPerSecond(8.36)).toBe('8.4')
    expect(formatTokensPerSecond(34.4)).toBe('34')
    expect(formatLatency(840)).toBe('0.8s')
    expect(formatLatency(1_240)).toBe('1.2s')
    expect(formatLatency(12_400)).toBe('12s')
  })
})
