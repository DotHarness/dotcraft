import { randomUUID } from 'node:crypto'
import { STANDALONE_CHAT_INSTRUCTIONS } from '../../shared/chatFolders'
import type {
  ThreadTaskDefaults,
  ThreadTaskReasoningEffort,
  ThreadTaskStartOptions
} from '../../shared/threadTasks'

const WORKTREE_CREATION_TIMEOUT_MS = 180_000

type JsonObject = Record<string, unknown>

export interface ThreadTaskConnection {
  workspacePath: string
  request<T = unknown>(method: string, params?: unknown, timeoutMs?: number | null): Promise<T>
  createChatFolder?: (firstMessage: string) => Promise<string>
}

export interface ChildThreadWire {
  id?: string
  displayName?: string | null
  [key: string]: unknown
}

export interface ChildThreadSpec {
  title?: string
  callerThreadId?: string
  defaults: ThreadTaskDefaults
  options?: ThreadTaskStartOptions
  folderSeed: string
}

export function desktopIdentity(workspacePath: string): JsonObject {
  return {
    channelName: 'dotcraft-desktop',
    userId: 'local',
    channelContext: `workspace:${workspacePath}`,
    workspacePath
  }
}

export function buildReasoningConfig(effort: ThreadTaskReasoningEffort, existing?: Record<string, unknown>): JsonObject {
  const output = [existing?.output, existing?.Output].find((value) => typeof value === 'string' && value.trim() !== '')
  return { enabled: true, effort, output: output ?? 'full' }
}

export function composeBrief(prompt: string, background?: string): string {
  return background?.trim()
    ? `Background (from the chat that started this one):\n${background.trim()}\n\nTask:\n${prompt}`
    : prompt
}

export async function createChildThread(connection: ThreadTaskConnection, spec: ChildThreadSpec): Promise<ChildThreadWire> {
  if (!connection.workspacePath.trim()) {
    throw new Error('No Desktop workspace is currently open.')
  }
  const options = spec.options ?? {}
  const config: JsonObject = {}
  const model = options.model ?? spec.defaults.model
  if (model) {
    config.model = model
    if (options.providerId && options.model) config.providerId = options.providerId
  }
  const effort = options.reasoningEffort ?? spec.defaults.reasoningEffort
  if (effort === 'off') config.reasoning = { enabled: false, output: 'full' }
  else if (effort) config.reasoning = buildReasoningConfig(effort)
  if (options.approvalPolicy) config.approvalPolicy = options.approvalPolicy

  const params: JsonObject = {
    identity: desktopIdentity(connection.workspacePath),
    historyMode: 'server',
    ...(spec.title ? { displayName: spec.title } : {})
  }

  if (options.workLocation === 'worktree') {
    const result = await connection.request<{ thread?: ChildThreadWire; setup?: { status?: string; outputTail?: string } }>(
      'worktree/createAndStart',
      {
        ...params,
        ...(Object.keys(config).length > 0 ? { config } : {}),
        ...(options.baseRef ? { baseRef: options.baseRef } : {}),
        ...(options.branchName ? { branchName: options.branchName } : {}),
        ...(options.environmentPath ? { environmentPath: options.environmentPath } : {}),
        operationId: randomUUID()
      },
      options.environmentPath ? null : WORKTREE_CREATION_TIMEOUT_MS
    )
    const thread = requireThread(result.thread, 'worktree/createAndStart')
    if (result.setup?.status === 'failed') {
      const tail = result.setup.outputTail?.trim()
      throw new Error(tail ? `The worktree environment setup failed: ${tail}` : 'The worktree environment setup failed.')
    }
    return thread
  }

  const callerThreadId = spec.callerThreadId?.trim()
  if (callerThreadId) params.spawnedFromThreadId = callerThreadId
  if (connection.createChatFolder) {
    params.cwd = await connection.createChatFolder(spec.folderSeed)
    config.developerInstructions = STANDALONE_CHAT_INSTRUCTIONS
  }
  if (Object.keys(config).length > 0) params.config = config
  const result = await connection.request<{ thread?: ChildThreadWire }>('thread/start', params)
  return requireThread(result.thread, 'thread/start')
}

export async function startChildTurn(
  connection: ThreadTaskConnection,
  threadId: string,
  text: string
): Promise<JsonObject | null> {
  const result = await connection.request<{ turn?: JsonObject }>('turn/start', {
    threadId,
    input: [{ type: 'text', text }],
    identity: desktopIdentity(connection.workspacePath)
  })
  return result.turn ?? null
}

function requireThread(thread: ChildThreadWire | undefined, method: string): ChildThreadWire {
  if (!thread?.id) throw new Error(`${method} did not return a thread id.`)
  return thread
}
