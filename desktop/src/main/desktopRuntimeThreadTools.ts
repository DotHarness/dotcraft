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
    queuedInputs: thread.queuedInputs ?? [],
    turnCount: turns.length,
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
    items: items.map((item) => summarizeItem(item, includeOutputs, maxOutputCharsPerItem))
  }
}

function summarizeItem(item: unknown, includeOutputs: boolean, maxOutputCharsPerItem: number): JsonObject {
  if (!isRecord(item)) return { type: 'unknown' }
  const summary: JsonObject = {
    id: typeof item.id === 'string' ? item.id : '',
    type: typeof item.type === 'string' ? item.type : 'unknown',
    status: typeof item.status === 'string' ? item.status : 'unknown'
  }
  const text = firstString(item.text, item.content, item.message, item.errorMessage)
  if (text) {
    summary.text = truncate(text, includeOutputs ? maxOutputCharsPerItem : 500)
  }
  if (includeOutputs) {
    const output = firstString(item.aggregatedOutput, item.output, item.result)
    if (output) {
      summary.output = truncate(output, maxOutputCharsPerItem)
    }
  }
  return summary
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
  return `Thread ${summary.id}: ${title}\nStatus: ${summary.status ?? 'unknown'}\nRecent turns: ${turns.length}`
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
