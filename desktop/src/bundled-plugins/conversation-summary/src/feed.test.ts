import type { DesktopPluginHost } from '@dotcraft/plugin'
import { describe, expect, it, vi } from 'vitest'
import { startSummaryFeed, type SummaryState } from './feed'

function wireItem(id: string, turnId: string, createdAt: string) {
  return { id, turnId, type: 'toolCall', status: 'completed', createdAt, payload: { callId: id, toolName: 'ReadFile' } }
}

function createHost(responses: Record<string, (params: any) => unknown>) {
  const notifications = new Map<string, (params: any) => void>()
  const host = {
    subagents: { list: () => [], onChange: () => () => undefined },
    appServer: {
      request: vi.fn(async (method: string, params: unknown) => responses[method]?.(params) ?? {}),
      onNotification: (method: string, listener: (params: any) => void) => {
        notifications.set(method, listener)
        return () => notifications.delete(method)
      }
    }
  } as unknown as DesktopPluginHost
  return { host, notify: (method: string, params: unknown) => notifications.get(method)?.(params) }
}

async function settle(): Promise<void> {
  for (let index = 0; index < 5; index += 1) await Promise.resolve()
}

describe('Summary feed', () => {
  it('merges live items that arrive during history loading without duplicates', async () => {
    let releaseHistory: (value: unknown) => void = () => undefined
    const { host, notify } = createHost({
      'thread/items/list': () => new Promise((resolve) => { releaseHistory = resolve }),
      'thread/read': () => ({ thread: { plan: null } }),
      'automation/list': () => ({ automations: [] })
    })
    let state: SummaryState | null = null
    const stop = startSummaryFeed(host, 'thread-1', (next) => { state = next })

    notify('item/completed', { threadId: 'thread-1', turnId: 't2', item: wireItem('b', 't2', '2026-10-01T10:05:00Z') })
    notify('item/completed', { threadId: 'thread-2', turnId: 'x', item: wireItem('z', 'x', '2026-10-01T10:05:00Z') })
    releaseHistory({
      data: [
        { turnId: 't1', item: wireItem('a', 't1', '2026-10-01T10:00:00Z') },
        { turnId: 't2', item: wireItem('b', 't2', '2026-10-01T10:05:00Z') }
      ],
      nextCursor: null
    })
    await settle()

    expect(state!.items.map((item) => item.id)).toEqual(['a', 'b'])
    stop()
  })

  it('drops items of turns removed by a rollback when the next turn starts', async () => {
    const { host, notify } = createHost({
      'thread/items/list': () => ({
        data: [
          { turnId: 't1', item: wireItem('a', 't1', '2026-10-01T10:00:00Z') },
          { turnId: 't2', item: wireItem('b', 't2', '2026-10-01T10:05:00Z') }
        ],
        nextCursor: null
      }),
      'thread/turns/list': () => ({
        data: [
          { id: 't3', startedAt: '2026-10-01T10:06:00Z' },
          { id: 't1', startedAt: '2026-10-01T09:59:00Z' }
        ],
        nextCursor: null
      })
    })
    let state: SummaryState | null = null
    const stop = startSummaryFeed(host, 'thread-1', (next) => { state = next })
    await settle()

    notify('turn/started', { turn: { id: 't3', threadId: 'thread-1' } })
    await settle()

    expect(state!.items.map((item) => item.id)).toEqual(['a'])
    stop()
  })
})
