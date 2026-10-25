import { vi } from 'vitest'
import type { NewTaskPromptMode } from '../../shared/threadTasks'
import {
  DESKTOP_THREAD_TOOL_NAMESPACE,
  handleDesktopRuntimeThreadToolCall,
  type AppServerRequestClient,
  type DynamicToolCallResult
} from '../desktopRuntimeThreadTools'
import { DesktopThreadTasks } from '../threadTasks/threadTaskService'
import { ThreadTaskStore } from '../threadTasks/threadTaskStore'

export const WORKSPACE = 'F:\\examples\\workspace'
export const PARENT = 'thread-parent'

type Handler = (method: string, params?: unknown, timeoutMs?: number | null) => unknown

export interface Harness {
  client: AppServerRequestClient
  calls: () => Array<[string, unknown]>
  store: ThreadTaskStore
  tasks: DesktopThreadTasks
  notifyOffer: ReturnType<typeof vi.fn>
  setMode: (mode: NewTaskPromptMode) => void
  call: (args: Record<string, unknown>, turnId?: string, tool?: string) => Promise<DynamicToolCallResult | undefined>
  composerTurn: (turnId: string, overrides?: { originChannel?: string; triggerKind?: string }) => void
}

export function createHarness(options: { handler?: Handler; filePath?: string | null; now?: () => number } = {}): Harness {
  let threadCount = 0
  const sendRequest = vi.fn(async (method: string, params?: unknown, timeoutMs?: number | null) => {
    const custom = options.handler?.(method, params, timeoutMs)
    if (custom !== undefined) return custom
    if (method === 'thread/start' || method === 'worktree/createAndStart') {
      threadCount += 1
      return { thread: { id: `thread-child-${threadCount}`, displayName: (params as { displayName?: string }).displayName } }
    }
    if (method === 'turn/start') return { turn: { id: `turn-child-${threadCount}` } }
    throw new Error(`unexpected ${method}`)
  })
  const client = { sendRequest } as unknown as AppServerRequestClient
  const store = new ThreadTaskStore(options.filePath ?? null, options.now)
  let mode: NewTaskPromptMode = 'ask'
  const notifyOffer = vi.fn()
  const tasks = new DesktopThreadTasks({ store, getNewTaskPrompt: () => mode, notifyOffer })
  return {
    client,
    calls: () => sendRequest.mock.calls.map((call) => [call[0], call[1]] as [string, unknown]),
    store,
    tasks,
    notifyOffer,
    setMode: (next) => { mode = next },
    call: (args, turnId = 'turn-1', tool = 'CreateThread') => handleDesktopRuntimeThreadToolCall(client, {
      namespace: DESKTOP_THREAD_TOOL_NAMESPACE,
      tool,
      threadId: PARENT,
      turnId,
      callId: `call-${turnId}`,
      arguments: args
    }, WORKSPACE, { tasks }),
    composerTurn: (turnId, overrides = {}) => tasks.handleNotification('turn/started', {
      turn: {
        id: turnId,
        threadId: PARENT,
        originChannel: overrides.originChannel ?? 'dotcraft-desktop',
        items: [{ type: 'userMessage', payload: { text: 'hi', ...(overrides.triggerKind ? { triggerKind: overrides.triggerKind } : {}) } }]
      }
    })
  }
}

export function structured(result: DynamicToolCallResult | undefined): Record<string, unknown> {
  return (result?.structuredContent ?? {}) as Record<string, unknown>
}
