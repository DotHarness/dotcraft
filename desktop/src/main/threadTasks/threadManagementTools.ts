import type { AppServerRequestClient, DesktopThreadToolSettingsHost, DynamicToolCallResult } from '../desktopRuntimeThreadTools'
import { submitThreadMessage } from '../desktopThreadMessageTool'
import { normalizeWorkspaceProjectKey } from '../../shared/workspaceProjectKey'
import { desktopIdentity, type ThreadTaskConnection } from './childThreads'
import type { DesktopLinkedChats, LinkedChatChild, LinkedChatRef, LinkedTurn, ThreadHeaderResult } from './linkedChats'
import { callingThread, connectThreadProject, currentThreadProject, readProjectArgument, resolveThreadProject } from './threadProjects'

export const MAX_LIST_THREADS_LIMIT = 100
const DEFAULT_LIST_THREADS_LIMIT = 20
const REASONING_EFFORT_VALUES = new Set(['low', 'medium', 'high', 'extraHigh', 'max', 'ultra'])

type JsonObject = Record<string, unknown>
type ReasoningEffortValue = 'low' | 'medium' | 'high' | 'extraHigh' | 'max' | 'ultra'

interface ThreadSummaryWire {
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

export async function handleThreadManagementTool(
  connection: ThreadTaskConnection,
  linkedChats: DesktopLinkedChats | undefined,
  settingsHost: DesktopThreadToolSettingsHost | undefined,
  tool: string,
  args: JsonObject,
  callerId?: string
): Promise<DynamicToolCallResult> {
  const parent = callerId ? callingThread(connection, callerId) : undefined
  if (tool === 'ListProjects') {
    const projects = connection.remote ? [currentThreadProject(connection)] :
      await connection.projects?.list() ?? [currentThreadProject(connection)]
    return ok(projects.map(project => {
      const machine = project.machine ? ` on ${project.machine}` : ''
      const extra = project.secondaryFolders?.length
        ? `, +${project.secondaryFolders.length} folder${project.secondaryFolders.length === 1 ? '' : 's'}`
        : ''
      return `- ${project.id}: ${project.name} (${project.primaryFolder ?? project.path}${extra})${machine} [${project.running ? 'running' : 'stopped'}]`
    }).join('\n'), { projects })
  }
  if (tool === 'ListThreads') {
    const linked = optionalBoolean(args, 'linked', false)
    if (!linked.ok) return linked.error
    if (linked.value) {
      const threads = parent && linkedChats ? linkedChats.list(parent).map(({ child, createdAt, followed, status }) => ({
        id: child.threadId, displayName: child.title, project: child.projectId, projectName: child.projectName,
        workspacePath: child.workspacePath, createdAt, status, startedHere: !followed
      })) : []
      return threadListResult(threads, threads.length, null)
    }
    const target = await connectThreadProject(connection, await resolveThreadProject(connection, readProjectArgument(args.project)))
    return listThreadsTool(requestClient(target), args, target.workspacePath)
  }

  const threadId = requiredNonEmptyString(args, 'threadId')
  if (!threadId.ok) return threadId.error
  if (tool === 'FollowThread' || tool === 'UnfollowThread') {
    if (!parent || !linkedChats) return fail('ThreadManagementUnavailable', 'Desktop chat following is unavailable.')
    return changeThreadFollow(connection, linkedChats, parent, threadId.value, readProjectArgument(args.project), tool === 'FollowThread')
  }
  if (tool === 'StopThread') {
    const projectId = readProjectArgument(args.project)
    if (threadId.value === callerId) return fail('NotLinked', 'A chat cannot stop itself.')
    const link = parent && linkedChats?.find(parent, threadId.value, projectId)
    if (!link || link.followed || !linkedChats) return fail('NotLinked', 'Only chats started by this chat can be stopped.')
    const target = await connectThreadProject(connection, linkedProject(link.child))
    const client = requestClient(target)
    const { thread } = await client.sendRequest<ThreadHeaderResult>('thread/read', { threadId: threadId.value })
    if (!thread) return fail('ThreadNotFound', `Thread '${threadId.value}' was not found.`)
    const turnId = thread.runtime?.activeTurnId
    const result = { threadId: threadId.value, ...targetRef(target), stopped: Boolean(turnId) }
    if (!turnId) return ok(`Chat "${link.child.title}" is idle; nothing was stopped.`, result)
    linkedChats.markStopped(link, turnId)
    try {
      await client.sendRequest('turn/interrupt', { threadId: threadId.value, turnId })
    } catch (error) {
      linkedChats.clearStopped(link, turnId)
      throw error
    }
    return ok(`Stopped the active turn in "${link.child.title}".`, result)
  }

  const target = await resolveTargetThread(connection, linkedChats, args, callerId)
  const client = requestClient(target)
  switch (tool) {
    case 'SendMessageToThread':
      return withWorkspace(await sendMessageToThreadTool(client, threadId.value, args), target)
    case 'SetThreadTitle':
      return withWorkspace(await setThreadTitleTool(client, threadId.value, args), target)
    case 'SetThreadArchived':
      return withWorkspace(await setThreadArchivedTool(client, threadId.value, args), target)
    case 'SetThreadPinned':
      return withWorkspace(await setThreadPinnedTool(client, threadId.value, args, currentThreadProject(target).id, settingsHost), target)
    default:
      return fail('UnsupportedTool', `Desktop tool '${tool}' is not supported.`)
  }
}

async function changeThreadFollow(
  source: ThreadTaskConnection,
  linkedChats: DesktopLinkedChats,
  parent: LinkedChatRef,
  threadId: string,
  projectId: string | undefined,
  follow: boolean
): Promise<DynamicToolCallResult> {
  const existing = linkedChats.find(parent, threadId, projectId)
  if (!follow) {
    if (!existing) return ok('This chat was not followed.', { threadId, following: false, changed: false })
    if (!linkedChats.unfollow(existing)) {
      return followResult(existing.child, 'Still following a chat started here', true, false)
    }
    return followResult(existing.child, 'Stopped following', false, true)
  }
  if (existing) return followResult(existing.child, 'Already following', true, false)
  const project = await resolveThreadProject(source, projectId)
  if (parent.projectId === project.id && parent.threadId === threadId) return fail('TargetUnsupported', 'A chat cannot follow itself.')
  const target = await connectThreadProject(source, project)
  const client = requestClient(target)
  const { thread } = await client.sendRequest<ThreadHeaderResult>('thread/read', { threadId })
  if (!thread) return fail('ThreadNotFound', `Thread '${threadId}' was not found.`)
  if (thread.status === 'archived') return fail('ThreadArchived', 'Archived chats cannot be followed.')
  const child: LinkedChatChild = { ...callingThread(target, threadId), projectName: project.name, title: thread.displayName || threadId }
  const latest = thread.runtime?.activeTurnId || thread.runtime?.hasBackgroundWork ? undefined :
    (await client.sendRequest<{ data: LinkedTurn[] }>('thread/turns/list', {
      threadId, limit: 1, sortDirection: 'descending', itemsView: 'none'
    })).data[0]
  const result = linkedChats.follow(parent, child, latest)
  if (result === 'cycle') return fail('TargetUnsupported', 'Following this chat would create a cycle of reports.')
  return followResult(child, result === 'followed' ? 'Now following' : 'Already following', true, result === 'followed')
}

function followResult(child: LinkedChatChild, message: string, following: boolean, changed: boolean): DynamicToolCallResult {
  const { threadId, projectId, title } = child
  return ok(`${message}: "${title}".`, { threadId, projectId, following, changed })
}

export async function resolveTargetThread(
  source: ThreadTaskConnection,
  links: DesktopLinkedChats | undefined,
  args: JsonObject,
  callerId?: string
): Promise<ThreadTaskConnection> {
  const projectId = readProjectArgument(args.project)
  const link = callerId && typeof args.threadId === 'string' && links?.find(callingThread(source, callerId), args.threadId.trim(), projectId)
  return connectThreadProject(source, link ? linkedProject(link.child) : await resolveThreadProject(source, projectId))
}

function linkedProject(child: LinkedChatChild) {
  return { id: child.projectId, name: child.projectName, path: child.workspacePath }
}

export function requestClient(connection: ThreadTaskConnection): AppServerRequestClient {
  return connection.client ?? { sendRequest: (method, params, timeout) => connection.request(method, params, timeout) }
}

function withWorkspace(result: DynamicToolCallResult, target: ThreadTaskConnection): DynamicToolCallResult {
  return result.success && isRecord(result.structuredContent)
    ? { ...result, structuredContent: { ...result.structuredContent, ...targetRef(target) } } : result
}

function targetRef(target: ThreadTaskConnection) {
  return { workspacePath: target.workspacePath, projectId: currentThreadProject(target).id }
}

async function listThreadsTool(
  client: AppServerRequestClient,
  args: Record<string, unknown>,
  workspacePath: string
): Promise<DynamicToolCallResult> {
  if (!workspacePath.trim()) {
    return fail('ThreadManagementUnavailable', 'No Desktop workspace is currently open.')
  }

  const query = optionalString(args, 'query')
  if (query?.ok === false) return query.error
  const limit = optionalInteger(args, 'limit', DEFAULT_LIST_THREADS_LIMIT, MAX_LIST_THREADS_LIMIT)
  if (limit.ok === false) return limit.error
  const cursor = optionalString(args, 'cursor')
  if (cursor?.ok === false) return cursor.error
  const includeArchived = optionalBoolean(args, 'includeArchived', false)
  if (includeArchived.ok === false) return includeArchived.error

  const request: JsonObject = {
    identity: desktopIdentity(workspacePath),
    scope: 'workspace',
    includeSubAgents: false,
    includeArchived: includeArchived.value,
    limit: limit.value
  }
  if (query?.value) request.query = query.value
  if (cursor?.value) request.cursor = cursor.value

  const result = await client.sendRequest<{
    data?: ThreadSummaryWire[]
    nextCursor?: string | null
    totalMatched?: number | null
  }>('thread/list', request)
  const threads = (result.data ?? []).map(summarizeThread)
  return threadListResult(threads, result.totalMatched ?? threads.length, result.nextCursor ?? null)
}

function threadListResult(threads: JsonObject[], totalMatched: number, nextCursor: string | null): DynamicToolCallResult {
  const text = threads.length === 0
    ? 'No matching threads were found.'
    : `Found ${threads.length} thread${threads.length === 1 ? '' : 's'}${nextCursor ? ' (more available)' : ''}:\n${threads.map(formatThreadSummaryLine).join('\n')}`
  return ok(text, { threads, count: threads.length, totalMatched, nextCursor })
}

async function sendMessageToThreadTool(
  client: AppServerRequestClient,
  threadId: string,
  args: Record<string, unknown>
): Promise<DynamicToolCallResult> {
  const prompt = requiredNonEmptyString(args, 'prompt')
  if (prompt.ok === false) return prompt.error
  const model = optionalNonEmptyString(args, 'model')
  if (model?.ok === false) return model.error
  const reasoningEffort = optionalReasoningEffort(args, 'reasoningEffort')
  if (reasoningEffort?.ok === false) return reasoningEffort.error
  return submitThreadMessage(client, threadId, prompt.value, model?.value, reasoningEffort?.value)
}

async function setThreadTitleTool(
  client: AppServerRequestClient,
  threadId: string,
  args: Record<string, unknown>
): Promise<DynamicToolCallResult> {
  const title = requiredNonEmptyString(args, 'title')
  if (title.ok === false) return title.error

  await client.sendRequest('thread/rename', {
    threadId,
    displayName: title.value
  })
  return ok(`Renamed thread ${threadId} to "${title.value}".`, {
    threadId,
    title: title.value
  })
}

async function setThreadArchivedTool(
  client: AppServerRequestClient,
  threadId: string,
  args: Record<string, unknown>
): Promise<DynamicToolCallResult> {
  const archived = requiredBoolean(args, 'archived')
  if (archived.ok === false) return archived.error

  await client.sendRequest(archived.value ? 'thread/archive' : 'thread/unarchive', {
    threadId
  })
  return ok(`${archived.value ? 'Archived' : 'Restored'} thread ${threadId}.`, {
    threadId,
    archived: archived.value
  })
}

async function setThreadPinnedTool(
  client: AppServerRequestClient,
  threadId: string,
  args: Record<string, unknown>,
  projectKey: string,
  settingsHost: DesktopThreadToolSettingsHost | undefined
): Promise<DynamicToolCallResult> {
  const pinned = requiredBoolean(args, 'pinned')
  if (pinned.ok === false) return pinned.error
  const workspace = normalizeWorkspaceProjectKey(projectKey)
  if (!workspace) {
    return fail('ThreadManagementUnavailable', 'No Desktop workspace is currently open.')
  }
  if (!settingsHost) {
    return fail('ThreadManagementUnavailable', 'Desktop settings are not available.')
  }

  if (pinned.value) {
    const readResult = await client.sendRequest<{ thread?: ThreadSummaryWire }>('thread/read', {
      threadId
    })
    const thread = readResult.thread
    if (!thread) {
      return fail('ThreadNotFound', `Thread '${threadId}' was not found.`)
    }
    if (thread.status === 'archived') {
      return fail('ThreadArchived', `Thread '${threadId}' is archived.`)
    }
    if (isSubAgentThreadWire(thread)) {
      return fail('TargetUnsupported', `Thread '${threadId}' is a subagent child thread and cannot be pinned.`)
    }
  }

  const settings = settingsHost.getSettings()
  const current = resolvePinnedThreadIdsForWorkspace(settings.pinnedThreadIdsByWorkspace, workspace)
  const next = pinned.value
    ? normalizePinnedThreadIds([threadId, ...current])
    : normalizePinnedThreadIds(current.filter((id) => id !== threadId))

  await settingsHost.updateSettings({
    pinnedThreadIdsByWorkspace: {
      [workspace]: next
    }
  })
  settingsHost.onPinnedThreadIdsChanged?.(workspace, next)

  return ok(`${pinned.value ? 'Pinned' : 'Unpinned'} thread ${threadId}.`, {
    threadId,
    pinned: pinned.value,
    pinnedThreadIds: next
  })
}

function ok(text: string, structuredContent?: unknown): DynamicToolCallResult {
  return {
    success: true,
    contentItems: [{ type: 'text', text }],
    structuredContent
  }
}

function fail(errorCode: string, errorMessage: string): DynamicToolCallResult {
  return { success: false, errorCode, errorMessage }
}

function requiredNonEmptyString(
  args: Record<string, unknown>,
  field: string
): { ok: true; value: string } | { ok: false; error: DynamicToolCallResult } {
  const value = args[field]
  if (typeof value !== 'string' || value.trim() === '') {
    return {
      ok: false,
      error: fail('InvalidArguments', `${field} must be a non-empty string.`)
    }
  }
  return { ok: true, value: value.trim() }
}

function optionalString(
  args: Record<string, unknown>,
  field: string
): { ok: true; value: string | undefined } | { ok: false; error: DynamicToolCallResult } | null {
  if (!(field in args) || args[field] == null) return null
  const value = args[field]
  if (typeof value !== 'string') {
    return {
      ok: false,
      error: fail('InvalidArguments', `${field} must be a string.`)
    }
  }
  return { ok: true, value: value.trim() || undefined }
}

function optionalNonEmptyString(
  args: Record<string, unknown>,
  field: string
): { ok: true; value: string | undefined } | { ok: false; error: DynamicToolCallResult } | null {
  if (!(field in args) || args[field] == null) return null
  const value = args[field]
  if (typeof value !== 'string' || value.trim() === '') {
    return {
      ok: false,
      error: fail('InvalidArguments', `${field} must be a non-empty string when provided.`)
    }
  }
  return { ok: true, value: value.trim() }
}

function optionalReasoningEffort(
  args: Record<string, unknown>,
  field: string
): { ok: true; value: ReasoningEffortValue | undefined } | { ok: false; error: DynamicToolCallResult } | null {
  if (!(field in args) || args[field] == null) return null
  const value = args[field]
  if (typeof value !== 'string' || !REASONING_EFFORT_VALUES.has(value)) {
    return {
      ok: false,
      error: fail('InvalidArguments', `${field} must be one of low, medium, high, extraHigh, max, or ultra.`)
    }
  }
  return { ok: true, value: value as ReasoningEffortValue }
}

function requiredBoolean(
  args: Record<string, unknown>,
  field: string
): { ok: true; value: boolean } | { ok: false; error: DynamicToolCallResult } {
  const value = args[field]
  if (typeof value !== 'boolean') {
    return {
      ok: false,
      error: fail('InvalidArguments', `${field} must be a boolean.`)
    }
  }
  return { ok: true, value }
}

function optionalBoolean(
  args: Record<string, unknown>,
  field: string,
  defaultValue: boolean
): { ok: true; value: boolean } | { ok: false; error: DynamicToolCallResult } {
  if (!(field in args) || args[field] == null) return { ok: true, value: defaultValue }
  const value = args[field]
  if (typeof value !== 'boolean') {
    return {
      ok: false,
      error: fail('InvalidArguments', `${field} must be a boolean.`)
    }
  }
  return { ok: true, value }
}

function optionalInteger(
  args: Record<string, unknown>,
  field: string,
  defaultValue: number,
  maxValue: number
): { ok: true; value: number } | { ok: false; error: DynamicToolCallResult } {
  if (!(field in args) || args[field] == null) return { ok: true, value: defaultValue }
  const value = args[field]
  if (!Number.isInteger(value) || typeof value !== 'number' || value < 1 || value > maxValue) {
    return {
      ok: false,
      error: fail('InvalidArguments', `${field} must be an integer from 1 to ${maxValue}.`)
    }
  }
  return { ok: true, value }
}

function resolvePinnedThreadIdsForWorkspace(
  pinnedThreadIdsByWorkspace: Record<string, string[]> | undefined,
  workspacePath: string
): string[] {
  const workspaceKey = normalizeWorkspaceProjectKey(workspacePath)
  if (!workspaceKey || !pinnedThreadIdsByWorkspace) return []
  const exact = pinnedThreadIdsByWorkspace[workspaceKey]
  if (Array.isArray(exact)) return normalizePinnedThreadIds(exact)
  const match = Object.entries(pinnedThreadIdsByWorkspace).find(
    ([candidate]) => normalizeWorkspaceProjectKey(candidate) === workspaceKey
  )
  return normalizePinnedThreadIds(match?.[1] ?? [])
}

function normalizePinnedThreadIds(threadIds: Iterable<string>): string[] {
  const seen = new Set<string>()
  const normalized: string[] = []
  for (const value of threadIds) {
    const id = typeof value === 'string' ? value.trim() : ''
    if (!id || seen.has(id)) continue
    seen.add(id)
    normalized.push(id)
  }
  return normalized
}

function isSubAgentThreadWire(thread: ThreadSummaryWire): boolean {
  const source = isRecord(thread.source) ? thread.source : null
  const kind = stringProperty(source, 'kind')
  return kind?.toLowerCase() === 'subagent'
    || thread.originChannel?.toLowerCase() === 'subagent'
}

function summarizeThread(thread: ThreadSummaryWire): JsonObject {
  return {
    id: thread.id ?? '',
    displayName: thread.displayName ?? null,
    status: thread.status ?? 'unknown',
    originChannel: thread.originChannel ?? null,
    createdAt: thread.createdAt ?? null,
    lastActiveAt: thread.lastActiveAt ?? null,
    runtime: thread.runtime ?? null,
    goal: thread.goal ?? null
  }
}

function formatThreadSummaryLine(thread: JsonObject): string {
  const title = typeof thread.displayName === 'string' && thread.displayName.trim()
    ? thread.displayName.trim()
    : '(untitled)'
  return `- ${thread.id}: ${title} [${thread.status ?? 'unknown'}]${thread.projectName ? ` · ${thread.projectName}` : ''}`
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return value != null && typeof value === 'object' && !Array.isArray(value)
}

function stringProperty(value: unknown, key: string): string | null {
  if (!isRecord(value)) return null
  const property = value[key]
  if (typeof property !== 'string' || property.trim() === '') return null
  return property
}
