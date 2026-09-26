import { readThreadTool } from './desktopReadThread'
import { readThreadToolDefinition } from './desktopReadThreadContract'
import { normalizeWorkspaceProjectKey } from '../shared/workspaceProjectKey'

export const DESKTOP_THREAD_TOOL_NAMESPACE = 'desktop'
export const DESKTOP_THREAD_COORDINATION_CONTEXT_KEY = 'desktop.threadCoordination'

const MAX_LIST_THREADS_LIMIT = 100
const DEFAULT_LIST_THREADS_LIMIT = 20
const REASONING_EFFORT_VALUES = new Set(['low', 'medium', 'high', 'extraHigh', 'ultra'])

type JsonObject = Record<string, unknown>
type ReasoningEffortValue = 'low' | 'medium' | 'high' | 'extraHigh' | 'ultra'

export interface RuntimeAdditionalContextEntry {
  kind: 'application'
  value: string
}

export interface DynamicToolFunctionSpec {
  type: 'function'
  name: string
  description: string
  inputSchema: JsonObject
  deferLoading?: boolean
}

export interface DynamicToolNamespaceSpec {
  type: 'namespace'
  name: string
  description: string
  tools: DynamicToolFunctionSpec[]
}

export type DynamicToolSpec = DynamicToolFunctionSpec | DynamicToolNamespaceSpec

interface DesktopThreadFunctionAuthoring {
  namespace: string
  name: string
  description: string
  inputSchema: JsonObject
  outputSchema?: JsonObject
  display?: { title?: string; subtitle?: string }
}

export interface AppServerRequestClient {
  sendRequest<T = unknown>(method: string, params?: unknown, timeoutMs?: number | null): Promise<T>
}

export interface DesktopAppServerRequestOptions {
  supportsDynamicToolRebind?: boolean
  settingsHost?: DesktopThreadToolSettingsHost
}

export interface DesktopThreadToolSettingsHost {
  getSettings(): { pinnedThreadIdsByWorkspace?: Record<string, string[]> }
  updateSettings(partial: { pinnedThreadIdsByWorkspace: Record<string, string[]> }): Promise<void>
  onPinnedThreadIdsChanged?(workspacePath: string, threadIds: string[]): void
}

export interface DynamicToolCallParams {
  threadId?: string
  turnId?: string
  callId?: string
  namespace?: string | null
  tool?: string
  arguments?: unknown
}

export type DynamicToolContentItem =
  | { type: 'text'; text: string }
  | { type: 'image'; mediaType: string; url: string; dataBase64?: never }
  | { type: 'image'; mediaType: string; dataBase64: string; url?: never }

export interface DynamicToolCallResult {
  success: boolean
  contentItems?: DynamicToolContentItem[]
  structuredContent?: unknown
  errorCode?: string
  errorMessage?: string
}

interface ThreadSummaryWire {
  id?: string
  displayName?: string | null
  status?: string
  originChannel?: string
  channelContext?: string | null
  createdAt?: string
  lastActiveAt?: string
  source?: Record<string, unknown> | null
  metadata?: Record<string, unknown> | null
  runtime?: unknown
  goal?: unknown
}

interface ThreadWire extends ThreadSummaryWire {
  turns?: Array<Record<string, unknown>>
  queuedInputs?: unknown[]
  configuration?: JsonObject | null
}

const TOOL_NAMES = new Set([
  'CreateThread',
  'ListThreads',
  'ReadThread',
  'SendMessageToThread',
  'SetThreadTitle',
  'SetThreadArchived',
  'SetThreadPinned'
])

const DESKTOP_THREAD_COORDINATION_CONTEXT: RuntimeAdditionalContextEntry = {
  kind: 'application',
  value: 'When the user asks to create, inspect, continue, pin, archive, rename, or otherwise manage DotCraft threads in the background, search for the relevant thread tool first: CreateThread, ListThreads, ReadThread, SendMessageToThread, SetThreadTitle, SetThreadArchived, SetThreadPinned.'
}

// Bound-tool state is connection-local; reconnecting gives AppServer a new callback target.
let boundClient: AppServerRequestClient | null = null
let boundThreadIds = new Set<string>()
let visualizationBoundThreadIds = new Set<string>()
let visualizationBindingPromises = new Map<string, Promise<void>>()

export function resetDesktopThreadToolBindings(): void {
  boundClient = null
  boundThreadIds = new Set<string>()
  visualizationBoundThreadIds = new Set<string>()
  visualizationBindingPromises = new Map<string, Promise<void>>()
}

export function buildDesktopThreadDynamicTools(): DynamicToolSpec[] {
  const functions: DesktopThreadFunctionAuthoring[] = [
    {
      namespace: DESKTOP_THREAD_TOOL_NAMESPACE,
      name: 'CreateThread',
      description: 'Create a new DotCraft thread in the current Desktop workspace and start its initial prompt.',
      inputSchema: {
        type: 'object',
        properties: {
          prompt: { type: 'string', description: 'Initial prompt for the new thread.' },
          displayName: { type: 'string', description: 'Optional display name for the created thread.' },
          model: { type: 'string', description: 'Optional per-thread model override for the created thread.' },
          reasoningEffort: {
            type: 'string',
            enum: ['low', 'medium', 'high', 'extraHigh', 'ultra'],
            description: 'Optional per-thread reasoning effort for the created thread.'
          }
        },
        required: ['prompt'],
        additionalProperties: false
      },
      outputSchema: {
        type: 'object',
        properties: {
          thread: { type: 'object' },
          turn: { type: 'object' },
          started: { type: 'boolean' }
        }
      },
      display: { title: 'Create thread', subtitle: 'Desktop' }
    },
    {
      namespace: DESKTOP_THREAD_TOOL_NAMESPACE,
      name: 'ListThreads',
      description: 'List recent DotCraft threads in the current Desktop workspace.',
      inputSchema: {
        type: 'object',
        properties: {
          query: { type: 'string', description: 'Optional local text filter for thread id, title, or origin.' },
          limit: { type: 'integer', minimum: 1, maximum: MAX_LIST_THREADS_LIMIT },
          cursor: { type: 'string', description: 'Opaque cursor from a previous ListThreads result.' },
          includeArchived: { type: 'boolean', description: 'Include archived threads in the result page.' }
        },
        additionalProperties: false
      },
      outputSchema: {
        type: 'object',
        properties: {
          threads: { type: 'array', items: { type: 'object' } },
          count: { type: 'integer' },
          nextCursor: { type: ['string', 'null'] },
          totalMatched: { type: 'integer' }
        }
      },
      display: { title: 'List threads', subtitle: 'Desktop' }
    },
    readThreadToolDefinition,
    {
      namespace: DESKTOP_THREAD_TOOL_NAMESPACE,
      name: 'SendMessageToThread',
      description: 'Send a follow-up prompt to an existing DotCraft thread without changing Desktop focus.',
      inputSchema: {
        type: 'object',
        properties: {
          threadId: { type: 'string' },
          prompt: { type: 'string' },
          model: { type: 'string', description: 'Unsupported until AppServer exposes turn-scoped model override.' },
          reasoningEffort: {
            type: 'string',
            enum: ['low', 'medium', 'high', 'extraHigh', 'ultra'],
            description: 'Optional persistent reasoning effort for future turns in the target thread.'
          }
        },
        required: ['threadId', 'prompt'],
        additionalProperties: false
      },
      outputSchema: {
        type: 'object',
        properties: {
          turn: { type: 'object' },
          queuedInput: { type: 'object' },
          started: { type: 'boolean' },
          queued: { type: 'boolean' }
        }
      },
      display: { title: 'Send message', subtitle: 'Desktop' }
    },
    {
      namespace: DESKTOP_THREAD_TOOL_NAMESPACE,
      name: 'SetThreadTitle',
      description: 'Rename a DotCraft thread.',
      inputSchema: {
        type: 'object',
        properties: {
          threadId: { type: 'string' },
          title: { type: 'string' }
        },
        required: ['threadId', 'title'],
        additionalProperties: false
      },
      outputSchema: {
        type: 'object',
        properties: {
          threadId: { type: 'string' },
          title: { type: 'string' }
        }
      },
      display: { title: 'Rename thread', subtitle: 'Desktop' }
    },
    {
      namespace: DESKTOP_THREAD_TOOL_NAMESPACE,
      name: 'SetThreadArchived',
      description: 'Archive or restore a DotCraft thread.',
      inputSchema: {
        type: 'object',
        properties: {
          threadId: { type: 'string' },
          archived: { type: 'boolean' }
        },
        required: ['threadId', 'archived'],
        additionalProperties: false
      },
      outputSchema: {
        type: 'object',
        properties: {
          threadId: { type: 'string' },
          archived: { type: 'boolean' }
        }
      },
      display: { title: 'Archive thread', subtitle: 'Desktop' }
    },
    {
      namespace: DESKTOP_THREAD_TOOL_NAMESPACE,
      name: 'SetThreadPinned',
      description: 'Pin or unpin a top-level DotCraft thread in the current Desktop workspace.',
      inputSchema: {
        type: 'object',
        properties: {
          threadId: { type: 'string' },
          pinned: { type: 'boolean' }
        },
        required: ['threadId', 'pinned'],
        additionalProperties: false
      },
      outputSchema: {
        type: 'object',
        properties: {
          threadId: { type: 'string' },
          pinned: { type: 'boolean' },
          pinnedThreadIds: { type: 'array', items: { type: 'string' } }
        }
      },
      display: { title: 'Pin thread', subtitle: 'Desktop' }
    }
  ]

  return [
    {
      type: 'namespace',
      name: DESKTOP_THREAD_TOOL_NAMESPACE,
      description: 'Desktop thread management tools.',
      tools: functions.map(({ namespace: _namespace, outputSchema: _outputSchema, display: _display, ...tool }) => ({
        type: 'function',
        ...tool,
        deferLoading: true
      }))
    }
  ]
}

export function buildDesktopThreadAdditionalContext(): Record<string, RuntimeAdditionalContextEntry> {
  return {
    [DESKTOP_THREAD_COORDINATION_CONTEXT_KEY]: { ...DESKTOP_THREAD_COORDINATION_CONTEXT }
  }
}

export async function sendDesktopAppServerRequest<T = unknown>(
  client: AppServerRequestClient,
  method: string,
  params?: unknown,
  timeoutMs?: number | null,
  options: DesktopAppServerRequestOptions = {}
): Promise<T> {
  trackClient(client)

  if (method === 'turn/start' || method === 'turn/enqueue') {
    const threadId = getStringProperty(params, 'threadId')
    if (threadId) {
      await ensureDesktopThreadToolsBound(client, threadId, options)
    }
  }

  if (method === 'visualization/view/open') {
    const threadId = getStringProperty(params, 'threadId')
    if (threadId) {
      await ensureInlineVisualizationThreadBound(client, threadId, options)
    }
  }

  const startsThread = method === 'thread/start' || method === 'worktree/createAndStart'
  const nextParams = startsThread
    ? withDesktopThreadDynamicTools(params)
    : method === 'thread/resume' && options.supportsDynamicToolRebind === true
      ? withDesktopThreadDynamicTools(params)
      : params

  const result = await client.sendRequest<T>(method, nextParams, timeoutMs)

  if (startsThread || method === 'thread/resume') {
    const threadId = extractThreadId(result)
      ?? (method === 'thread/resume' ? getStringProperty(nextParams, 'threadId') : undefined)
    if (threadId) {
      visualizationBoundThreadIds.add(threadId)
    }
    if (threadId && requestIncludesDesktopThreadTools(nextParams)) {
      markDesktopThreadToolsBound(client, threadId)
    }
  }

  return result
}

async function ensureInlineVisualizationThreadBound(
  client: AppServerRequestClient,
  threadId: string,
  options: DesktopAppServerRequestOptions
): Promise<void> {
  trackClient(client)
  if (visualizationBoundThreadIds.has(threadId)) return

  const existing = visualizationBindingPromises.get(threadId)
  if (existing) {
    await existing
    return
  }

  const resumeParams = options.supportsDynamicToolRebind === true
    ? withDesktopThreadDynamicTools({ threadId })
    : { threadId }
  const binding = client.sendRequest('thread/resume', resumeParams).then(() => {
    if (boundClient !== client) return
    visualizationBoundThreadIds.add(threadId)
    if (requestIncludesDesktopThreadTools(resumeParams)) boundThreadIds.add(threadId)
  })
  visualizationBindingPromises.set(threadId, binding)
  try {
    await binding
  } finally {
    if (visualizationBindingPromises.get(threadId) === binding) {
      visualizationBindingPromises.delete(threadId)
    }
  }
}

export async function handleDesktopRuntimeThreadToolCall(
  client: AppServerRequestClient,
  params: unknown,
  workspacePath: string,
  options: DesktopAppServerRequestOptions = {}
): Promise<DynamicToolCallResult | undefined> {
  const p = isRecord(params) ? params as DynamicToolCallParams : {}
  if (p.namespace !== DESKTOP_THREAD_TOOL_NAMESPACE || !p.tool || !TOOL_NAMES.has(p.tool)) {
    return undefined
  }

  const args = isRecord(p.arguments) ? p.arguments : {}

  try {
    switch (p.tool) {
      case 'CreateThread':
        return await createThreadTool(client, args, workspacePath, options, p.threadId)
      case 'ListThreads':
        return await listThreadsTool(client, args, workspacePath, options)
      case 'ReadThread':
        return await readThreadTool(client, p.arguments)
      case 'SendMessageToThread':
        return await sendMessageToThreadTool(client, args, options)
      case 'SetThreadTitle':
        return await setThreadTitleTool(client, args)
      case 'SetThreadArchived':
        return await setThreadArchivedTool(client, args)
      case 'SetThreadPinned':
        return await setThreadPinnedTool(client, args, workspacePath, options.settingsHost)
      default:
        return fail('UnsupportedTool', `Desktop tool '${p.tool}' is not supported.`)
    }
  } catch (error) {
    return fail(mapRequestErrorCode(error), requestErrorMessage(error))
  }
}

async function createThreadTool(
  client: AppServerRequestClient,
  args: Record<string, unknown>,
  workspacePath: string,
  options: DesktopAppServerRequestOptions,
  callerThreadId?: string
): Promise<DynamicToolCallResult> {
  const prompt = requiredNonEmptyString(args, 'prompt')
  if (prompt.ok === false) return prompt.error
  const displayName = optionalNonEmptyString(args, 'displayName')
  if (displayName?.ok === false) return displayName.error
  const model = optionalNonEmptyString(args, 'model')
  if (model?.ok === false) return model.error
  const reasoningEffort = optionalReasoningEffort(args, 'reasoningEffort')
  if (reasoningEffort?.ok === false) return reasoningEffort.error
  if (!workspacePath.trim()) {
    return fail('ThreadManagementUnavailable', 'No Desktop workspace is currently open.')
  }

  const startParams: JsonObject = {
    identity: desktopIdentity(workspacePath),
    historyMode: 'server'
  }
  if (displayName?.value) {
    startParams.displayName = displayName.value
  }
  // Record which thread spawned this one so the new thread's first user message can
  // link back to its source (the calling thread). Kept as a non-subagent origin.
  if (callerThreadId && callerThreadId.trim()) {
    startParams.spawnedFromThreadId = callerThreadId.trim()
  }
  const config: JsonObject = {}
  if (model?.value) {
    config.model = model.value
  }
  if (reasoningEffort?.value) {
    config.reasoning = buildReasoningConfig(reasoningEffort.value)
  }
  if (Object.keys(config).length > 0) {
    startParams.config = config
  }

  const startResult = await sendDesktopAppServerRequest<{ thread?: ThreadSummaryWire }>(
    client,
    'thread/start',
    startParams,
    undefined,
    options
  )
  const thread = startResult.thread
  const threadId = thread?.id
  if (!threadId) {
    return fail('AppServerRequestFailed', 'thread/start did not return a thread id.')
  }

  let turnResult: { turn?: JsonObject }
  try {
    turnResult = await sendDesktopAppServerRequest<{ turn?: JsonObject }>(
      client,
      'turn/start',
      {
        threadId,
        input: [{ type: 'text', text: prompt.value }],
        identity: desktopIdentity(workspacePath)
      },
      undefined,
      options
    )
  } catch (error) {
    return fail(
      'AppServerRequestFailed',
      `Created thread ${formatThreadTitle(thread)} but failed to start its initial turn: ${requestErrorMessage(error)}`,
      {
        thread,
        turn: null,
        started: false
      }
    )
  }

  return ok(
    `Created thread ${formatThreadTitle(thread)} and started its initial turn.`,
    {
      thread,
      turn: turnResult.turn ?? null,
      started: Boolean(turnResult.turn)
    }
  )
}

async function listThreadsTool(
  client: AppServerRequestClient,
  args: Record<string, unknown>,
  workspacePath: string,
  _options: DesktopAppServerRequestOptions
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
  const totalMatched = typeof result.totalMatched === 'number' ? result.totalMatched : threads.length
  const text = threads.length === 0
    ? 'No matching threads were found.'
    : `Found ${threads.length} thread${threads.length === 1 ? '' : 's'}${result.nextCursor ? ' (more available)' : ''}:\n${threads.map(formatThreadSummaryLine).join('\n')}`

  return ok(text, {
    threads,
    count: threads.length,
    totalMatched,
    nextCursor: result.nextCursor ?? null
  })
}

async function sendMessageToThreadTool(
  client: AppServerRequestClient,
  args: Record<string, unknown>,
  options: DesktopAppServerRequestOptions
): Promise<DynamicToolCallResult> {
  const threadId = requiredNonEmptyString(args, 'threadId')
  if (threadId.ok === false) return threadId.error
  const prompt = requiredNonEmptyString(args, 'prompt')
  if (prompt.ok === false) return prompt.error
  const model = optionalNonEmptyString(args, 'model')
  if (model?.ok === false) return model.error
  const reasoningEffort = optionalReasoningEffort(args, 'reasoningEffort')
  if (reasoningEffort?.ok === false) return reasoningEffort.error
  if (model?.value) {
    return fail('UnsupportedOption', 'SendMessageToThread.model is not supported by the current AppServer turn protocol.')
  }

  const readResult = await client.sendRequest<{ thread?: ThreadWire }>('thread/read', {
    threadId: threadId.value
  })
  if (!readResult.thread) {
    return fail('ThreadNotFound', `Thread '${threadId.value}' was not found.`)
  }
  if (readResult.thread.status === 'archived') {
    return fail('ThreadArchived', `Thread '${threadId.value}' is archived.`)
  }
  if (reasoningEffort?.value) {
    await client.sendRequest('thread/config/update', {
      threadId: threadId.value,
      config: buildConfigWithReasoningEffort(readResult.thread.configuration, reasoningEffort.value)
    })
  }

  const input = [{ type: 'text', text: prompt.value }]
  try {
    const turnResult = await sendDesktopAppServerRequest<{ turn?: JsonObject }>(
      client,
      'turn/start',
      { threadId: threadId.value, input },
      undefined,
      options
    )
    return ok(`Sent message to thread ${threadId.value}.`, {
      threadId: threadId.value,
      turn: turnResult.turn ?? null,
      started: true,
      queued: false
    })
  } catch (error) {
    if (!isThreadBusyError(error)) {
      throw error
    }
    try {
      const queueResult = await sendDesktopAppServerRequest<{ queuedInput?: unknown }>(
        client,
        'turn/enqueue',
        { threadId: threadId.value, input },
        undefined,
        options
      )
      return ok(`Queued message for thread ${threadId.value}.`, {
        threadId: threadId.value,
        queuedInput: queueResult.queuedInput ?? null,
        started: false,
        queued: true
      })
    } catch (queueError) {
      return fail('ThreadBusy', requestErrorMessage(queueError))
    }
  }
}

async function setThreadTitleTool(
  client: AppServerRequestClient,
  args: Record<string, unknown>
): Promise<DynamicToolCallResult> {
  const threadId = requiredNonEmptyString(args, 'threadId')
  if (threadId.ok === false) return threadId.error
  const title = requiredNonEmptyString(args, 'title')
  if (title.ok === false) return title.error

  await client.sendRequest('thread/rename', {
    threadId: threadId.value,
    displayName: title.value
  })
  return ok(`Renamed thread ${threadId.value} to "${title.value}".`, {
    threadId: threadId.value,
    title: title.value
  })
}

async function setThreadArchivedTool(
  client: AppServerRequestClient,
  args: Record<string, unknown>
): Promise<DynamicToolCallResult> {
  const threadId = requiredNonEmptyString(args, 'threadId')
  if (threadId.ok === false) return threadId.error
  const archived = requiredBoolean(args, 'archived')
  if (archived.ok === false) return archived.error

  await client.sendRequest(archived.value ? 'thread/archive' : 'thread/unarchive', {
    threadId: threadId.value
  })
  return ok(`${archived.value ? 'Archived' : 'Restored'} thread ${threadId.value}.`, {
    threadId: threadId.value,
    archived: archived.value
  })
}

async function setThreadPinnedTool(
  client: AppServerRequestClient,
  args: Record<string, unknown>,
  workspacePath: string,
  settingsHost: DesktopThreadToolSettingsHost | undefined
): Promise<DynamicToolCallResult> {
  const threadId = requiredNonEmptyString(args, 'threadId')
  if (threadId.ok === false) return threadId.error
  const pinned = requiredBoolean(args, 'pinned')
  if (pinned.ok === false) return pinned.error
  const workspace = normalizeWorkspaceProjectKey(workspacePath)
  if (!workspace) {
    return fail('ThreadManagementUnavailable', 'No Desktop workspace is currently open.')
  }
  if (!settingsHost) {
    return fail('ThreadManagementUnavailable', 'Desktop settings are not available.')
  }

  if (pinned.value) {
    const readResult = await client.sendRequest<{ thread?: ThreadWire }>('thread/read', {
      threadId: threadId.value
    })
    const thread = readResult.thread
    if (!thread) {
      return fail('ThreadNotFound', `Thread '${threadId.value}' was not found.`)
    }
    if (thread.status === 'archived') {
      return fail('ThreadArchived', `Thread '${threadId.value}' is archived.`)
    }
    if (isSubAgentThreadWire(thread)) {
      return fail('TargetUnsupported', `Thread '${threadId.value}' is a subagent child thread and cannot be pinned.`)
    }
  }

  const settings = settingsHost.getSettings()
  const current = resolvePinnedThreadIdsForWorkspace(settings.pinnedThreadIdsByWorkspace, workspace)
  const next = pinned.value
    ? normalizePinnedThreadIds([threadId.value, ...current])
    : normalizePinnedThreadIds(current.filter((id) => id !== threadId.value))

  await settingsHost.updateSettings({
    pinnedThreadIdsByWorkspace: {
      [workspace]: next
    }
  })
  settingsHost.onPinnedThreadIdsChanged?.(workspace, next)

  return ok(`${pinned.value ? 'Pinned' : 'Unpinned'} thread ${threadId.value}.`, {
    threadId: threadId.value,
    pinned: pinned.value,
    pinnedThreadIds: next
  })
}

async function ensureDesktopThreadToolsBound(
  client: AppServerRequestClient,
  threadId: string,
  options: DesktopAppServerRequestOptions
): Promise<void> {
  trackClient(client)
  if (boundThreadIds.has(threadId) || options.supportsDynamicToolRebind !== true) {
    return
  }

  await client.sendRequest('thread/resume', withDesktopThreadDynamicTools({ threadId }))
  markDesktopThreadToolsBound(client, threadId)
}

export function hasDesktopThreadTools(threadId: string, options: DesktopAppServerRequestOptions): boolean {
  return options.supportsDynamicToolRebind === true || boundThreadIds.has(threadId)
}

function withDesktopThreadDynamicTools(params: unknown): unknown {
  if (!isRecord(params)) return params

  const existing = Array.isArray(params.dynamicTools)
    ? params.dynamicTools.filter((tool) => !isDesktopThreadToolSpec(tool))
    : []
  const existingAdditionalContext = isRecord(params.additionalContext)
    ? params.additionalContext
    : {}

  return {
    ...params,
    additionalContext: {
      ...existingAdditionalContext,
      ...buildDesktopThreadAdditionalContext()
    },
    dynamicTools: [
      ...existing,
      ...buildDesktopThreadDynamicTools()
    ]
  }
}

function requestIncludesDesktopThreadTools(params: unknown): boolean {
  return isRecord(params)
    && Array.isArray(params.dynamicTools)
    && params.dynamicTools.some(isDesktopThreadToolSpec)
}

function isDesktopThreadToolSpec(value: unknown): boolean {
  if (!isRecord(value)) return false
  return value.type === 'namespace'
    && value.name === DESKTOP_THREAD_TOOL_NAMESPACE
    && Array.isArray(value.tools)
}

function markDesktopThreadToolsBound(client: AppServerRequestClient, threadId: string): void {
  trackClient(client)
  boundThreadIds.add(threadId)
}

function trackClient(client: AppServerRequestClient): void {
  if (boundClient === client) return
  boundClient = client
  boundThreadIds = new Set<string>()
  visualizationBoundThreadIds = new Set<string>()
  visualizationBindingPromises = new Map<string, Promise<void>>()
}

function desktopIdentity(workspacePath: string): JsonObject {
  return {
    channelName: 'dotcraft-desktop',
    userId: 'local',
    channelContext: `workspace:${workspacePath}`,
    workspacePath
  }
}

function ok(text: string, structuredContent?: unknown): DynamicToolCallResult {
  return {
    success: true,
    contentItems: [{ type: 'text', text }],
    structuredContent
  }
}

function fail(errorCode: string, errorMessage: string, structuredContent?: unknown): DynamicToolCallResult {
  return {
    success: false,
    errorCode,
    errorMessage,
    ...(structuredContent === undefined ? {} : { structuredContent })
  }
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
      error: fail('InvalidArguments', `${field} must be one of low, medium, high, extraHigh, or ultra.`)
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

function buildReasoningConfig(effort: ReasoningEffortValue, existing?: Record<string, unknown>): JsonObject {
  return {
    enabled: true,
    effort,
    output: firstString(existing?.output, existing?.Output) ?? 'full'
  }
}

function buildConfigWithReasoningEffort(config: JsonObject | null | undefined, effort: ReasoningEffortValue): JsonObject {
  const next = cloneJsonObject(config) ?? { mode: 'agent' }
  const existingReasoning = isRecord(next.reasoning)
    ? next.reasoning
    : isRecord(next.Reasoning)
      ? next.Reasoning
      : undefined
  delete next.Reasoning
  next.reasoning = buildReasoningConfig(effort, existingReasoning)
  return next
}

function cloneJsonObject(value: unknown): JsonObject | null {
  if (!isRecord(value)) return null
  try {
    const cloned = JSON.parse(JSON.stringify(value)) as unknown
    return isRecord(cloned) ? cloned : null
  } catch {
    return { ...value }
  }
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

function formatThreadTitle(thread: ThreadSummaryWire | undefined): string {
  const title = thread?.displayName?.trim()
  return title ? `"${title}" (${thread?.id ?? 'unknown'})` : (thread?.id ?? 'unknown')
}

function formatThreadSummaryLine(thread: JsonObject): string {
  const title = typeof thread.displayName === 'string' && thread.displayName.trim()
    ? thread.displayName.trim()
    : '(untitled)'
  return `- ${thread.id}: ${title} [${thread.status ?? 'unknown'}]`
}

function extractThreadId(result: unknown): string | null {
  if (!isRecord(result) || !isRecord(result.thread)) return null
  return typeof result.thread.id === 'string' ? result.thread.id : null
}

function getStringProperty(value: unknown, key: string): string | null {
  if (!isRecord(value)) return null
  const property = value[key]
  return typeof property === 'string' && property.trim() !== '' ? property : null
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

function firstString(...values: unknown[]): string | null {
  for (const value of values) {
    if (typeof value === 'string' && value.trim() !== '') return value
  }
  return null
}

function requestErrorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error)
}

function mapRequestErrorCode(error: unknown): string {
  const message = requestErrorMessage(error).toLowerCase()
  if (message.includes('not found')) return 'ThreadNotFound'
  if (message.includes('archived')) return 'ThreadArchived'
  if (isThreadBusyError(error)) return 'ThreadBusy'
  return 'AppServerRequestFailed'
}

function isThreadBusyError(error: unknown): boolean {
  const message = requestErrorMessage(error).toLowerCase()
  return message.includes('turninprogress')
    || message.includes('turn in progress')
    || message.includes('threadbusy')
    || message.includes('thread busy')
    || message.includes('already running')
    || message.includes('is running')
    || message.includes('maintenance')
}
