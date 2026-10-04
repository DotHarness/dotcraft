import { isComplete, type ChatHistory, type HistoryItem, type HistoryTurn } from './history'
import { parsePlanMarkdown } from './planMarkdown'
import { userImages, userSegments, type UserSegment } from './userSegments'

export type ToolVerb = 'ran' | 'edited' | 'read' | 'searched' | 'used'

export type ToolIcon = 'terminal' | 'declined' | 'stopped' | 'read' | 'search' | 'folder' | 'edit' | 'web' | 'other'

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
  | 'turnFailed'

export type TranscriptEntry =
  | { kind: 'user'; id: string; text: string; segments: UserSegment[]; images: string[]; added: boolean }
  | { kind: 'assistant'; id: string; text: string; streaming: boolean; phase: 'commentary' | 'final' | null; at: string; copy: boolean }
  | { kind: 'reasoning'; id: string; text: string }
  | {
      kind: 'tool'
      id: string
      verb: ToolVerb
      icon: ToolIcon
      subject: string | null
      details: ToolDetail[]
      images: string[]
      code: boolean
      category: ToolCategory | null
      web: 'search' | 'fetch' | null
      path: string
      created: boolean
      live: boolean
      failed: boolean
      added?: number
      removed?: number
    }
  | { kind: 'toolGroup'; id: string; label: ToolGroupLabel; failed: boolean; children: ToolEntry[] }
  | {
      kind: 'activity'
      id: string
      status: 'working' | 'worked' | 'stopped'
      startedAt: string | null
      endedAt: string | null
      children: TranscriptEntry[]
    }
  | { kind: 'notice'; id: string; tone: 'neutral' | 'error'; notice: NoticeKind; detail?: string }
  | { kind: 'image'; id: string; status: 'inProgress' | 'completed' | 'failed'; uri: string | null; dropped: boolean; error?: string }
  | { kind: 'plan'; id: string; title: string; overview: string; todos: PlanTodo[]; content: string }

export type ToolEntry = Extract<TranscriptEntry, { kind: 'tool' }>

export type ToolCategory = 'explore' | 'shell' | 'write' | 'web'

export type ToolGroupLabel =
  | { kind: 'explored' | 'ran' | 'created' | 'modified' | 'webSearched' | 'webFetched' | 'webUsed'; count: number }
  | { kind: 'createdAndModified'; created: number; modified: number }

const CATEGORIES: Record<string, ToolCategory> = {
  'core.read-file': 'explore',
  'core.lsp': 'explore',
  'core.shell': 'shell',
  'core.file-write': 'write',
  'core.web': 'web',
}

const DONE = new Set(['completed', 'failed', 'cancelled'])

export interface PlanTodo {
  content: string
  status: 'pending' | 'in_progress' | 'completed' | 'cancelled'
}

function todoStatus(value: unknown): PlanTodo['status'] {
  return value === 'in_progress' || value === 'completed' || value === 'cancelled' ? value : 'pending'
}

const SHELL_TOOLS = new Set(['Exec', 'RunCommand', 'BashCommand'])
const HIDDEN_TOOLS = new Set(['RequestUserInput'])

function str(value: unknown): string {
  return typeof value === 'string' ? value : ''
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
  code?: boolean
  counts?: { added: number; removed: number } | null
}

function toolShape(toolName: string, input: Record<string, unknown>, result: HistoryItem | undefined): ToolShape {
  if (SHELL_TOOLS.has(toolName)) return { verb: 'ran', target: firstLine(str(input.command)), full: str(input.command), code: true }
  const path = str(input.path)
  switch (toolName) {
    case 'ReadFile':
      return { verb: 'read', target: baseName(path), full: path }
    case 'WriteFile':
    case 'EditFile':
      return { verb: 'edited', target: baseName(path), full: path, counts: fileChangeCounts(result) }
    case 'GrepFiles':
    case 'FindFiles':
      return { verb: 'searched', target: str(input.pattern), code: true }
    case 'WebSearch':
      return { verb: 'searched', target: str(input.query) }
    case 'WebFetch':
      return { verb: 'read', target: str(input.url) }
    default:
      return { verb: 'used', target: toolName }
  }
}

function toolIcon(toolName: string, result: HistoryItem | undefined): ToolIcon {
  if (SHELL_TOOLS.has(toolName)) {
    const errorCode = str(result?.payload.errorCode)
    if (errorCode === 'tool_approval_rejected') return 'declined'
    return errorCode === 'tool_cancelled' ? 'stopped' : 'terminal'
  }
  switch (toolName) {
    case 'ReadFile':
    case 'SkillView':
      return 'read'
    case 'GrepFiles':
      return 'search'
    case 'FindFiles':
      return 'folder'
    case 'WriteFile':
    case 'EditFile':
    case 'SkillManage':
      return 'edit'
    case 'WebSearch':
    case 'WebFetch':
      return 'web'
    default:
      return 'other'
  }
}

function isCodeModeWrapper(item: HistoryItem): boolean {
  if (str(item.payload.toolName) !== 'CodeMode' || item.payload.namespace) return false
  const source = item.payload.source as { kind?: string; sourceId?: string } | null | undefined
  if (source) return source.kind === 'CoreNative' && source.sourceId === 'code-mode'
  return !isComplete(item)
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
  if (item.type !== 'toolCall' || str(item.payload.toolName) !== 'CreatePlan') return null
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

function webOperation(operation: string): ToolEntry['web'] {
  return operation === 'search' || operation === 'fetch' ? operation : null
}

function createsFile(result: HistoryItem | undefined): boolean {
  const content = result?.payload.structuredContent as { changes?: { kind?: unknown }[] } | undefined
  return Array.isArray(content?.changes) && content.changes.some((change) => change.kind === 'add')
}

function toolEntry(item: HistoryItem, results: Map<string, HistoryItem>): ToolEntry | null {
  const toolName = str(item.payload.toolName)
  if (!toolName || HIDDEN_TOOLS.has(toolName) || isCodeModeWrapper(item)) return null
  const id = entryId(item)
  const result = item.type === 'toolCall' ? results.get(inTurn(item.turnId, str(item.payload.callId))) : item
  const input = item.type === 'toolCall' ? args(item.payload) : {}
  const shape: ToolShape = item.type === 'toolCall' ? toolShape(toolName, input, result) : { verb: 'used', target: toolName }
  const presentation = presentationOf(item, result)
  const live = item.type === 'toolCall' ? !result : !isComplete(item)
  const base = {
    kind: 'tool' as const,
    id,
    verb: shape.verb,
    icon: toolIcon(toolName, result),
    images: imagesOf(result),
    category: CATEGORIES[presentation.id] ?? null,
    web: webOperation(presentation.operation),
    path: str(input.path),
    created: createsFile(result),
    live,
    failed: !live && (result?.payload.success === false || result?.status === 'failed' || item.status === 'failed'),
  }
  if (!shape.target && !isComplete(item)) return { ...base, subject: null, details: [], code: false }
  return {
    ...base,
    subject: shape.target || toolName,
    details: [{ target: shape.full || shape.target || toolName, output: outputOf(result) }],
    code: Boolean(shape.target) && shape.code === true,
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

function groupLabel(category: ToolCategory, items: ToolEntry[]): ToolGroupLabel {
  if (category === 'explore') return { kind: 'explored', count: items.length }
  if (category === 'shell') return { kind: 'ran', count: items.length }
  if (category === 'web') {
    const kind = items.every((item) => item.web === 'search') ? 'webSearched' : items.every((item) => item.web === 'fetch') ? 'webFetched' : 'webUsed'
    return { kind, count: items.length }
  }
  const paths = new Map<string, boolean>()
  for (const item of items) paths.set(item.path || item.id, (paths.get(item.path || item.id) ?? false) || item.created)
  const created = [...paths.values()].filter(Boolean).length
  const modified = paths.size - created
  if (created > 0 && modified > 0) return { kind: 'createdAndModified', created, modified }
  return created > 0 ? { kind: 'created', count: created } : { kind: 'modified', count: modified }
}

function aggregate(run: ToolEntry[]): TranscriptEntry[] {
  const out: TranscriptEntry[] = []
  let pending: ToolEntry[] = []
  const flush = () => {
    const category = pending[0]?.category
    if (category && pending.length >= 2) {
      out.push({
        kind: 'toolGroup',
        id: `group-${pending[0].id}`,
        label: groupLabel(category, pending),
        failed: category !== 'shell' && pending.some((item) => item.failed),
        children: pending,
      })
    } else {
      out.push(...pending)
    }
    pending = []
  }
  for (const item of run) {
    if (item.live || !item.category) {
      flush()
      out.push(item)
      continue
    }
    if (pending.length > 0 && pending[0].category !== item.category) flush()
    pending.push(item)
  }
  flush()
  return out
}

function groupTools(body: TranscriptEntry[], running: boolean): TranscriptEntry[] {
  const out: TranscriptEntry[] = []
  for (let index = 0; index < body.length; ) {
    if (body[index].kind !== 'tool') {
      out.push(body[index])
      index += 1
      continue
    }
    let end = index
    while (end < body.length && body[end].kind === 'tool') end += 1
    const run = body.slice(index, end) as ToolEntry[]
    out.push(...(running && end === body.length ? run : aggregate(run)))
    index = end
  }
  return out
}

function lastIndex(entries: TranscriptEntry[], test: (entry: TranscriptEntry) => boolean): number {
  for (let index = entries.length - 1; index >= 0; index -= 1) if (test(entries[index])) return index
  return -1
}

function layoutTurn(turnId: string, turn: HistoryTurn | undefined, entries: TranscriptEntry[], running: boolean): TranscriptEntry[] {
  let start = 0
  while (start < entries.length && entries[start].kind === 'user' && !(entries[start] as { added: boolean }).added) start += 1
  const opening = entries.slice(0, start)
  const body = groupTools(
    entries.slice(start).filter((entry) => entry.kind !== 'reasoning' || running),
    running,
  )
  const completed = !running && turn?.status === 'completed'
  const final =
    turn?.status === 'failed'
      ? -1
      : lastIndex(body, (entry) => entry.kind === 'assistant' && (entry.phase === 'final' || (completed && entry.phase === null)))
  if (completed) {
    const footer = lastIndex(body, (entry) => entry.kind === 'assistant')
    if (footer >= 0) body[footer] = { ...(body[footer] as Extract<TranscriptEntry, { kind: 'assistant' }>), copy: true }
  }
  if (!turn) return [...opening, ...body]
  const status: Extract<TranscriptEntry, { kind: 'activity' }>['status'] | null =
    turn.status === 'cancelled' ? 'stopped' : final >= 0 || turn.status === 'completed' ? 'worked' : running ? 'working' : null
  if (!status) return [...opening, ...body]
  const finalAt = final >= 0 ? (body[final] as { at: string }).at || null : null
  const activity = {
    kind: 'activity' as const,
    id: `activity-${turnId}`,
    status,
    startedAt: turn.startedAt ?? null,
    endedAt: status === 'worked' ? (finalAt ?? turn.completedAt ?? null) : (turn.completedAt ?? null),
  }
  if (status !== 'worked' || final <= 0) return [...opening, { ...activity, children: [] }, ...body]
  const before = body.slice(0, final)
  const plan = lastIndex(before, (entry) => entry.kind === 'plan')
  const image = lastIndex(before, (entry) => entry.kind === 'image' && entry.status === 'completed' && entry.uri !== null)
  const pinned = (entry: TranscriptEntry, index: number) => (entry.kind === 'user' && entry.added) || index === plan || index === image
  return [
    ...opening,
    { ...activity, children: before.filter((entry, index) => !pinned(entry, index)) },
    ...before.filter(pinned),
    ...body.slice(final),
  ]
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

export function buildTranscript(history: ChatHistory): TranscriptEntry[] {
  const approvals = new Map<string, HistoryItem>()
  const results = new Map<string, HistoryItem>()
  const turnErrors = new Map<string, string>()
  for (const item of history.items) {
    if (item.type === 'approvalRequest') approvals.set(inTurn(item.turnId, str(item.payload.requestId)), item)
    if (item.type === 'toolResult') results.set(inTurn(item.turnId, str(item.payload.callId)), item)
    if (item.type === 'error' && str(item.payload.message)) turnErrors.set(item.turnId, str(item.payload.message))
  }
  const turns = new Map(history.turns.map((turn) => [turn.id, turn]))
  const result: TranscriptEntry[] = []
  let out: TranscriptEntry[] = []
  let live = false

  const closeTurn = (turnId: string) => {
    const turn = turns.get(turnId)
    if (turn?.status === 'failed') {
      const detail = turn.error ?? turnErrors.get(turnId)
      out.push({ kind: 'notice', id: `turn-${turnId}`, tone: 'error', notice: 'turnFailed', ...(detail ? { detail } : {}) })
    }
    result.push(...layoutTurn(turnId, turn, out, turn ? !DONE.has(turn.status) : live))
    out = []
    live = false
  }

  let currentTurn: string | null = null
  for (const item of history.items) {
    if (currentTurn !== null && item.turnId !== currentTurn) closeTurn(currentTurn)
    currentTurn = item.turnId
    if (!isComplete(item)) live = true
    const payload = item.payload
    switch (item.type) {
      case 'userMessage': {
        const mode = str(payload.deliveryMode)
        if (mode === 'subagentMailbox') break
        const value = str(payload.text)
        const images = userImages(payload.nativeInputParts)
        if (value || images.length > 0) {
          out.push({
            kind: 'user',
            id: entryId(item),
            text: value,
            segments: userSegments(value, payload.nativeInputParts),
            images,
            added: mode === 'guidance',
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
          })
        }
        break
      }
      case 'reasoningContent': {
        const value = str(payload.text)
        if (!isComplete(item)) out.push({ kind: 'reasoning', id: entryId(item), text: value })
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
        const entry = toolEntry(item, results)
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
        out.push({ kind: 'notice', id: entryId(item), tone: 'neutral', notice, ...(detail ? { detail } : {}) })
        break
      }
      case 'userInputResponse': {
        const detail = answersOf(payload)
        out.push({ kind: 'notice', id: entryId(item), tone: 'neutral', notice: 'answered', ...(detail ? { detail } : {}) })
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
      out.push({ kind: 'user', id: `echo-${echo.clientId}`, text: echo.text, segments, images, added: echo.added })
    }
  }
  return out
}
