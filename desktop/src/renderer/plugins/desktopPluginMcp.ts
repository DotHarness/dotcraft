import type { DesktopPluginMcpToolCall, DesktopPluginMcpToolResult } from '@dotcraft/plugin'
import type { JsonValue } from '@dotcraft/sdk/contracts'
import type { AppServerConnectionSource } from '../../shared/appServerBoundary'
import { readAppServerErrorFields } from '../../shared/appServerError'
import { currentMcpManagementScope, isCurrentMcpManagementScope } from '../stores/mcpManagement'
import { getThreadConnection, isThreadObservationCurrent } from '../stores/threadConnectionStore'

const THREAD_NOT_FOUND = -32010

interface HiddenThread extends AppServerConnectionSource {
  id: Promise<string>
}

let hiddenThread: HiddenThread | null = null

function startHiddenThread(scope: AppServerConnectionSource): Promise<string> {
  return window.api.appServer.sendConnectionRequest(scope.connectionKey, 'thread/start',
    { identity: { channelName: 'dotcraft-desktop', userId: 'local' }, ephemeral: true, config: { approvalPolicy: 'deny' } },
    undefined, { connectionId: scope.connectionId })
    .then((result) => result.thread.id)
}

function releaseHiddenThread(entry: HiddenThread): void {
  void entry.id.then(async (threadId) => {
    if (isThreadObservationCurrent(entry)) {
      await window.api.appServer.sendConnectionRequest(entry.connectionKey, 'thread/delete', { threadId },
        undefined, { connectionId: entry.connectionId })
    }
  }).catch(() => {})
}

function hiddenThreadFor(scope: AppServerConnectionSource): HiddenThread {
  const previous = hiddenThread
  if (previous?.connectionKey === scope.connectionKey && previous.connectionId === scope.connectionId) return previous
  if (previous && previous.connectionKey !== scope.connectionKey) releaseHiddenThread(previous)
  const entry: HiddenThread = {
    ...scope,
    id: previous?.connectionKey === scope.connectionKey
      ? previous.id.then(async (threadId) => {
          try {
            await window.api.appServer.sendConnectionRequest(scope.connectionKey, 'thread/resume', { threadId },
              undefined, { connectionId: scope.connectionId })
            return threadId
          } catch (error) {
            if (readAppServerErrorFields(error).rpcCode !== THREAD_NOT_FOUND) throw error
            return startHiddenThread(scope)
          }
        })
      : startHiddenThread(scope)
  }
  hiddenThread = entry
  void entry.id.catch(() => {
    if (hiddenThread === entry) hiddenThread = null
  })
  return entry
}

function callTool(call: DesktopPluginMcpToolCall, threadId: string, scope: AppServerConnectionSource): Promise<DesktopPluginMcpToolResult> {
  return window.api.appServer.sendConnectionRequest(scope.connectionKey, 'mcpServer/tool/call', {
    server: call.server,
    tool: call.tool,
    arguments: (call.arguments ?? {}) as Record<string, JsonValue>,
    threadId
  }, undefined, { connectionId: scope.connectionId }) as Promise<DesktopPluginMcpToolResult>
}

export async function callDesktopPluginMcpTool(call: DesktopPluginMcpToolCall): Promise<DesktopPluginMcpToolResult> {
  if (call.threadId) {
    const scope = getThreadConnection(call.threadId) ?? currentMcpManagementScope()
    if (!scope || !isThreadObservationCurrent(scope)) throw new Error('AppServer connection is not available.')
    return callTool(call, call.threadId, scope)
  }
  const scope = currentMcpManagementScope()
  if (!scope) throw new Error('AppServer connection is not available.')
  const assertCurrent = (): void => {
    if (!isCurrentMcpManagementScope(scope)) throw new Error('AppServer connection changed before MCP tool dispatch.')
  }
  const entry = hiddenThreadFor(scope)
  let threadId = await entry.id
  assertCurrent()
  let result: DesktopPluginMcpToolResult
  try {
    result = await callTool(call, threadId, scope)
  } catch (error) {
    if (readAppServerErrorFields(error).rpcCode !== THREAD_NOT_FOUND || !isCurrentMcpManagementScope(scope)) throw error
    if (hiddenThread === entry) hiddenThread = null
    threadId = await hiddenThreadFor(scope).id
    assertCurrent()
    result = await callTool(call, threadId, scope)
  }
  assertCurrent()
  return result
}
