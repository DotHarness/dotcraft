import { isComplete, type ChatHistory, type HistoryItem } from './history'

export type ToolVerb = 'ran' | 'edited' | 'read' | 'searched' | 'used'

export type NoticeKind =
  | 'allowedOnce'
  | 'allowedForSession'
  | 'allowedAlways'
  | 'rejected'
  | 'answered'
  | 'stopped'
  | 'turnFailed'

export type TranscriptEntry =
  | { kind: 'user'; id: string; text: string; added: boolean }
  | { kind: 'assistant'; id: string; text: string; streaming: boolean }
  | { kind: 'reasoning'; id: string; text: string; seconds: number | null }
  | {
      kind: 'tool'
      id: string
      verb: ToolVerb
      subjects: string[]
      code: boolean
      group: 'read-file' | 'edit-file' | null
      added?: number
      removed?: number
    }
  | { kind: 'notice'; id: string; tone: 'neutral' | 'error'; notice: NoticeKind; detail?: string }

type ToolEntry = Extract<TranscriptEntry, { kind: 'tool' }>

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
  code?: boolean
  group?: ToolEntry['group']
  counts?: { added: number; removed: number } | null
}

function toolShape(toolName: string, input: Record<string, unknown>, result: HistoryItem | undefined): ToolShape {
  if (SHELL_TOOLS.has(toolName)) return { verb: 'ran', target: firstLine(str(input.command)), code: true }
  switch (toolName) {
    case 'ReadFile':
      return { verb: 'read', target: baseName(str(input.path)), group: 'read-file' }
    case 'WriteFile':
    case 'EditFile':
      return { verb: 'edited', target: baseName(str(input.path)), group: 'edit-file', counts: fileChangeCounts(result) }
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

function toolEntry(item: HistoryItem, results: Map<string, HistoryItem>): ToolEntry | null {
  const toolName = str(item.payload.toolName)
  if (!toolName || HIDDEN_TOOLS.has(toolName)) return null
  const id = entryId(item)
  if (item.type !== 'toolCall') return { kind: 'tool', id, verb: 'used', subjects: [toolName], code: false, group: null }
  const shape = toolShape(toolName, args(item.payload), results.get(inTurn(item.turnId, str(item.payload.callId))))
  if (!shape.target && !isComplete(item)) return { kind: 'tool', id, verb: shape.verb, subjects: [], code: false, group: null }
  return {
    kind: 'tool',
    id,
    verb: shape.verb,
    subjects: [shape.target || toolName],
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
    .flatMap((entry) => entry.answers)
    .join(', ')
}

function seconds(item: HistoryItem): number | null {
  if (!isComplete(item) || !item.completedAt || !item.createdAt) return null
  return Math.max(1, Math.round((Date.parse(item.completedAt) - Date.parse(item.createdAt)) / 1000))
}

function mergeTool(previous: TranscriptEntry | undefined, next: ToolEntry): boolean {
  if (!previous || previous.kind !== 'tool' || !next.group || previous.group !== next.group) return false
  for (const subject of next.subjects) if (!previous.subjects.includes(subject)) previous.subjects.push(subject)
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
        if (value) out.push({ kind: 'user', id: entryId(item), text: value, added: mode === 'guidance' })
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
        const entry = toolEntry(item, results)
        if (entry && !mergeTool(out[out.length - 1], entry)) out.push(entry)
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
    out.push({ kind: 'user', id: `echo-${echo.clientId}`, text: echo.text, added: echo.added })
  }
  return out
}
