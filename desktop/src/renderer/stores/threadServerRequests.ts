import { create } from 'zustand'
import type { RawServerRequestPayload } from '../../shared/appServerBoundary'
import type { McpElicitationRequest } from '../components/mcp/McpElicitationDialog'
import { useConnectionStore } from './connectionStore'
import { getThreadConversation } from './conversationStore'
import { getThreadConnection, matchesThreadConnection } from './threadConnectionStore'
import { useThreadStore } from './threadStore'
import { isThreadRestoring } from './threadRestoration'

interface Elicitation extends McpElicitationRequest {
  threadId: string
  turnId: string | null
  wireRequestId: string | number
  connectionKey?: string
  connectionId?: string
}

export const useThreadElicitationStore = create<{ requests: Elicitation[] }>(() => ({ requests: [] }))

export function dismissThreadElicitation(bridgeId: string): void {
  useThreadElicitationStore.setState((state) => ({
    requests: state.requests.filter((request) => request.bridgeId !== bridgeId)
  }))
}

export function applyThreadServerRequest(payload: RawServerRequestPayload): boolean {
  const { bridgeId, method, wireRequestId } = payload
  if (
    method !== 'item/approval/request' &&
    method !== 'item/tool/requestUserInput' &&
    method !== 'mcpServer/elicitation/request'
  )
    return false
  const p = (payload.params ?? {}) as Record<string, unknown>
  const threadId = typeof p.threadId === 'string' ? p.threadId : null
  const target = threadId ? getThreadConnection(threadId) : undefined
  if (target && !matchesThreadConnection(threadId!, payload)) return true
  const foreground = useConnectionStore.getState()
  const selected =
    useThreadStore.getState().activeThreadId === threadId &&
    (payload.connectionId == null || payload.connectionId === foreground.connectionId)
  if (method === 'mcpServer/elicitation/request') {
    const mode = p.mode === 'url' ? 'url' : p.mode === 'form' ? 'form' : null
    if (!mode || !threadId || wireRequestId == null) {
      void window.api.appServer.sendServerResponse(bridgeId, {
        action: 'decline'
      })
      return true
    }
    useThreadElicitationStore.setState((state) => ({
      requests: [
        ...state.requests,
        {
          bridgeId,
          threadId,
          turnId: typeof p.turnId === 'string' ? p.turnId : null,
          wireRequestId,
          connectionKey: payload.connectionKey,
          connectionId: payload.connectionId,
          serverName: typeof p.serverName === 'string' ? p.serverName : 'MCP',
          mode,
          message: typeof p.message === 'string' ? p.message : undefined,
          url: typeof p.url === 'string' ? p.url : undefined,
          requestedSchema: p.requestedSchema
        }
      ]
    }))
    return true
  }
  const conversation = threadId ? getThreadConversation(threadId)?.getState() : undefined
  const turnId = typeof p.turnId === 'string' ? p.turnId : null
  if (!selected || !conversation || isThreadRestoring(threadId)) {
    if (threadId) {
      const request = {
        bridgeId,
        turnId,
        rawParams: p,
        connectionId: payload.connectionId
      }
      if (method === 'item/approval/request') useThreadStore.getState().parkApproval(threadId, request)
      else useThreadStore.getState().parkUserInput(threadId, request)
    }
    return true
  }
  if (method === 'item/approval/request') conversation.onApprovalRequest(bridgeId, p)
  else conversation.onUserInputRequest(bridgeId, p)
  return true
}
