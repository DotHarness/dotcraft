import { selectedWorktreeEnvironment } from '../components/projectActions/worktreeEnvironments'
import { useUIStore } from '../stores/uiStore'
import {
  useWorktreeCreationStore,
  type WorktreeCreationEnvironment,
  type WorktreeCreationResult
} from '../stores/worktreeCreationStore'
import type { ThreadSummary } from '../types/thread'

const WORKTREE_CREATION_TIMEOUT_MS = 180_000

type RequestParams = Record<string, unknown>

async function environmentFor(workspacePath: string, local: boolean): Promise<WorktreeCreationEnvironment | null> {
  if (!local || !workspacePath) return null
  return selectedWorktreeEnvironment(workspacePath).catch(() => null)
}

function creationParams(params: RequestParams, operationId: string, environment: WorktreeCreationEnvironment | null): RequestParams {
  return { ...params, operationId, ...(environment ? { environmentPath: environment.path } : {}) }
}

async function threadOf(method: string, params: RequestParams, timeoutMs?: number | null): Promise<ThreadSummary> {
  const result = await window.api.appServer.sendRequestRaw(method, params, timeoutMs) as { thread?: ThreadSummary }
  if (!result.thread?.id) throw new Error(`${method} did not return a thread`)
  return result.thread
}

export async function createWelcomeWorktreeThread({
  workspacePath,
  local,
  params,
  localParams
}: {
  workspacePath: string
  local: boolean
  params: RequestParams
  localParams: RequestParams
}): Promise<ThreadSummary> {
  const anchorId = useUIStore.getState().pendingThreadCreation?.requestId ?? `welcome-${crypto.randomUUID()}`
  const environment = await environmentFor(workspacePath, local)
  return useWorktreeCreationStore.getState().run<ThreadSummary>({
    anchorId,
    workspacePath,
    environment,
    create: async (operationId) => await window.api.appServer.sendRequestRaw(
      'worktree/createAndStart',
      creationParams(params, operationId, environment),
      environment ? null : WORKTREE_CREATION_TIMEOUT_MS
    ) as WorktreeCreationResult<ThreadSummary>,
    createWithoutWorktree: () => threadOf('thread/start', localParams)
  })
}

export async function createForkWorktreeThread({
  sourceThreadId,
  workspacePath,
  local,
  params,
  localParams
}: {
  sourceThreadId: string
  workspacePath: string
  local: boolean
  params: RequestParams
  localParams: RequestParams
}): Promise<ThreadSummary> {
  const environment = await environmentFor(workspacePath, local)
  return useWorktreeCreationStore.getState().run<ThreadSummary>({
    anchorId: sourceThreadId,
    workspacePath,
    environment,
    create: async (operationId) => await window.api.appServer.sendRequestRaw(
      'worktree/createAndFork',
      creationParams(params, operationId, environment),
      environment ? null : WORKTREE_CREATION_TIMEOUT_MS
    ) as WorktreeCreationResult<ThreadSummary>,
    createWithoutWorktree: () => threadOf('thread/fork', localParams)
  })
}
