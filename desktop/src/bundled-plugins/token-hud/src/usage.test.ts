import type { DesktopPluginHost } from '@dotcraft/plugin'
import { afterEach, describe, expect, it, vi } from 'vitest'

import {
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
  it('aggregates client-observed throughput and refreshes workspace totals after a turn', async () => {
    vi.useFakeTimers()
    const notifications = new Map<string, (params: any) => void>()
    const request = vi.fn().mockResolvedValue({
      totalTokens: 3_500_000,
      totalInputTokens: 2_000_000,
      cacheHitRate: 0.8162
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

    let observedAtMs = 0
    const stop = startUsageFeed(host, () => observedAtMs)
    await vi.runAllTicks()
    notifications.get('turn/started')?.({ turn: { id: 'turn-1', threadId: 'thread-1' } })
    notifications.get('item/usage/delta')?.({
      threadId: 'thread-1', outputTokens: 20
    })
    expect(getUsage().tokensPerSecond).toBeNull()
    observedAtMs = 1_000
    notifications.get('item/agentMessage/delta')?.({
      threadId: 'thread-1', turnId: 'turn-1', delta: 'Hello'
    })
    observedAtMs = 3_000
    notifications.get('item/usage/delta')?.({
      threadId: 'thread-1', turnId: 'turn-1', outputTokens: 60
    })
    observedAtMs = 4_000
    notifications.get('item/toolCall/argumentsDelta')?.({
      threadId: 'thread-1', turnId: 'turn-1', delta: '{'
    })
    observedAtMs = 5_000
    notifications.get('item/usage/delta')?.({
      threadId: 'thread-1', turnId: 'turn-1', outputTokens: 60
    })

    expect(getUsage().tokensPerSecond).toBe(40)
    expect(getUsage().waitingForSample).toBe(false)
    request.mockRejectedValueOnce(new Error('offline'))
    notifications.get('turn/completed')?.({ turn: { id: 'turn-1', threadId: 'thread-1' } })
    await vi.advanceTimersByTimeAsync(0)
    expect(getUsage().totalTokens).toBe(3_500_000)
    expect(getUsage().cacheHitRate).toBeCloseTo(0.8162)
    expect(request).toHaveBeenCalledTimes(2)
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

    let observedAtMs = 0
    const stop = startUsageFeed(host, () => observedAtMs)
    await vi.runAllTicks()
    notifications.get('turn/started')?.({ turn: { id: 'turn-1', threadId: 'thread-1' } })
    observedAtMs = 1_000
    notifications.get('item/agentMessage/delta')?.({ threadId: 'thread-1', turnId: 'turn-1' })
    observedAtMs = 3_000
    notifications.get('item/usage/delta')?.({
      threadId: 'thread-1', turnId: 'turn-1', outputTokens: 60
    })
    expect(getUsage().tokensPerSecond).toBe(30)

    sessionListener?.({
      workspacePath: '/workspace/example', threadId: 'thread-2', mode: 'agent', busy: true
    })
    expect(getUsage().tokensPerSecond).toBe(30)
    expect(getUsage().waitingForSample).toBe(true)

    notifications.get('turn/started')?.({ turn: { id: 'turn-2', threadId: 'thread-2' } })
    expect(getUsage().tokensPerSecond).toBe(30)
    observedAtMs = 5_000
    notifications.get('item/agentMessage/delta')?.({ threadId: 'thread-2', turnId: 'turn-2' })
    observedAtMs = 6_000
    notifications.get('item/usage/delta')?.({
      threadId: 'thread-2', turnId: 'turn-2', outputTokens: 20
    })
    expect(getUsage().tokensPerSecond).toBe(20)
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
  })
})
