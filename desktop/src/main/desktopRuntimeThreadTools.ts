export const DESKTOP_THREAD_TOOL_NAMESPACE = 'desktop'
export const DESKTOP_THREAD_COORDINATION_CONTEXT_KEY = 'desktop.threadCoordination'

const MAX_LIST_THREADS_LIMIT = 100
const DEFAULT_LIST_THREADS_LIMIT = 20
const DEFAULT_READ_TURN_LIMIT = 10
const MAX_READ_TURN_LIMIT = 50
const DEFAULT_OUTPUT_CHARS_PER_ITEM = 2_000
const MAX_OUTPUT_CHARS_PER_ITEM = 20_000

type JsonObject = Record<string, unknown>

export interface RuntimeAdditionalContextEntry {
  kind: 'application'
  value: string
}

export interface DynamicToolSpec {
  namespace?: string
  name: string
  description: string
  inputSchema: JsonObject
  outputSchema?: JsonObject
  deferLoading?: boolean
  display?: {
    title?: string
    subtitle?: string
  }
}

export interface AppServerRequestClient {
  sendRequest<T = unknown>(method: string, params?: unknown, timeoutMs?: number | null): Promise<T>
}

export interface DesktopAppServerRequestOptions {
  supportsDynamicToolRebind?: boolean
}

export interface DynamicToolCallParams {
  threadId?: string
  turnId?: string
  callId?: string
  namespace?: string | null
  tool?: string
  arguments?: unknown
}

export interface DynamicToolCallResult {
  success: boolean
  contentItems?: Array<{ type: 'text'; text: string }>
  structuredResult?: unknown
  errorCode?: string
  errorMessage?: string
}

interface ThreadSummaryWire {
  id?: string
  displayName?: string | null
  status?: string
  originChannel?: string
  createdAt?: string
  lastActiveAt?: string
  runtime?: unknown
  goal?: unknown
}

interface ThreadWire extends ThreadSummaryWire {
  turns?: Array<Record<string, unknown>>
  queuedInputs?: unknown[]
}

const TOOL_NAMES = new Set([
  'CreateThread',
  'ListThreads',
  'ReadThread',
  'SendMessageToThread',
  'SetThreadTitle',
  'SetThreadArchived'
])

const DESKTOP_THREAD_COORDINATION_CONTEXT: RuntimeAdditionalContextEntry = {
  kind: 'application',
  value: 'When the user asks to create, inspect, continue, archive, rename, or otherwise manage DotCraft threads in the background, search for the relevant thread tool first: CreateThread, ListThreads, ReadThread, SendMessageToThread, SetThreadTitle, SetThreadArchived.'
}

// Bound-tool state is connection-local; reconnecting gives AppServer a new callback target.
let boundClient: AppServerRequestClient | null = null
let boundThreadIds = new Set<string>()

export function resetDesktopThreadToolBindings(): void {
  boundClient = null
  boundThreadIds = new Set<string>()
}

export function buildDesktopThreadDynamicTools(): DynamicToolSpec[] {
  return [
    {
      namespace: DESKTOP_THREAD_TOOL_NAMESPACE,
      name: 'CreateThread',
      description: 'Create a new DotCraft thread in the current Desktop workspace and start its initial prompt.',
      inputSchema: {
        type: 'object',
        properties: {
          prompt: { type: 'string', description: 'Initial prompt for the new thread.' },
          displayName: { type: 'string', description: 'Optional display name for the created thread.' },
          model: { type: 'string', description: 'Optional per-thread model override for the created thread.' }
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
          limit: { type: 'integer', minimum: 1, maximum: MAX_LIST_THREADS_LIMIT }
        },
        additionalProperties: false
      },
      outputSchema: {
        type: 'object',
        properties: {
          threads: { type: 'array', items: { type: 'object' } },
          count: { type: 'integer' }
        }
      },
      display: { title: 'List threads', subtitle: 'Desktop' }
    },
    {
      namespace: DESKTOP_THREAD_TOOL_NAMESPACE,
      name: 'ReadThread',
      description: 'Read status and recent turn summaries for one DotCraft thread without opening it.',
      inputSchema: {
        type: 'object',
        properties: {
          threadId: { type: 'string' },
          includeOutputs: { type: 'boolean' },
          maxOutputCharsPerItem: { type: 'integer', minimum: 1, maximum: MAX_OUTPUT_CHARS_PER_ITEM },
          turnLimit: { type: 'integer', minimum: 1, maximum: MAX_READ_TURN_LIMIT }
        },
        required: ['threadId'],
        additionalProperties: false
      },
      outputSchema: {
        type: 'object',
        properties: {
          thread: { type: 'object' }
        }
      },
      display: { title: 'Read thread', subtitle: 'Desktop' }
    },
    {
      namespace: DESKTOP_THREAD_TOOL_NAMESPACE,
      name: 'SendMessageToThread',
      description: 'Send a follow-up prompt to an existing DotCraft thread without changing Desktop focus.',
      inputSchema: {
        type: 'object',
        properties: {
          threadId: { type: 'string' },
          prompt: { type: 'string' },
          model: { type: 'string', description: 'Unsupported until AppServer exposes turn-scoped model override.' }
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
    }
  ].map((tool) => ({ ...tool, deferLoading: true }))
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

  const nextParams = method === 'thread/start'
    ? withDesktopThreadDynamicTools(params)
    : method === 'thread/resume' && options.supportsDynamicToolRebind === true
      ? withDesktopThreadDynamicTools(params)
      : params

  const result = await client.sendRequest<T>(method, nextParams, timeoutMs)

  if (method === 'thread/start' || method === 'thread/resume') {
    const threadId = extractThreadId(result)
    if (threadId && requestIncludesDesktopThreadTools(nextParams)) {
      markDesktopThreadToolsBound(client, threadId)
    }
  }

  return result
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
        return await createThreadTool(client, args, workspacePath, options)
      case 'ListThreads':
        return await listThreadsTool(client, args, workspacePath, options)
      case 'ReadThread':
        return await readThreadTool(client, args, options)
      case 'SendMessageToThread':
        return await sendMessageToThreadTool(client, args, options)
      case 'SetThreadTitle':
        return await setThreadTitleTool(client, args)
      case 'SetThreadArchived':
        return await setThreadArchivedTool(client, args)
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
  options: DesktopAppServerRequestOptions
): Promise<DynamicToolCallResult> {
  const prompt = requiredNonEmptyString(args, 'prompt')
  if (prompt.ok === false) return prompt.error
  const displayName = optionalNonEmptyString(args, 'displayName')
  if (displayName?.ok === false) return displayName.error
  const model = optionalNonEmptyString(args, 'model')
  if (model?.ok === false) return model.error
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
  if (model?.value) {
    startParams.config = { model: model.value }
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

  const turnResult = await sendDesktopAppServerRequest<{ turn?: JsonObject }>(
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

  const result = await client.sendRequest<{ data?: ThreadSummaryWire[] }>('thread/list', {
    identity: desktopIdentity(workspacePath),
    includeSubAgents: false
  })
  const filtered = filterThreads(result.data ?? [], query?.value)
  const threads = filtered.slice(0, limit.value).map(summarizeThread)
  const text = threads.length === 0
    ? 'No matching threads were found.'
    : `Found ${threads.length} thread${threads.length === 1 ? '' : 's'}:\n${threads.map(formatThreadSummaryLine).join('\n')}`

  return ok(text, {
    threads,
    count: threads.length,
    totalMatched: filtered.length
  })
}

async function readThreadTool(
  client: AppServerRequestClient,
  args: Record<string, unknown>,
  _options: DesktopAppServerRequestOptions
): Promise<DynamicToolCallResult> {
  const threadId = requiredNonEmptyString(args, 'threadId')
  if (threadId.ok === false) return threadId.error
  const includeOutputs = optionalBoolean(args, 'includeOutputs', false)
  if (includeOutputs.ok === false) return includeOutputs.error
  const maxOutputCharsPerItem = optionalInteger(
    args,
    'maxOutputCharsPerItem',
    DEFAULT_OUTPUT_CHARS_PER_ITEM,
    MAX_OUTPUT_CHARS_PER_ITEM
  )
  if (maxOutputCharsPerItem.ok === false) return maxOutputCharsPerItem.error
  const turnLimit = optionalInteger(args, 'turnLimit', DEFAULT_READ_TURN_LIMIT, MAX_READ_TURN_LIMIT)
  if (turnLimit.ok === false) return turnLimit.error

  const result = await client.sendRequest<{ thread?: ThreadWire }>('thread/read', {
    threadId: threadId.value,
    includeTurns: true
  })
  const thread = result.thread
  if (!thread) {
    return fail('ThreadNotFound', `Thread '${threadId.value}' was not found.`)
  }

  const summary = summarizeThreadWithTurns(
    thread,
    turnLimit.value,
    includeOutputs.value,
    maxOutputCharsPerItem.value
  )
  return ok(formatReadThreadText(summary), { thread: summary })
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
  return value.namespace === DESKTOP_THREAD_TOOL_NAMESPACE
    && typeof value.name === 'string'
    && TOOL_NAMES.has(value.name)
}

function markDesktopThreadToolsBound(client: AppServerRequestClient, threadId: string): void {
  trackClient(client)
  boundThreadIds.add(threadId)
}

function trackClient(client: AppServerRequestClient): void {
  if (boundClient === client) return
  boundClient = client
  boundThreadIds = new Set<string>()
}

function desktopIdentity(workspacePath: string): JsonObject {
  return {
    channelName: 'dotcraft-desktop',
    userId: 'local',
    channelContext: `workspace:${workspacePath}`,
    workspacePath
  }
}

function ok(text: string, structuredResult?: unknown): DynamicToolCallResult {
  return {
    success: true,
    contentItems: [{ type: 'text', text }],
    structuredResult
  }
}

function fail(errorCode: string, errorMessage: string): DynamicToolCallResult {
  return {
    success: false,
    errorCode,
    errorMessage
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

function filterThreads(threads: ThreadSummaryWire[], query?: string): ThreadSummaryWire[] {
  const normalized = query?.trim().toLowerCase()
  if (!normalized) return threads
  return threads.filter((thread) => {
    const fields = [
      thread.id,
      thread.displayName,
      thread.originChannel,
      thread.status
    ]
    return fields.some((field) => typeof field === 'string' && field.toLowerCase().includes(normalized))
  })
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

function summarizeThreadWithTurns(
  thread: ThreadWire,
  turnLimit: number,
  includeOutputs: boolean,
  maxOutputCharsPerItem: number
): JsonObject {
  const turns = Array.isArray(thread.turns) ? thread.turns : []
  const recentTurns = turns.slice(-turnLimit).map((turn) =>
    summarizeTurn(turn, includeOutputs, maxOutputCharsPerItem)
  )
  return {
    ...summarizeThread(thread),
    queuedInputCount: Array.isArray(thread.queuedInputs) ? thread.queuedInputs.length : 0,
    turnCount: turns.length,
    page: {
      order: 'oldest_first',
      limit: turnLimit,
      hasMore: turns.length > recentTurns.length
    },
    turns: recentTurns
  }
}

function summarizeTurn(
  turn: Record<string, unknown>,
  includeOutputs: boolean,
  maxOutputCharsPerItem: number
): JsonObject {
  const items = Array.isArray(turn.items) ? turn.items : []
  return {
    id: typeof turn.id === 'string' ? turn.id : '',
    status: typeof turn.status === 'string' ? turn.status : 'unknown',
    startedAt: typeof turn.startedAt === 'string' ? turn.startedAt : null,
    completedAt: typeof turn.completedAt === 'string' ? turn.completedAt : null,
    itemCount: items.length,
    items: items.map((item) => summarizeItem(item, includeOutputs, maxOutputCharsPerItem))
  }
}

function summarizeItem(item: unknown, includeOutputs: boolean, maxOutputCharsPerItem: number): JsonObject {
  if (!isRecord(item)) return { type: 'unknown' }
  const payload = isRecord(item.payload) ? item.payload : {}
  const type = stringProperty(item, 'type') ?? stringProperty(item, 'payloadKind') ?? 'unknown'
  const summary: JsonObject = {
    id: stringProperty(item, 'id') ?? '',
    type,
    status: stringProperty(item, 'status') ?? 'unknown'
  }

  switch (type) {
    case 'userMessage':
      return summarizeUserMessage(summary, payload, includeOutputs, maxOutputCharsPerItem)
    case 'agentMessage':
      return summarizeTextPayload(summary, payload, includeOutputs, maxOutputCharsPerItem)
    case 'reasoningContent':
      summary.note = 'reasoning content omitted'
      if (includeOutputs) return summarizeTextPayload(summary, payload, includeOutputs, maxOutputCharsPerItem)
      return summary
    case 'commandExecution':
      return summarizeCommandExecution(summary, payload, includeOutputs, maxOutputCharsPerItem)
    case 'toolExecution':
      return summarizeToolExecution(summary, payload)
    case 'toolCall':
      return summarizeToolCall(summary, payload)
    case 'pluginFunctionCall':
      return summarizePluginFunctionCall(summary, payload, includeOutputs, maxOutputCharsPerItem)
    case 'dynamicToolCall':
      return summarizeDynamicToolCall(summary, payload, includeOutputs, maxOutputCharsPerItem)
    case 'toolResult':
      return summarizeToolResult(summary, payload, includeOutputs, maxOutputCharsPerItem)
    case 'approvalRequest':
      return summarizeApprovalRequest(summary, payload)
    case 'approvalResponse':
      return copyKnownFields(summary, payload, ['requestId', 'approved', 'decision'])
    case 'userInputRequest':
      return summarizeUserInputRequest(summary, payload)
    case 'userInputResponse':
      return copyKnownFields(summary, payload, ['requestId'])
    case 'systemNotice':
      return summarizeSystemNotice(summary, payload)
    case 'error':
      return summarizeError(summary, payload)
    default:
      return summarizeFallbackItem(summary, item, payload, includeOutputs, maxOutputCharsPerItem)
  }
}

function summarizeUserMessage(
  summary: JsonObject,
  payload: Record<string, unknown>,
  includeOutputs: boolean,
  maxOutputCharsPerItem: number
): JsonObject {
  const maxTextChars = includeOutputs ? maxOutputCharsPerItem : 500
  const text = stringProperty(payload, 'text')
  if (text) summary.text = truncate(text, maxTextChars)

  const nativeInputParts = Array.isArray(payload.nativeInputParts) ? payload.nativeInputParts : []
  const materializedInputParts = Array.isArray(payload.materializedInputParts) ? payload.materializedInputParts : []
  const inputParts = nativeInputParts.length > 0 ? nativeInputParts : materializedInputParts
  if (inputParts.length > 0) {
    summary.content = inputParts
      .map((part) => summarizeInputPart(part, maxTextChars))
      .filter((part): part is JsonObject => part != null)
  }

  copyOptionalStringFields(summary, payload, [
    'deliveryMode',
    'senderId',
    'senderName',
    'channelName',
    'triggerKind',
    'triggerLabel'
  ])
  return summary
}

function summarizeTextPayload(
  summary: JsonObject,
  payload: Record<string, unknown>,
  includeOutputs: boolean,
  maxOutputCharsPerItem: number
): JsonObject {
  const text = stringProperty(payload, 'text')
  if (text) {
    summary.text = truncate(text, includeOutputs ? maxOutputCharsPerItem : 500)
    delete summary.note
  }
  return summary
}

function summarizeCommandExecution(
  summary: JsonObject,
  payload: Record<string, unknown>,
  includeOutputs: boolean,
  maxOutputCharsPerItem: number
): JsonObject {
  copyOptionalStringFields(summary, payload, [
    'command',
    'workingDirectory',
    'source',
    'sessionId',
    'outputPath',
    'backgroundReason',
    'callId'
  ])
  copyOptionalNumberFields(summary, payload, ['exitCode', 'durationMs', 'originalOutputChars'])
  copyOptionalBooleanFields(summary, payload, ['truncated'])
  const payloadStatus = stringProperty(payload, 'status')
  if (payloadStatus) summary.status = payloadStatus
  const output = stringProperty(payload, 'aggregatedOutput')
  if (includeOutputs && output) {
    summary.output = truncate(output, maxOutputCharsPerItem)
  } else if (output) {
    summary.outputChars = output.length
  }
  return summary
}

function summarizeToolExecution(summary: JsonObject, payload: Record<string, unknown>): JsonObject {
  copyOptionalStringFields(summary, payload, ['callId', 'toolName', 'errorMessage'])
  copyOptionalNumberFields(summary, payload, ['durationMs'])
  copyOptionalBooleanFields(summary, payload, ['success'])
  const payloadStatus = stringProperty(payload, 'status')
  if (payloadStatus) summary.status = payloadStatus
  const resultPreview = stringProperty(payload, 'resultPreview')
  if (resultPreview) summary.resultPreview = truncate(resultPreview, 500)
  return summary
}

function summarizeToolCall(summary: JsonObject, payload: Record<string, unknown>): JsonObject {
  copyOptionalStringFields(summary, payload, ['toolName', 'callId'])
  const argumentsPreview = jsonPreview(payload.arguments, 500)
  if (argumentsPreview) summary.argumentsPreview = argumentsPreview
  return summary
}

function summarizePluginFunctionCall(
  summary: JsonObject,
  payload: Record<string, unknown>,
  includeOutputs: boolean,
  maxOutputCharsPerItem: number
): JsonObject {
  copyOptionalStringFields(summary, payload, [
    'pluginId',
    'namespace',
    'functionName',
    'callId',
    'errorCode',
    'errorMessage'
  ])
  copyOptionalBooleanFields(summary, payload, ['success'])
  const argumentsPreview = jsonPreview(payload.arguments, 500)
  if (argumentsPreview) summary.argumentsPreview = argumentsPreview
  if (includeOutputs) {
    addToolOutputPreview(summary, payload, maxOutputCharsPerItem)
  }
  return summary
}

function summarizeDynamicToolCall(
  summary: JsonObject,
  payload: Record<string, unknown>,
  includeOutputs: boolean,
  maxOutputCharsPerItem: number
): JsonObject {
  copyOptionalStringFields(summary, payload, [
    'namespace',
    'toolName',
    'callId',
    'errorCode',
    'errorMessage'
  ])
  copyOptionalBooleanFields(summary, payload, ['success'])
  const argumentsPreview = jsonPreview(payload.arguments, 500)
  if (argumentsPreview) summary.argumentsPreview = argumentsPreview
  if (includeOutputs) {
    addToolOutputPreview(summary, payload, maxOutputCharsPerItem)
  }
  return summary
}

function summarizeToolResult(
  summary: JsonObject,
  payload: Record<string, unknown>,
  includeOutputs: boolean,
  maxOutputCharsPerItem: number
): JsonObject {
  copyOptionalStringFields(summary, payload, ['callId'])
  copyOptionalBooleanFields(summary, payload, ['success'])
  const result = stringProperty(payload, 'result')
  if (includeOutputs && result) {
    summary.result = truncate(result, maxOutputCharsPerItem)
  } else if (result) {
    summary.resultChars = result.length
  }
  return summary
}

function summarizeApprovalRequest(summary: JsonObject, payload: Record<string, unknown>): JsonObject {
  copyOptionalStringFields(summary, payload, ['approvalType', 'operation', 'target', 'requestId', 'scopeKey'])
  const reason = stringProperty(payload, 'reason')
  if (reason) summary.reason = truncate(reason, 500)
  return summary
}

function summarizeUserInputRequest(summary: JsonObject, payload: Record<string, unknown>): JsonObject {
  copyOptionalStringFields(summary, payload, ['requestId'])
  const questions = Array.isArray(payload.questions) ? payload.questions : []
  summary.questionCount = questions.length
  if (questions.length > 0) {
    summary.questions = questions.map((question) => {
      if (!isRecord(question)) return { question: 'unknown' }
      return {
        id: stringProperty(question, 'id') ?? '',
        header: stringProperty(question, 'header') ?? '',
        question: truncate(stringProperty(question, 'question') ?? '', 500),
        optionCount: Array.isArray(question.options) ? question.options.length : 0
      }
    })
  }
  return summary
}

function summarizeSystemNotice(summary: JsonObject, payload: Record<string, unknown>): JsonObject {
  copyOptionalStringFields(summary, payload, ['kind', 'trigger', 'mode'])
  copyOptionalNumberFields(summary, payload, ['tokensBefore', 'tokensAfter', 'percentLeftAfter', 'clearedToolResults'])
  return summary
}

function summarizeError(summary: JsonObject, payload: Record<string, unknown>): JsonObject {
  copyOptionalStringFields(summary, payload, ['code'])
  copyOptionalBooleanFields(summary, payload, ['fatal'])
  const message = stringProperty(payload, 'message')
  if (message) summary.message = truncate(message, 500)
  return summary
}

function summarizeFallbackItem(
  summary: JsonObject,
  item: Record<string, unknown>,
  payload: Record<string, unknown>,
  includeOutputs: boolean,
  maxOutputCharsPerItem: number
): JsonObject {
  const text = firstString(
    item.text,
    item.content,
    item.message,
    item.errorMessage,
    payload.text,
    payload.content,
    payload.message,
    payload.errorMessage
  )
  if (text) {
    summary.text = truncate(text, includeOutputs ? maxOutputCharsPerItem : 500)
  }
  if (includeOutputs) {
    const output = firstString(
      payload.aggregatedOutput,
      payload.output,
      payload.result,
      item.aggregatedOutput,
      item.output,
      item.result
    )
    if (output) {
      summary.output = truncate(output, maxOutputCharsPerItem)
    }
  }
  return summary
}

function summarizeInputPart(part: unknown, maxTextChars: number): JsonObject | null {
  if (!isRecord(part)) return null
  const type = stringProperty(part, 'type') ?? 'unknown'
  const summary: JsonObject = { type }
  switch (type) {
    case 'text': {
      const text = stringProperty(part, 'text')
      if (text) summary.text = truncate(text, maxTextChars)
      return summary
    }
    case 'commandRef':
      copyOptionalStringFields(summary, part, ['name', 'argsText', 'rawText'])
      return summary
    case 'skillRef':
      copyOptionalStringFields(summary, part, ['name'])
      return summary
    case 'fileRef':
    case 'localImage':
      copyOptionalStringFields(summary, part, ['path', 'displayPath', 'fileName', 'mimeType'])
      return summary
    case 'image':
      copyOptionalStringFields(summary, part, ['url'])
      return summary
    default:
      copyOptionalStringFields(summary, part, ['name', 'path', 'displayPath', 'url', 'fileName'])
      return summary
  }
}

function addToolOutputPreview(summary: JsonObject, payload: Record<string, unknown>, maxOutputCharsPerItem: number): void {
  const contentItems = Array.isArray(payload.contentItems) ? payload.contentItems : []
  const textItems = contentItems
    .map((item) => isRecord(item) ? stringProperty(item, 'text') : null)
    .filter((text): text is string => Boolean(text))
  if (textItems.length > 0) {
    summary.contentPreview = truncate(textItems.join('\n'), maxOutputCharsPerItem)
  }
  const structuredResult = jsonPreview(payload.structuredResult, maxOutputCharsPerItem)
  if (structuredResult) {
    summary.structuredResultPreview = structuredResult
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

function formatReadThreadText(summary: JsonObject): string {
  const turns = Array.isArray(summary.turns) ? summary.turns : []
  const title = typeof summary.displayName === 'string' && summary.displayName.trim()
    ? summary.displayName.trim()
    : '(untitled)'
  const turnCount = numberProperty(summary, 'turnCount') ?? turns.length
  const queuedInputCount = numberProperty(summary, 'queuedInputCount') ?? 0
  const page = isRecord(summary.page) ? summary.page : {}
  const hasMore = booleanProperty(page, 'hasMore') ?? false
  const firstShown = turns.length === 0 ? 0 : Math.max(1, turnCount - turns.length + 1)
  const lastShown = turns.length === 0 ? 0 : turnCount
  const lines = [
    `Thread ${summary.id}: ${title}`,
    `Status: ${summary.status ?? 'unknown'}`,
    `Runtime: ${formatRuntimeSummary(summary.runtime)}`,
    `Queued inputs: ${queuedInputCount}`,
    `Turns: ${turnCount} total; showing ${firstShown}-${lastShown}${hasMore ? ' (more older turns available)' : ''}`
  ]

  for (const turn of turns) {
    if (!isRecord(turn)) continue
    lines.push('')
    lines.push(formatTurnSummaryLine(turn))
    const items = Array.isArray(turn.items) ? turn.items : []
    if (items.length === 0) {
      lines.push('  - No items')
      continue
    }
    for (const item of items) {
      if (!isRecord(item)) continue
      lines.push(`  - ${formatItemSummaryLine(item)}`)
    }
  }

  return lines.join('\n')
}

function formatRuntimeSummary(runtime: unknown): string {
  if (!isRecord(runtime)) return 'unknown'
  const flags: string[] = []
  for (const key of ['running', 'busy', 'waitingOnApproval', 'waitingOnInput', 'waitingOnPlanConfirmation']) {
    if (booleanProperty(runtime, key) === true) flags.push(key)
  }
  const maintenanceKind = stringProperty(runtime, 'maintenanceKind')
  if (maintenanceKind) flags.push(`maintenance=${maintenanceKind}`)
  return flags.length > 0 ? flags.join(', ') : 'idle'
}

function formatTurnSummaryLine(turn: Record<string, unknown>): string {
  const id = stringProperty(turn, 'id') ?? '(unknown turn)'
  const status = stringProperty(turn, 'status') ?? 'unknown'
  const startedAt = stringProperty(turn, 'startedAt')
  const completedAt = stringProperty(turn, 'completedAt')
  const timestamps = [startedAt, completedAt].filter(Boolean).join(' -> ')
  return `Turn ${id} [${status}]${timestamps ? ` ${timestamps}` : ''}`
}

function formatItemSummaryLine(item: Record<string, unknown>): string {
  const type = stringProperty(item, 'type') ?? 'unknown'
  const status = stringProperty(item, 'status') ?? 'unknown'
  switch (type) {
    case 'userMessage':
      return `User: ${formatTextOrContent(item)}`
    case 'agentMessage':
      return `Agent: ${stringProperty(item, 'text') ?? '(empty message)'}`
    case 'reasoningContent':
      return 'Reasoning content omitted'
    case 'commandExecution':
      return formatCommandExecutionLine(item, status)
    case 'toolExecution':
      return formatToolExecutionLine(item, status)
    case 'toolCall':
      return `Tool call: ${stringProperty(item, 'toolName') ?? '(unknown tool)'}${formatCallId(item)}`
    case 'pluginFunctionCall':
      return `Plugin tool: ${formatQualifiedName(item, 'pluginId', 'functionName')}${formatSuccess(item)}${formatCallId(item)}`
    case 'dynamicToolCall':
      return `Dynamic tool: ${formatQualifiedName(item, 'namespace', 'toolName')}${formatSuccess(item)}${formatCallId(item)}`
    case 'toolResult':
      return `Tool result${formatCallId(item)}${formatSuccess(item)}${formatLengthHint(item, 'resultChars')}`
    case 'approvalRequest':
      return `Approval request: ${stringProperty(item, 'operation') ?? '(unknown operation)'} ${stringProperty(item, 'target') ?? ''}`.trim()
    case 'approvalResponse':
      return `Approval response: ${booleanProperty(item, 'approved') === true ? 'approved' : 'declined'}`
    case 'userInputRequest':
      return `User input request: ${numberProperty(item, 'questionCount') ?? 0} question(s)`
    case 'userInputResponse':
      return 'User input response'
    case 'systemNotice':
      return `System notice: ${stringProperty(item, 'kind') ?? 'unknown'}${stringProperty(item, 'trigger') ? ` (${stringProperty(item, 'trigger')})` : ''}`
    case 'error':
      return `Error: ${stringProperty(item, 'message') ?? stringProperty(item, 'code') ?? 'unknown error'}`
    default:
      return `${type} [${status}]${stringProperty(item, 'text') ? `: ${stringProperty(item, 'text')}` : ''}`
  }
}

function formatTextOrContent(item: Record<string, unknown>): string {
  const text = stringProperty(item, 'text')
  if (text) return text
  const content = Array.isArray(item.content) ? item.content : []
  const parts = content.map(formatInputPartLine).filter(Boolean)
  return parts.length > 0 ? parts.join('; ') : '(empty message)'
}

function formatInputPartLine(part: unknown): string | null {
  if (!isRecord(part)) return null
  const type = stringProperty(part, 'type') ?? 'unknown'
  switch (type) {
    case 'text':
      return stringProperty(part, 'text')
    case 'image':
      return `image ${stringProperty(part, 'url') ?? ''}`.trim()
    case 'localImage':
      return `local image ${firstString(part.fileName, part.displayPath, part.path, part.mimeType) ?? ''}`.trim()
    case 'fileRef':
      return `file ${firstString(part.displayPath, part.path, part.fileName) ?? ''}`.trim()
    case 'commandRef':
      return stringProperty(part, 'rawText') ?? `command ${stringProperty(part, 'name') ?? ''}`.trim()
    case 'skillRef':
      return `skill ${stringProperty(part, 'name') ?? ''}`.trim()
    default:
      return type
  }
}

function formatCommandExecutionLine(item: Record<string, unknown>, status: string): string {
  const parts = [`Command: ${stringProperty(item, 'command') ?? '(unknown command)'}`, `[${status}]`]
  const exitCode = numberProperty(item, 'exitCode')
  if (exitCode != null) parts.push(`exit=${exitCode}`)
  const durationMs = numberProperty(item, 'durationMs')
  if (durationMs != null) parts.push(`durationMs=${durationMs}`)
  const workingDirectory = stringProperty(item, 'workingDirectory')
  if (workingDirectory) parts.push(`cwd=${workingDirectory}`)
  const outputChars = numberProperty(item, 'outputChars')
  if (outputChars != null) parts.push(`outputChars=${outputChars}`)
  return parts.join(' ')
}

function formatToolExecutionLine(item: Record<string, unknown>, status: string): string {
  const parts = [`Tool execution: ${stringProperty(item, 'toolName') ?? '(unknown tool)'}`, `[${status}]`]
  const success = booleanProperty(item, 'success')
  if (success != null) parts.push(`success=${success}`)
  const error = stringProperty(item, 'errorMessage')
  if (error) parts.push(`error=${error}`)
  return parts.join(' ')
}

function formatQualifiedName(item: Record<string, unknown>, prefixKey: string, nameKey: string): string {
  const prefix = stringProperty(item, prefixKey)
  const name = stringProperty(item, nameKey) ?? '(unknown tool)'
  return prefix ? `${prefix}.${name}` : name
}

function formatCallId(item: Record<string, unknown>): string {
  const callId = stringProperty(item, 'callId')
  return callId ? ` callId=${callId}` : ''
}

function formatSuccess(item: Record<string, unknown>): string {
  const success = booleanProperty(item, 'success')
  return success == null ? '' : ` success=${success}`
}

function formatLengthHint(item: Record<string, unknown>, field: string): string {
  const length = numberProperty(item, field)
  return length == null ? '' : ` ${field}=${length}`
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

function numberProperty(value: unknown, key: string): number | null {
  if (!isRecord(value)) return null
  const property = value[key]
  return typeof property === 'number' && Number.isFinite(property) ? property : null
}

function booleanProperty(value: unknown, key: string): boolean | null {
  if (!isRecord(value)) return null
  const property = value[key]
  return typeof property === 'boolean' ? property : null
}

function copyKnownFields(summary: JsonObject, payload: Record<string, unknown>, fields: string[]): JsonObject {
  for (const field of fields) {
    const value = payload[field]
    if (typeof value === 'string' || typeof value === 'boolean' || typeof value === 'number') {
      summary[field] = value
    }
  }
  return summary
}

function copyOptionalStringFields(summary: JsonObject, payload: Record<string, unknown>, fields: string[]): void {
  for (const field of fields) {
    const value = stringProperty(payload, field)
    if (value) summary[field] = value
  }
}

function copyOptionalNumberFields(summary: JsonObject, payload: Record<string, unknown>, fields: string[]): void {
  for (const field of fields) {
    const value = numberProperty(payload, field)
    if (value != null) summary[field] = value
  }
}

function copyOptionalBooleanFields(summary: JsonObject, payload: Record<string, unknown>, fields: string[]): void {
  for (const field of fields) {
    const value = booleanProperty(payload, field)
    if (value != null) summary[field] = value
  }
}

function jsonPreview(value: unknown, maxChars: number): string | null {
  if (value == null) return null
  if (typeof value === 'string') return truncate(value, maxChars)
  try {
    return truncate(JSON.stringify(value), maxChars)
  } catch {
    return null
  }
}

function firstString(...values: unknown[]): string | null {
  for (const value of values) {
    if (typeof value === 'string' && value.trim() !== '') return value
  }
  return null
}

function truncate(value: string, maxChars: number): string {
  return value.length <= maxChars ? value : `${value.slice(0, maxChars)}...`
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
