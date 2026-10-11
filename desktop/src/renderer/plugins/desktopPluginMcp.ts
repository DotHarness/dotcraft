import type { DesktopPluginMcpToolCall, DesktopPluginMcpToolResult } from '@dotcraft/plugin'

import { readAppServerErrorFields } from '../../shared/appServerError'
import { useConnectionStore } from '../stores/connectionStore'

const THREAD_NOT_FOUND = -32010

interface HiddenThread {
  epoch: number
  id: Promise<string>
  resolvedId?: string
}

let hiddenThread: HiddenThread | null = null

function connectionEpoch(): number {
  return useConnectionStore.getState().connectionEpoch
}

function hiddenThreadId(): Promise<string> {
  const epoch = connectionEpoch()
  if (hiddenThread?.epoch === epoch) return hiddenThread.id
  const entry: HiddenThread = {
    epoch,
    id: window.api.appServer
      .sendRequestRaw('thread/start', { ephemeral: true, config: { approvalPolicy: 'deny' } })
      .then((result) => {
        entry.resolvedId = (result as { thread: { id: string } }).thread.id
        return entry.resolvedId
      })
  }
  hiddenThread = entry
  entry.id.catch(() => {
    if (hiddenThread === entry) hiddenThread = null
  })
  return entry.id
}

function forgetHiddenThread(threadId: string): void {
  if (hiddenThread?.resolvedId === threadId) hiddenThread = null
}

function callTool(call: DesktopPluginMcpToolCall, threadId: string): Promise<DesktopPluginMcpToolResult> {
  return window.api.appServer.sendRequestRaw('mcpServer/tool/call', {
    server: call.server,
    tool: call.tool,
    arguments: call.arguments ?? {},
    threadId
  }) as Promise<DesktopPluginMcpToolResult>
}

export async function callDesktopPluginMcpTool(call: DesktopPluginMcpToolCall): Promise<DesktopPluginMcpToolResult> {
  if (call.threadId) return callTool(call, call.threadId)
  const epoch = connectionEpoch()
  const threadId = await hiddenThreadId()
  if (connectionEpoch() !== epoch) throw new Error('AppServer connection changed before MCP tool dispatch.')
  try {
    return await callTool(call, threadId)
  } catch (error) {
    const threadGone = readAppServerErrorFields(error).rpcCode === THREAD_NOT_FOUND
    if (!threadGone || connectionEpoch() !== epoch) throw error
    forgetHiddenThread(threadId)
    const retryThreadId = await hiddenThreadId()
    if (connectionEpoch() !== epoch) throw error
    return callTool(call, retryThreadId)
  }
}
