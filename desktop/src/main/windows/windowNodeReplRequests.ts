import { randomUUID } from 'node:crypto'
import type { BrowserWindow } from 'electron'
import { nodeReplManager, type NodeReplApprovalRequest } from '../nodeReplManager'
import type { DesktopWindowRuntime } from './windowRuntime'
import type { WorkspaceConnectionEntry } from './windowTypes'

interface ThreadBinding {
  entry: WorkspaceConnectionEntry
  owner: BrowserWindow
  sessionId: string
  turnId?: string
  evaluations: Set<string>
}

const bindings = new Map<string, ThreadBinding>()
const observedClients = new WeakSet<WorkspaceConnectionEntry['client']>()

export function isWorkspaceNodeReplRequest(method: string): boolean {
  return method === 'ext/nodeRepl/evaluate' || method === 'ext/nodeRepl/cancel'
}

export function isCurrentWorkspaceRequestSource(ctx: DesktopWindowRuntime, entry: WorkspaceConnectionEntry): boolean {
  return !ctx.runtimeIsStopping() && entry.connected && !entry.client.isDisposed && ctx.workspaceConnections.get(entry.key) === entry &&
    ctx.mainWindow != null && !ctx.mainWindow.isDestroyed()
}

export function ownsWorkspaceNodeReplThread(entry: WorkspaceConnectionEntry, threadId: string): boolean {
  return bindings.get(threadId)?.entry === entry
}

export function releaseWorkspaceNodeReplBindings(entry: WorkspaceConnectionEntry): void {
  for (const [threadId, binding] of bindings) {
    if (binding.entry !== entry) continue
    bindings.delete(threadId)
    nodeReplManager.reset(threadId, { preserveBrowserTabs: true })
  }
}

export function handleWorkspaceNodeReplNotification(entry: WorkspaceConnectionEntry, method: string, params: unknown): void {
  if (method !== 'thread/deleted' || !params || typeof params !== 'object') return
  const threadId = (params as { threadId?: unknown }).threadId
  if (typeof threadId !== 'string') return
  const binding = bindings.get(threadId)
  if (binding?.entry !== entry) return
  bindings.delete(threadId)
  nodeReplManager.reset(threadId)
  if (binding.turnId) void entry.client.releaseBrowserTurn(threadId, binding.turnId).catch(console.warn)
}

function bindThread(entry: WorkspaceConnectionEntry, owner: BrowserWindow, threadId: string, sessionId: string): ThreadBinding {
  const existing = bindings.get(threadId)
  if (existing && (existing.entry !== entry || existing.owner !== owner || existing.sessionId !== sessionId)) {
    throw new Error('Node REPL thread is already bound to another workspace or window.')
  }
  for (const [id, binding] of bindings) {
    if (id !== threadId && binding.sessionId === sessionId) {
      throw new Error('Browser session is already bound to another thread.')
    }
  }
  if (existing) return existing
  if (!observedClients.has(entry.client)) {
    observedClients.add(entry.client)
    entry.client.on('close', () => releaseWorkspaceNodeReplBindings(entry))
    entry.client.once('disposed', () => releaseWorkspaceNodeReplBindings(entry))
  }
  const binding: ThreadBinding = { entry, owner, sessionId, evaluations: new Set() }
  bindings.set(threadId, binding)
  return binding
}

export async function handleWorkspaceNodeReplRequest(
  ctx: DesktopWindowRuntime,
  entry: WorkspaceConnectionEntry,
  method: string,
  params: unknown
): Promise<unknown> {
  const p = (params ?? {}) as {
    threadId?: unknown; turnId?: string; evaluationId?: string; browserSession?: Record<string, unknown>
    code?: unknown; timeoutMs?: number
  }
  if (typeof p.threadId !== 'string' || !p.threadId.trim()) {
    return method === 'ext/nodeRepl/cancel' ? { ok: false } : { error: 'Invalid Node REPL evaluate request.', images: [], logs: [] }
  }
  const threadId = p.threadId
  if (method === 'ext/nodeRepl/cancel') {
    const binding = bindings.get(threadId)
    return binding?.entry === entry && binding.owner === ctx.mainWindow &&
      typeof p.evaluationId === 'string' && binding.evaluations.has(p.evaluationId)
      ? nodeReplManager.cancel(threadId, p.evaluationId) : { ok: false }
  }
  const foreground = entry.role === 'foreground' && ctx.wireClient === entry.client
  if (!foreground && (entry.kind === 'remote' || ctx.activeRemoteProject != null)) {
    throw new Error('Node REPL request is not routed to an active local workspace connection.')
  }
  if (typeof p.code !== 'string' || (p.browserSession?.threadId != null && p.browserSession.threadId !== threadId)) {
    return { error: 'Invalid Node REPL evaluate request.', images: [], logs: [] }
  }
  const owner = ctx.mainWindow!
  const sessionId = typeof p.browserSession?.sessionId === 'string' ? p.browserSession.sessionId : threadId
  const binding = bindThread(entry, owner, threadId, sessionId)
  const evaluationId = p.evaluationId || randomUUID()
  if (binding.evaluations.has(evaluationId)) throw new Error('Node REPL evaluation is already in flight.')
  binding.evaluations.add(evaluationId)
  const client = entry.client
  const workspacePath = entry.localWorkspacePath ?? entry.workspacePath
  try {
    const turnId = p.browserSession?.turnId ?? p.turnId
    if (typeof turnId === 'string') {
      binding.turnId = turnId
      await client.retainBrowserTurn(threadId, turnId)
    }
    if (!isCurrentWorkspaceRequestSource(ctx, entry) || ctx.mainWindow !== owner || bindings.get(threadId) !== binding) {
      throw new Error('Workspace request source changed before Node REPL evaluation.')
    }
    return await nodeReplManager.evaluate(owner, {
      threadId, turnId: p.turnId, evaluationId, browserSession: p.browserSession, code: p.code,
      timeoutMs: p.timeoutMs, workspacePath, projectKey: entry.projectId,
      requestApproval: async (request: NodeReplApprovalRequest) => {
        if (!isCurrentWorkspaceRequestSource(ctx, entry) || ctx.mainWindow !== owner) return false
        const result = await client.sendRequest<{ approved?: boolean }>(
          'ext/nodeRepl/requestApproval', { threadId, ...request }, null
        )
        return result?.approved === true
      }
    })
  } finally {
    binding.evaluations.delete(evaluationId)
  }
}
