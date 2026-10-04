import { isComplete, type ChatHistory, type HistoryItem } from './history'
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
  | 'stopped'
  | 'turnFailed'

export type TranscriptEntry =
  | { kind: 'user'; id: string; text: string; segments: UserSegment[]; images: string[]; added: boolean }
  | { kind: 'assistant'; id: string; text: string; streaming: boolean }
  | { kind: 'reasoning'; id: string; text: string; seconds: number | null }
  | {
      kind: 'tool'
      id: string
      verb: ToolVerb
      icon: ToolIcon
      subjects: string[]
      details: ToolDetail[]
      images: string[]
      code: boolean
      group: 'read-file' | 'edit-file' | null
      added?: number
      removed?: number
    }
  | { kind: 'notice'; id: string; tone: 'neutral' | 'error'; notice: NoticeKind; detail?: string }
  | { kind: 'image'; id: string; status: 'inProgress' | 'completed' | 'failed'; uri: string | null; dropped: boolean; error?: string }
  | { kind: 'plan'; id: string; title: string; overview: string; todos: PlanTodo[]; content: string }

type ToolEntry = Extract<TranscriptEntry, { kind: 'tool' }>

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
  group?: ToolEntry['group']
  counts?: { added: number; removed: number } | null
}

function toolShape(toolName: string, input: Record<string, unknown>, result: HistoryItem | undefined): ToolShape {
  if (SHELL_TOOLS.has(toolName)) return { verb: 'ran', target: firstLine(str(input.command)), full: str(input.command), code: true }
  const path = str(input.path)
  switch (toolName) {
    case 'ReadFile':
      return { verb: 'read', target: baseName(path), full: path, group: 'read-file' }
    case 'WriteFile':
    case 'EditFile':
      return { verb: 'edited', target: baseName(path), full: path, group: 'edit-file', counts: fileChangeCounts(result) }
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

function toolEntry(item: HistoryItem, results: Map<string, HistoryItem>): ToolEntry | null {
  const toolName = str(item.payload.toolName)
  if (!toolName || HIDDEN_TOOLS.has(toolName) || isCodeModeWrapper(item)) return null
  const id = entryId(item)
  const result = item.type === 'toolCall' ? results.get(inTurn(item.turnId, str(item.payload.callId))) : item
  const shape: ToolShape =
    item.type === 'toolCall' ? toolShape(toolName, args(item.payload), result) : { verb: 'used', target: toolName }
  const base = { kind: 'tool' as const, id, verb: shape.verb, icon: toolIcon(toolName, result), images: imagesOf(result) }
  if (!shape.target && !isComplete(item)) return { ...base, subjects: [], details: [], code: false, group: null }
  return {
    ...base,
    subjects: [shape.target || toolName],
    details: [{ target: shape.full || shape.target || toolName, output: outputOf(result) }],
    code: Boolean(shape.target) && shape.code === true,
    group: shape.group ?? null,
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

function seconds(item: HistoryItem): number | null {
  if (!isComplete(item) || !item.completedAt || !item.createdAt) return null
  return Math.max(1, Math.round((Date.parse(item.completedAt) - Date.parse(item.createdAt)) / 1000))
}

function mergeTool(previous: TranscriptEntry | undefined, next: ToolEntry): boolean {
  if (!previous || previous.kind !== 'tool' || !next.group || previous.group !== next.group) return false
  for (const subject of next.subjects) if (!previous.subjects.includes(subject)) previous.subjects.push(subject)
  previous.details.push(...next.details)
  previous.images.push(...next.images)
  if (next.added !== undefined || next.removed !== undefined) {
    previous.added = (previous.added ?? 0) + (next.added ?? 0)
    previous.removed = (previous.removed ?? 0) + (next.removed ?? 0)
  }
  return true
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
  const out: TranscriptEntry[] = []

  const closeTurn = (turnId: string) => {
    const turn = turns.get(turnId)
    if (turn?.status === 'failed') {
      const detail = turn.error ?? turnErrors.get(turnId)
      out.push({ kind: 'notice', id: `turn-${turnId}`, tone: 'error', notice: 'turnFailed', ...(detail ? { detail } : {}) })
    } else if (turn?.status === 'cancelled') {
      out.push({ kind: 'notice', id: `turn-${turnId}`, tone: 'neutral', notice: 'stopped' })
    }
  }

  let currentTurn: string | null = null
  for (const item of history.items) {
    if (currentTurn !== null && item.turnId !== currentTurn) closeTurn(currentTurn)
    currentTurn = item.turnId
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
        if (value) out.push({ kind: 'assistant', id: entryId(item), text: value, streaming: !isComplete(item) })
        break
      }
      case 'reasoningContent': {
        const value = str(payload.text)
        if (value || !isComplete(item)) out.push({ kind: 'reasoning', id: entryId(item), text: value, seconds: seconds(item) })
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
        if (entry && !mergeTool(out[out.length - 1], entry)) out.push(entry)
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
  for (const echo of history.echoes) {
    const segments = userSegments(echo.text, echo.parts ?? null)
    const images = userImages(echo.parts)
    if (segments.length > 0 || images.length > 0) {
      out.push({ kind: 'user', id: `echo-${echo.clientId}`, text: echo.text, segments, images, added: echo.added })
    }
  }
  return out
}
