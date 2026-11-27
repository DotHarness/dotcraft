import type { ThreadTaskConnection } from './childThreads'
import { desktopIdentity } from './childThreads'
import type { DesktopLinkedChats, LinkedChatRef } from './linkedChats'
import type { LinkedChatWait } from './linkedChatStore'
import { connectThreadProject, currentThreadProject, type ThreadProject } from './threadProjects'

type JsonObject = Record<string, unknown>

export interface ActiveThreadSummaryWire {
  id?: string
  displayName?: string | null
  status?: string
  originChannel?: string
  createdAt?: string
  lastActiveAt?: string
  source?: Record<string, unknown> | null
  runtime?: unknown
  goal?: unknown
}

export interface ActiveThreadClassification {
  activeState: 'needsUser' | 'working'
  waitingFor?: LinkedChatWait
}

export interface ListActiveThreadsOptions {
  connection: ThreadTaskConnection
  linkedChats: DesktopLinkedChats | undefined
  parent: LinkedChatRef | undefined
  project?: ThreadProject
  query?: string
  limit: number
  maxPerProjectLimit: number
  summarize: (thread: ActiveThreadSummaryWire) => JsonObject
  requestClient: (target: ThreadTaskConnection) => { sendRequest<T = unknown>(method: string, params?: unknown): Promise<T> }
}

export function classifyActiveThread(thread: ActiveThreadSummaryWire): ActiveThreadClassification | null {
  if (thread.status === 'archived') return null
  const runtime = isRecord(thread.runtime) ? thread.runtime : null
  if (!runtime) return null

  if (runtime.waitingOnApproval === true) return { activeState: 'needsUser', waitingFor: 'approval' }
  if (runtime.waitingOnPlanConfirmation === true) return { activeState: 'needsUser', waitingFor: 'planConfirmation' }
  if (runtime.waitingOnInput === true) return { activeState: 'needsUser', waitingFor: 'input' }
  const hasActiveTurn = typeof runtime.activeTurnId === 'string' && runtime.activeTurnId.trim() !== ''
  if (runtime.running === true || runtime.busy === true || runtime.hasBackgroundWork === true || hasActiveTurn) {
    return { activeState: 'working' }
  }
  return null
}

export async function listActiveThreadsAcrossProjects(
  options: ListActiveThreadsOptions
): Promise<{ threads: JsonObject[]; totalMatched: number }> {
  const projects = options.project
    ? [options.project]
    : (options.connection.remote
        ? [currentThreadProject(options.connection)]
        : (await options.connection.projects?.list() ?? [currentThreadProject(options.connection)]).filter(p => p.running))

  const results = await Promise.allSettled(
    projects.map(project => listProjectActiveThreads(project, options))
  )

  const collected: JsonObject[] = []
  let firstError: unknown
  let succeeded = 0
  for (const result of results) {
    if (result.status === 'fulfilled') {
      succeeded++
      collected.push(...result.value)
    } else if (firstError === undefined) {
      firstError = result.reason
    }
  }
  if (succeeded === 0 && firstError !== undefined) throw firstError

  collected.sort(compareActiveThreads)
  return {
    threads: collected.slice(0, options.limit),
    totalMatched: collected.length
  }
}

async function listProjectActiveThreads(
  project: ThreadProject,
  options: ListActiveThreadsOptions
): Promise<JsonObject[]> {
  const target = await connectThreadProject(options.connection, project)
  const request: JsonObject = {
    identity: desktopIdentity(target.workspacePath),
    scope: 'workspace',
    includeSubAgents: false,
    includeArchived: false,
    limit: options.maxPerProjectLimit
  }
  if (options.query) request.query = options.query

  const response = await options.requestClient(target).sendRequest<{
    data?: ActiveThreadSummaryWire[]
  }>('thread/list', request)

  const items: JsonObject[] = []
  for (const thread of response.data ?? []) {
    const id = typeof thread.id === 'string' ? thread.id : ''
    if (!id) continue
    if (options.parent && project.id === options.parent.projectId && id === options.parent.threadId) continue
    const active = classifyActiveThread(thread)
    if (!active) continue
    const link = options.parent && options.linkedChats
      ? options.linkedChats.find(options.parent, id, project.id)
      : undefined
    items.push({
      ...options.summarize(thread),
      project: project.id,
      projectName: project.name,
      workspacePath: target.workspacePath,
      activeState: active.activeState,
      ...(active.waitingFor ? { waitingFor: active.waitingFor } : {}),
      linked: Boolean(link),
      ...(link ? { startedHere: !link.followed } : {})
    })
  }
  return items
}

function compareActiveThreads(a: JsonObject, b: JsonObject): number {
  const rankA = a.activeState === 'needsUser' ? 0 : 1
  const rankB = b.activeState === 'needsUser' ? 0 : 1
  if (rankA !== rankB) return rankA - rankB
  return timestampMs(b.lastActiveAt ?? b.createdAt) - timestampMs(a.lastActiveAt ?? a.createdAt)
}

function timestampMs(value: unknown): number {
  if (typeof value !== 'string') return 0
  const parsed = Date.parse(value)
  return Number.isNaN(parsed) ? 0 : parsed
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return value != null && typeof value === 'object' && !Array.isArray(value)
}
