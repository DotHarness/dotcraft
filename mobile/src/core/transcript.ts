import { isComplete, type ChatHistory, type HistoryItem, type HistoryTurn } from './history'
import { parsePlanMarkdown } from './planMarkdown'
import { capacityRetry, dividerOf, failureEntry, originOf, supersededTurns, threadActionOf } from './timelineMarkers'
import { groupTools, type ToolGroupLabel } from './toolGroups'
import { turnChanges, type TurnChanges } from './turnChanges'
import { userImages, userSegments, type ImageSource, type UserSegment } from './userSegments'

export type ToolVerb =
  | 'ran'
  | 'edited'
  | 'read'
  | 'searched'
  | 'used'
  | 'backgroundStarted'
  | 'backgroundStopped'
  | 'spawned'
  | 'messaged'
  | 'listedAgents'
  | 'stoppedShell'
  | 'stoppedAgent'
  | 'stoppedWorkflow'
  | 'stoppingShell'
  | 'stoppingAgent'
  | 'stoppingWorkflow'

export type ToolIcon = 'terminal' | 'declined' | 'stopped' | 'read' | 'search' | 'folder' | 'edit' | 'web' | 'agent' | 'other'

export type TaskWakeKind = 'terminal' | 'subagent' | 'workflow'

export interface TaskWake {
  task: TaskWakeKind
  label: string
  status: string | null
}

interface ToolDetail {
  target: string
  output: string | null
}

export type NoticeKind =
  | 'allowedOnce'
  | 'allowedForSession'
  | 'allowedAlways'
  | 'rejected'
  | 'answered'

export type MessageOrigin = 'workflow' | 'automation' | 'app' | 'goal' | 'thread' | 'subagent'

export interface OriginMark {
  kind: MessageOrigin
  label: string
  threadId: string | null
}

export type DividerKind =
  | 'forked'
  | 'remoteConnected'
  | 'remoteLocal'
  | 'remoteDisconnected'
  | 'channelBound'
  | 'channelUnbound'
  | 'compactedAuto'
  | 'compactedManual'
  | 'compactedReactive'
  | 'usageLimit'

export interface ThreadAction {
  kind: 'created' | 'messaged'
  threadId: string
  name: string | null
  queued: boolean
}

export interface TranscriptOptions {
  stopped?: readonly string[]
  spawnedFrom?: string | null
}

export type TranscriptEntry =
  | {
      kind: 'user'
      id: string
      text: string
      segments: UserSegment[]
      images: ImageSource[]
      added: boolean
      origin: OriginMark | null
      goal: boolean
    }
  | {
      kind: 'assistant'
      id: string
      text: string
      streaming: boolean
      phase: 'commentary' | 'final' | null
      at: string
      copy: boolean
      changes: TurnChanges | null
    }
  | { kind: 'thinking'; id: string; visible: boolean; heading: string | null }
  | {
      kind: 'tool'
      id: string
      verb: ToolVerb
      icon: ToolIcon
      subject: string | null
      details: ToolDetail[]
      images: string[]
      startedAt: string
      category: ToolCategory | null
      web: 'search' | 'fetch' | null
      path: string
      created: boolean
      live: boolean
      failed: boolean
      added?: number
      removed?: number
    }
  | { kind: 'toolGroup'; id: string; label: ToolGroupLabel; icon: ToolIcon; failed: boolean; active: boolean; children: ToolEntry[] }
  | {
      kind: 'activity'
      id: string
      status: 'working' | 'worked' | 'stopped' | 'youStopped'
      startedAt: string | null
      endedAt: string | null
      origin: TaskWake | null
      children: TranscriptEntry[]
    }
  | { kind: 'notice'; id: string; notice: NoticeKind; detail?: string }
  | { kind: 'failure'; id: string; message: string; overloaded: boolean; retry: { delay: number | null } | null }
  | { kind: 'image'; id: string; status: 'inProgress' | 'completed' | 'failed'; uri: string | null; dropped: boolean; error?: string }
  | { kind: 'plan'; id: string; title: string; overview: string; todos: PlanTodo[]; content: string }
  | { kind: 'changes'; id: string; changes: TurnChanges }
  | { kind: 'divider'; id: string; divider: DividerKind; host?: string; channel?: string; target?: string }
  | { kind: 'threadAction'; id: string; action: ThreadAction }

export type ToolEntry = Extract<TranscriptEntry, { kind: 'tool' }>

export type ToolCategory = 'explore' | 'shell' | 'write' | 'web'

const CATEGORIES: Record<string, ToolCategory> = {
  'core.read-file': 'explore',
  'core.lsp': 'explore',
  'core.shell': 'shell',
  'core.file-write': 'write',
  'core.web': 'web',
}

const DONE = new Set(['completed', 'failed', 'cancelled'])

function isBackgroundTaskNotice(payload: Record<string, unknown>): boolean {
  const kind = str(payload.triggerKind)
  return kind === 'terminal' || kind === 'subagent' || (kind === 'workflow' && !!str(payload.triggerStatus))
}

export interface PlanTodo {
  content: string
  status: 'pending' | 'in_progress' | 'completed' | 'cancelled'
}

function todoStatus(value: unknown): PlanTodo['status'] {
  return value === 'in_progress' || value === 'completed' || value === 'cancelled' ? value : 'pending'
}

type ToolKind =
  | 'read' | 'grep' | 'find' | 'explore' | 'edit' | 'shell' | 'writeStdin'
  | 'webSearch' | 'webFetch' | 'skillView' | 'skillManage' | 'requestUserInput' | 'createPlan'
  | 'subagentSpawn' | 'subagentMessage' | 'subagentList' | 'taskStop'

type TaskKind = 'shell' | 'agent' | 'workflow'

const STOP_VERBS: Record<TaskKind, { stopped: ToolVerb; stopping: ToolVerb }> = {
  shell: { stopped: 'stoppedShell', stopping: 'stoppingShell' },
  agent: { stopped: 'stoppedAgent', stopping: 'stoppingAgent' },
  workflow: { stopped: 'stoppedWorkflow', stopping: 'stoppingWorkflow' },
}

function str(value: unknown): string {
  return typeof value === 'string' ? value : ''
}

function resultObject(result: HistoryItem | undefined): Record<string, unknown> {
  const raw = result?.payload.result
  if (typeof raw !== 'string') return {}
  try {
    const value = JSON.parse(raw) as unknown
    return value && typeof value === 'object' ? (value as Record<string, unknown>) : {}
  } catch {
    return {}
  }
}

function taskKindOf(value: unknown, taskId: string): TaskKind {
  if (value === 'shell' || value === 'agent' || value === 'workflow') return value
  if (taskId.startsWith('term_')) return 'shell'
  return taskId.startsWith('run_') ? 'workflow' : 'agent'
}

function args(payload: Record<string, unknown>): Record<string, unknown> {
  const raw = payload.arguments
  if (typeof raw !== 'string') return (raw ?? {}) as Record<string, unknown>
  try {
    return JSON.parse(raw) as Record<string, unknown>
  } catch {
    return {}
  }
}

export function baseName(path: string): string {
  const trimmed = path.replace(/[\\/]+$/, '')
  return trimmed.split(/[\\/]/).pop() || trimmed
}

function firstLine(value: string): string {
  return value.split(/\r?\n/, 1)[0]
}

function fileChangeCounts(result: HistoryItem | undefined): { added: number; removed: number } | null {
  const structured = result?.payload.structuredContent as { changes?: { additions: number; deletions: number }[] } | undefined
  const changes = structured?.changes
  if (!changes?.length) return null
  return {
    added: changes.reduce((sum, change) => sum + change.additions, 0),
    removed: changes.reduce((sum, change) => sum + change.deletions, 0),
  }
}

function inTurn(turnId: string, id: string): string {
  return `${turnId}/${id}`
}

function entryId(item: HistoryItem): string {
  return inTurn(item.turnId, item.id)
}

interface ToolShape {
  verb: ToolVerb
  target: string
  full?: string
  counts?: { added: number; removed: number } | null
  details?: ToolDetail[]
}

function taskStopShape(toolName: string, input: Record<string, unknown>, result: HistoryItem | undefined): ToolShape {
  if (result?.payload.success === false) return { verb: 'used', target: toolName }
  const value = resultObject(result)
  const verbs = STOP_VERBS[taskKindOf(value.kind, str(input.taskId))]
  const message = str(value.lastAgentMessage).trim()
  return {
    verb: !result || value.status === 'stopping' ? verbs.stopping : verbs.stopped,
    target: str(value.label).trim(),
    details: message ? [{ target: message, output: null }] : [],
  }
}

function toolShape(kind: ToolKind | null, toolName: string, input: Record<string, unknown>, result: HistoryItem | undefined): ToolShape {
  const path = str(input.path)
  switch (kind) {
    case 'subagentSpawn': {
      const name = str(resultObject(result).agentNickname) || str(input.agentNickname) || str(input.taskName)
      const prompt = str(input.message).trim()
      return name ? { verb: 'spawned', target: name, details: prompt ? [{ target: prompt, output: null }] : [] } : { verb: 'used', target: toolName }
    }
    case 'subagentMessage': {
      const target = str(input.target)
      const message = str(input.message).trim()
      return target ? { verb: 'messaged', target, details: message ? [{ target: message, output: null }] : [] } : { verb: 'used', target: toolName }
    }
    case 'subagentList':
      return { verb: 'listedAgents', target: '', details: [] }
    case 'taskStop':
      return taskStopShape(toolName, input, result)
    case 'shell':
      return { verb: 'ran', target: firstLine(str(input.command)), full: str(input.command) }
    case 'read':
      return { verb: 'read', target: baseName(path), full: path }
    case 'explore':
      return path ? { verb: 'read', target: baseName(path), full: path } : { verb: 'searched', target: str(input.pattern) }
    case 'edit':
      return { verb: 'edited', target: baseName(path), full: path, counts: fileChangeCounts(result) }
    case 'grep':
    case 'find':
      return { verb: 'searched', target: str(input.pattern) }
    case 'webSearch':
      return { verb: 'searched', target: str(input.query) }
    case 'webFetch':
      return { verb: 'read', target: str(input.url) }
    default:
      return { verb: 'used', target: toolName }
  }
}

function toolIcon(kind: ToolKind | null, result: HistoryItem | undefined): ToolIcon {
  switch (kind) {
    case 'shell': {
      const errorCode = str(result?.payload.errorCode)
      if (errorCode === 'tool_approval_rejected') return 'declined'
      return errorCode === 'tool_cancelled' ? 'stopped' : 'terminal'
    }
    case 'read':
    case 'explore':
    case 'skillView':
      return 'read'
    case 'grep':
      return 'search'
    case 'find':
      return 'folder'
    case 'edit':
    case 'skillManage':
      return 'edit'
    case 'webSearch':
    case 'webFetch':
      return 'web'
    case 'subagentSpawn':
    case 'subagentMessage':
    case 'subagentList':
      return 'agent'
    case 'taskStop':
      return result?.payload.success === false ? 'other' : 'stopped'
    default:
      return 'other'
  }
}

const CODE_MODE_TOOL_NAMES = new Set(['CodeMode', 'CodeModeWait'])
const HIDDEN_COORDINATION_PRESENTATIONS = new Set(['core.sleep', 'core.send-user-message-async'])

function isCodeModeWrapper(item: HistoryItem): boolean {
  if (!CODE_MODE_TOOL_NAMES.has(str(item.payload.toolName)) || item.payload.namespace) return false
  const source = item.payload.source as { kind?: string; sourceId?: string } | null | undefined
  if (source) return source.kind === 'CoreNative' && source.sourceId === 'code-mode'
  return !isComplete(item)
}

function isHiddenCoordinationTool(item: HistoryItem, result: HistoryItem | undefined): boolean {
  const source = (item.payload.source ?? result?.payload.source) as { kind?: unknown } | null | undefined
  return source?.kind === 'CoreNative' && HIDDEN_COORDINATION_PRESENTATIONS.has(presentationOf(item, result).id)
}

type ContentItem = { type?: string; text?: string; dataBase64?: string; mediaType?: string }

function contentItems(item: HistoryItem | undefined): ContentItem[] {
  const value = item?.payload.contentItems
  return Array.isArray(value) ? (value as ContentItem[]) : []
}

function imageUri(mediaType: string, data: string): string {
  return `data:${mediaType || 'image/png'};base64,${data}`
}

function outputOf(item: HistoryItem | undefined): string | null {
  const result = str(item?.payload.result)
  if (result) return result
  const text = contentItems(item)
    .flatMap((entry) => (entry.type === 'text' && entry.text ? [entry.text] : []))
    .join('\n')
  return text || null
}

function imagesOf(item: HistoryItem | undefined): string[] {
  return contentItems(item).flatMap((entry) =>
    entry.type === 'image' && entry.dataBase64?.trim() ? [imageUri(entry.mediaType?.trim() ?? '', entry.dataBase64.trim())] : [],
  )
}

function planEntry(item: HistoryItem, results: Map<string, HistoryItem>): TranscriptEntry | null {
  if (item.type !== 'toolCall' || toolKind(item, undefined) !== 'createPlan') return null
  const result = results.get(inTurn(item.turnId, str(item.payload.callId)))
  if (!result || result.payload.success === false || str(result.payload.result).startsWith('Error:')) return null
  const input = args(item.payload)
  const { title, overview, content } = parsePlanMarkdown(str(input.plan))
  const todos = Array.isArray(input.todos) ? (input.todos as { content?: unknown; status?: unknown }[]) : []
  return {
    kind: 'plan',
    id: entryId(item),
    title,
    overview,
    todos: todos.flatMap((todo) => (str(todo.content).trim() ? [{ content: str(todo.content).trim(), status: todoStatus(todo.status) }] : [])),
    content,
  }
}

function presentationOf(item: HistoryItem, result: HistoryItem | undefined): { id: string; operation: string } {
  const value = (item.payload.presentation ?? result?.payload.presentation) as
    | { presentationId?: unknown; options?: { operation?: unknown } }
    | null
    | undefined
  return { id: str(value?.presentationId), operation: str(value?.options?.operation) }
}

function toolKind(item: HistoryItem, result: HistoryItem | undefined): ToolKind | null {
  const source = (item.payload.source ?? result?.payload.source) as { kind?: unknown } | null | undefined
  if (source?.kind !== 'CoreNative') return null
  const { id, operation } = presentationOf(item, result)
  switch (id) {
    case 'core.read-file':
      return operation === 'read' || operation === 'grep' || operation === 'find' ? operation : 'explore'
    case 'core.file-write':
      return 'edit'
    case 'core.shell':
      return operation === 'writeStdin' ? 'writeStdin' : 'shell'
    case 'core.web':
      return operation === 'fetch' ? 'webFetch' : 'webSearch'
    case 'core.skill-view':
      return 'skillView'
    case 'core.skill-manage':
      return 'skillManage'
    case 'core.request-user-input':
      return 'requestUserInput'
    case 'core.create-plan':
      return 'createPlan'
    case 'core.subagent':
      return operation === 'spawn' ? 'subagentSpawn' : operation === 'sendMessage' ? 'subagentMessage' : operation === 'list' ? 'subagentList' : null
    case 'core.task':
      return operation === 'stop' ? 'taskStop' : null
    default:
      return null
  }
}

function webOperation(operation: string): ToolEntry['web'] {
  return operation === 'search' || operation === 'fetch' ? operation : null
}

function createsFile(result: HistoryItem | undefined): boolean {
  const content = result?.payload.structuredContent as { changes?: { kind?: unknown }[] } | undefined
  return Array.isArray(content?.changes) && content.changes.some((change) => change.kind === 'add')
}

const OPTIONAL_SUBJECT = new Set<ToolVerb>([
  'backgroundStarted',
  'backgroundStopped',
  'stoppedShell',
  'stoppedAgent',
  'stoppedWorkflow',
  'stoppingShell',
  'stoppingAgent',
  'stoppingWorkflow',
])

function shellBackground(execution: HistoryItem | undefined, result: HistoryItem | undefined): 'running' | 'stopped' | null {
  if (!execution || !result) return null
  const status = str(execution.payload.status)
  if (status === 'inProgress') return 'running'
  return status === 'cancelled' && str(execution.payload.sessionId) && result.payload.success !== false ? 'stopped' : null
}

function toolEntry(
  item: HistoryItem,
  results: Map<string, HistoryItem>,
  executions: Map<string, HistoryItem>,
  turnRunning: boolean,
): ToolEntry | null {
  const toolName = str(item.payload.toolName)
  const callId = inTurn(item.turnId, str(item.payload.callId))
  const result = item.type === 'toolCall' ? results.get(callId) : item
  const kind = toolKind(item, result)
  if (!toolName || kind === 'requestUserInput' || isCodeModeWrapper(item) || isHiddenCoordinationTool(item, result)) return null
  const id = entryId(item)
  const input = item.type === 'toolCall' ? args(item.payload) : {}
  const background = kind === 'shell' && item.type === 'toolCall' ? shellBackground(executions.get(callId), result) : null
  let shape: ToolShape = item.type === 'toolCall' ? toolShape(kind, toolName, input, result) : { verb: 'used', target: toolName }
  if (background && !turnRunning) shape = { ...shape, verb: background === 'running' ? 'backgroundStarted' : 'backgroundStopped' }
  const presentation = presentationOf(item, result)
  const live = item.type === 'toolCall' ? !result || (background === 'running' && turnRunning) : !isComplete(item)
  const base = {
    kind: 'tool' as const,
    id,
    verb: shape.verb,
    icon: toolIcon(kind, result),
    images: imagesOf(result),
    startedAt: item.createdAt,
    category: CATEGORIES[presentation.id] ?? null,
    web: webOperation(presentation.operation),
    path: str(input.path),
    created: createsFile(result),
    live,
    failed: !live && (result?.payload.success === false || result?.status === 'failed' || item.status === 'failed'),
  }
  if (!shape.target && !isComplete(item)) return { ...base, subject: null, details: [] }
  return {
    ...base,
    subject: shape.target || (OPTIONAL_SUBJECT.has(shape.verb) ? null : shape.verb === 'listedAgents' ? '' : toolName),
    details: shape.details ?? [{ target: shape.full || shape.target || toolName, output: outputOf(result) }],
    ...shape.counts,
  }
}

function approvalSubject(request: HistoryItem | undefined): string | undefined {
  if (!request) return undefined
  const payload = request.payload
  const type = str(payload.approvalType)
  if (type === 'shell') return firstLine(str(payload.operation)) || undefined
  if (type === 'file') return baseName(str(payload.target)) || undefined
  return str(payload.targetLabel) || str(payload.target) || str(payload.operation) || undefined
}

const DECISION_NOTICE: Record<string, NoticeKind> = {
  accept: 'allowedOnce',
  acceptForSession: 'allowedForSession',
  acceptAlways: 'allowedAlways',
}

function answersOf(payload: Record<string, unknown>): string {
  const response = payload.response as { answers: Record<string, { answers: string[] }> }
  return Object.values(response.answers)
    .flatMap((entry) => entry.answers.map((answer) => answer.replace(/^user_note: /, '')))
    .join(', ')
}

function lastIndex(entries: TranscriptEntry[], test: (entry: TranscriptEntry) => boolean): number {
  for (let index = entries.length - 1; index >= 0; index -= 1) if (test(entries[index])) return index
  return -1
}

function showsWork(entry: TranscriptEntry): boolean {
  switch (entry.kind) {
    case 'tool':
      return entry.live
    case 'toolGroup':
      return entry.children.some((child) => child.live)
    case 'image':
      return entry.status === 'inProgress'
    case 'assistant':
      return entry.streaming
    default:
      return false
  }
}

function hidesThinking(entry: TranscriptEntry): boolean {
  return (entry.kind === 'assistant' && entry.streaming) || (entry.kind === 'image' && entry.status === 'inProgress')
}

function lastIsFinalReply(entries: TranscriptEntry[]): boolean {
  const last = entries.at(-1)
  return last?.kind === 'assistant' && !last.streaming && last.phase !== 'commentary'
}

function thinkingEntry(turnId: string, body: TranscriptEntry[], heading: string | null): TranscriptEntry {
  return { kind: 'thinking', id: `thinking-${turnId}`, visible: !body.some(hidesThinking) && !lastIsFinalReply(body), heading }
}

function layoutTurn(
  turnId: string,
  turn: HistoryTurn | undefined,
  entries: TranscriptEntry[],
  running: boolean,
  changes: TurnChanges | null,
  origin: TaskWake | null = null,
  heading: string | null = null,
  stoppedByYou = false,
): TranscriptEntry[] {
  let start = 0
  while (start < entries.length && entries[start].kind === 'user' && !(entries[start] as { added: boolean }).added) start += 1
  const opening = entries.slice(0, start)
  const body = groupTools(entries.slice(start), running)
  const tail = running ? [thinkingEntry(turnId, body, heading)] : []
  const completed = !running && turn?.status === 'completed'
  const final =
    turn?.status === 'failed'
      ? -1
      : lastIndex(body, (entry) => entry.kind === 'assistant' && (entry.phase === 'final' || (completed && entry.phase === null)))
  if (completed) {
    const footer = lastIndex(body, (entry) => entry.kind === 'assistant')
    if (footer >= 0) body[footer] = { ...(body[footer] as Extract<TranscriptEntry, { kind: 'assistant' }>), copy: true, changes }
    else if (changes) body.push({ kind: 'changes', id: `changes-${turnId}`, changes })
  }
  if (!turn) return [...opening, ...body, ...tail]
  const status: Extract<TranscriptEntry, { kind: 'activity' }>['status'] | null =
    turn.status === 'cancelled' ? (stoppedByYou ? 'youStopped' : 'stopped') : final >= 0 || turn.status === 'completed' ? 'worked' : running ? 'working' : null
  if (!status) return [...opening, ...body, ...tail]
  const finalAt = final >= 0 ? (body[final] as { at: string }).at || null : null
  const activity = {
    kind: 'activity' as const,
    id: `activity-${turnId}`,
    status,
    startedAt: turn.startedAt ?? null,
    endedAt: status === 'worked' ? (finalAt ?? turn.completedAt ?? null) : (turn.completedAt ?? null),
    origin,
  }
  if (status !== 'worked' || final <= 0) return [...opening, { ...activity, children: [] }, ...body, ...tail]
  const before = body.slice(0, final)
  const plan = lastIndex(before, (entry) => entry.kind === 'plan')
  const image = lastIndex(before, (entry) => entry.kind === 'image' && entry.status === 'completed' && entry.uri !== null)
  const pinned = (entry: TranscriptEntry, index: number) =>
    (entry.kind === 'user' && entry.added) || entry.kind === 'divider' || entry.kind === 'threadAction' || index === plan || index === image
  return [
    ...opening,
    { ...activity, children: before.filter((entry, index) => !pinned(entry, index)) },
    ...before.filter(pinned),
    ...body.slice(final),
    ...tail,
  ]
}

export type LatestActivity = { kind: 'tool'; verb: ToolVerb; subject: string | null } | { kind: 'thinking' } | { kind: 'replying' }

function latestIn(entries: TranscriptEntry[]): LatestActivity | null {
  for (let index = entries.length - 1; index >= 0; index--) {
    const entry = entries[index]
    switch (entry.kind) {
      case 'user':
        return null
      case 'tool':
        return { kind: 'tool', verb: entry.verb, subject: entry.subject }
      case 'thinking':
        if (entry.visible && !entries.some(showsWork)) return { kind: 'thinking' }
        break
      case 'assistant':
        if (entry.streaming) return { kind: 'replying' }
        break
      case 'toolGroup':
      case 'activity': {
        const found = latestIn(entry.children)
        if (found) return found
      }
    }
  }
  return null
}

export function latestActivity(history: ChatHistory): LatestActivity | null {
  return latestIn(buildTranscript(history))
}

export function thinkingStatus(reasoning: string): string | null {
  const line = reasoning
    .split(/\r?\n/)
    .map((value) => value.replace(/<!--.*?-->/g, '').trim())
    .filter(Boolean)
    .at(-1)
  if (!line) return null
  const text = line
    .replace(/^#+\s*/, '')
    .replace(/(\*\*|__|\*|_|`)/g, '')
    .trim()
  return text || null
}

export function buildTranscript(history: ChatHistory, workspacePath: string | null = null, options: TranscriptOptions = {}): TranscriptEntry[] {
  const stopped = new Set(options.stopped ?? [])
  const firstTurn = history.olderCursor ? null : (history.turns[0]?.id ?? null)
  let threadOrigin = options.spawnedFrom ?? null
  const approvals = new Map<string, HistoryItem>()
  const results = new Map<string, HistoryItem>()
  const errorTexts = new Map<string, Set<string>>()
  const executions = new Map<string, HistoryItem>()
  for (const item of history.items) {
    if (item.type === 'commandExecution' && str(item.payload.callId)) executions.set(inTurn(item.turnId, str(item.payload.callId)), item)
    if (item.type === 'approvalRequest') approvals.set(inTurn(item.turnId, str(item.payload.requestId)), item)
    if (item.type === 'toolResult') results.set(inTurn(item.turnId, str(item.payload.callId)), item)
    if (item.type === 'error') errorTexts.set(item.turnId, (errorTexts.get(item.turnId) ?? new Set()).add(str(item.payload.message).trim()))
  }
  const superseded = supersededTurns(history)
  const capacity = capacityRetry(history)
  const turns = new Map(history.turns.map((turn) => [turn.id, turn]))
  const result: TranscriptEntry[] = []
  let out: TranscriptEntry[] = []
  let live = false
  let wake: TaskWake | null = null
  let heading: string | null = null
  let threadActions: TranscriptEntry[] = []

  const closeTurn = (turnId: string) => {
    const turn = turns.get(turnId)
    if (turn?.status === 'failed' && turn.error && !errorTexts.get(turnId)?.has(turn.error.trim())) {
      out.push(failureEntry(`turn-${turnId}`, turn.error, turn.providerError))
    }
    if (capacity?.turnId === turnId) {
      const last = out.findLastIndex((entry) => entry.kind === 'failure')
      if (last >= 0) out[last] = { ...(out[last] as Extract<TranscriptEntry, { kind: 'failure' }>), retry: { delay: capacity.delay } }
    }
    out.push(...threadActions)
    const changes = turn?.status === 'completed' ? turnChanges(history, turnId, workspacePath) : null
    result.push(...layoutTurn(turnId, turn, out, turn ? !DONE.has(turn.status) : live, changes, wake, heading, stopped.has(turnId)))
    out = []
    threadActions = []
    live = false
    wake = null
    heading = null
  }

  let currentTurn: string | null = null
  for (const item of history.items) {
    if (superseded.has(item.turnId)) continue
    if (currentTurn !== null && item.turnId !== currentTurn) closeTurn(currentTurn)
    currentTurn = item.turnId
    if (!isComplete(item)) live = true
    const payload = item.payload
    switch (item.type) {
      case 'userMessage': {
        const mode = str(payload.deliveryMode)
        if (isBackgroundTaskNotice(payload)) {
          if (mode !== 'guidance' && !wake && !out.some((entry) => entry.kind === 'user')) {
            wake = { task: str(payload.triggerKind) as TaskWakeKind, label: str(payload.triggerLabel).trim(), status: str(payload.triggerStatus) || null }
          }
          break
        }
        if (mode === 'subagentMailbox') break
        const value = str(payload.text)
        const images = userImages(payload.nativeInputParts)
        if (value || images.length > 0) {
          let origin = originOf(payload)
          if (!origin && threadOrigin && item.turnId === firstTurn && !str(payload.triggerKind)) {
            origin = { kind: 'thread', label: '', threadId: threadOrigin }
          }
          threadOrigin = null
          out.push({
            kind: 'user',
            id: entryId(item),
            text: value,
            segments: userSegments(value, payload.nativeInputParts),
            images,
            added: mode === 'guidance',
            origin,
            goal: payload.sentAsGoal === true,
          })
        }
        break
      }
      case 'agentMessage': {
        const value = str(payload.text)
        if (value) {
          const phase = str(payload.phase)
          out.push({
            kind: 'assistant',
            id: entryId(item),
            text: value,
            streaming: !isComplete(item),
            phase: phase === 'commentary' || phase === 'final' ? phase : null,
            at: item.createdAt,
            copy: false,
            changes: null,
          })
        }
        break
      }
      case 'reasoningContent': {
        heading = thinkingStatus(str(payload.text)) ?? heading
        break
      }
      case 'toolCall':
      case 'mcpToolCall':
      case 'dynamicToolCall': {
        const plan = planEntry(item, results)
        if (plan) {
          out.push(plan)
          break
        }
        const action = threadActionOf(item, item.type === 'toolCall' ? results.get(inTurn(item.turnId, str(item.payload.callId))) : item)
        if (action) {
          const seen = threadActions.some((entry) => entry.kind === 'threadAction' && entry.action.kind === action.kind && entry.action.threadId === action.threadId)
          if (!seen) threadActions.push({ kind: 'threadAction', id: entryId(item), action })
          break
        }
        const turn = turns.get(item.turnId)
        const entry = toolEntry(item, results, executions, turn ? !DONE.has(turn.status) : !isComplete(item))
        if (entry) out.push(entry)
        break
      }
      case 'imageGeneration': {
        const status = str(payload.status) || (isComplete(item) ? 'completed' : 'inProgress')
        const data = str(payload.result).trim()
        out.push({
          kind: 'image',
          id: entryId(item),
          status: status === 'failed' || status === 'completed' ? status : 'inProgress',
          uri: data ? imageUri(str(payload.mediaType).trim(), data) : null,
          dropped: payload.imageDropped === true,
          ...(str(payload.errorMessage) ? { error: str(payload.errorMessage) } : {}),
        })
        break
      }
      case 'approvalResponse': {
        const notice = DECISION_NOTICE[str(payload.decision)] ?? 'rejected'
        const detail = approvalSubject(approvals.get(inTurn(item.turnId, str(payload.requestId))))
        out.push({ kind: 'notice', id: entryId(item), notice, ...(detail ? { detail } : {}) })
        break
      }
      case 'error': {
        const message = str(payload.message)
        if (message) out.push(failureEntry(entryId(item), message, turns.get(item.turnId)?.providerError))
        break
      }
      case 'systemNotice': {
        const divider = dividerOf(item)
        if (divider) out.push(divider)
        break
      }
      case 'userInputResponse': {
        const detail = answersOf(payload)
        out.push({ kind: 'notice', id: entryId(item), notice: 'answered', ...(detail ? { detail } : {}) })
        break
      }
      default:
        break
    }
  }
  if (currentTurn !== null) closeTurn(currentTurn)
  out = result
  for (const echo of history.echoes) {
    const segments = userSegments(echo.text, echo.parts ?? null)
    const images = userImages(echo.parts)
    if (segments.length > 0 || images.length > 0) {
      out.push({ kind: 'user', id: `echo-${echo.clientId}`, text: echo.text, segments, images, added: echo.added, origin: null, goal: false })
    }
  }
  return out
}
