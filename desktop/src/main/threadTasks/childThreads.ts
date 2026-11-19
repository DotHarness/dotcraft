import { randomUUID } from 'node:crypto'
import type { ThreadProject, ThreadProjectHost } from './threadProjects'
import type { AppServerRequestClient } from '../desktopRuntimeThreadTools'
import { STANDALONE_CHAT_INSTRUCTIONS } from '../../shared/chatFolders'
import { TASK_LIST_INSTRUCTIONS, withTaskListInstructions } from '../../shared/taskListInstructions'
import type {
  ThreadTaskDefaults,
  ThreadTaskReasoningEffort,
  ThreadTaskStartOptions
} from '../../shared/threadTasks'

const WORKTREE_CREATION_TIMEOUT_MS = 180_000

type JsonObject = Record<string, unknown>

export interface ThreadTaskWorkspace {
  cwd: string
  runtimeWorkspaceRoots: string[]
}

export interface ThreadTaskConnection {
  ownerId?: number
  workspacePath: string
  client?: AppServerRequestClient
  project?: ThreadProject
  projects?: ThreadProjectHost
  remote?: boolean
  getProjectWorkspace?: () => ThreadTaskWorkspace | undefined
  request<T = unknown>(method: string, params?: unknown, timeoutMs?: number | null): Promise<T>
  createChatFolder?: (firstMessage: string) => Promise<string>
}

interface ChildThreadWire {
  id?: string
  displayName?: string | null
  [key: string]: unknown
}

export interface ChildThread extends ChildThreadWire {
  id: string
  workspacePath: string
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

export async function createChildThread(connection: ThreadTaskConnection, spec: ChildThreadSpec): Promise<ChildThread> {
  if (!connection.workspacePath.trim()) {
    throw new Error('No Desktop workspace is currently open.')
  }
  const options = spec.options ?? {}
  const workspace = connection.getProjectWorkspace?.()
  const config: JsonObject = { developerInstructions: TASK_LIST_INSTRUCTIONS }
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
    ...workspace,
    ...(spec.title ? { displayName: spec.title } : {})
  }

  if (options.workLocation === 'worktree') {
    const result = await connection.request<{ thread?: ChildThreadWire; setup?: { status?: string; outputTail?: string } }>(
      'worktree/createAndStart',
      {
        ...params,
        config,
        ...(options.baseRef ? { baseRef: options.baseRef } : {}),
        ...(options.branchName ? { branchName: options.branchName } : {}),
        ...(options.environmentPath ? { environmentPath: options.environmentPath } : {}),
        operationId: randomUUID()
      },
      options.environmentPath ? null : WORKTREE_CREATION_TIMEOUT_MS
    )
    const thread = requireThread(result.thread, connection.workspacePath, 'worktree/createAndStart')
    if (result.setup?.status === 'failed') {
      await connection.request('thread/unsubscribe', { threadId: thread.id })
      const tail = result.setup.outputTail?.trim()
      throw new Error(tail ? `The worktree environment setup failed: ${tail}` : 'The worktree environment setup failed.')
    }
    return thread
  }

  const callerThreadId = spec.callerThreadId?.trim()
  if (callerThreadId) params.spawnedFromThreadId = callerThreadId
  if (connection.createChatFolder && !workspace) {
    params.cwd = await connection.createChatFolder(spec.folderSeed)
    config.developerInstructions = withTaskListInstructions(STANDALONE_CHAT_INSTRUCTIONS)
  }
  params.config = config
  const result = await connection.request<{ thread?: ChildThreadWire }>('thread/start', params)
  return requireThread(result.thread, connection.workspacePath, 'thread/start')
}

export async function startChildTurn(
  connection: ThreadTaskConnection,
  threadId: string,
  text: string
): Promise<JsonObject | null> {
  try {
    const result = await connection.request<{ turn?: JsonObject }>('turn/start', {
      threadId,
      input: [{ type: 'text', text }],
      identity: desktopIdentity(connection.workspacePath)
    })
    return result.turn ?? null
  } catch (error) {
    await connection.request('thread/unsubscribe', { threadId })
    throw error
  }
}

function requireThread(thread: ChildThreadWire | undefined, workspacePath: string, method: string): ChildThread {
  if (!thread?.id) throw new Error(`${method} did not return a thread id.`)
  return { ...thread, id: thread.id, workspacePath }
}
