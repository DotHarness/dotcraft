import type { DesktopPluginHost } from '@dotcraft/plugin'
import { afterEach, describe, expect, it, vi } from 'vitest'

import {
  formatTokens,
  formatTokensPerSecond
} from '../../../../sdk/typescript/samples/desktop-plugins/token-hud/desktop/src/TokenHud'
import {
  getUsage,
  startUsageFeed
} from '../../../../sdk/typescript/samples/desktop-plugins/token-hud/desktop/src/usage'

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

  it('uses compact stable formatting', () => {
    expect(formatTokens(517)).toBe('517')
    expect(formatTokens(12_200)).toBe('12.2K')
    expect(formatTokens(517_000)).toBe('517K')
    expect(formatTokens(1_200_000)).toBe('1.2M')
    expect(formatTokensPerSecond(8.36)).toBe('8.4')
    expect(formatTokensPerSecond(34.4)).toBe('34')
  })
})
