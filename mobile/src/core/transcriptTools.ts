import { isComplete, type HistoryItem } from './history'
import { parsePlanMarkdown } from './planMarkdown'
import type { PlanTodo, ToolCategory, ToolDetail, ToolEntry, ToolIcon, ToolVerb, TranscriptEntry } from './transcriptTypes'

const CATEGORIES: Record<string, ToolCategory> = {
  'core.read-file': 'explore',
  'core.lsp': 'explore',
  'core.shell': 'shell',
  'core.file-write': 'write',
  'core.web': 'web',
}

function todoStatus(value: unknown): PlanTodo['status'] {
  return value === 'in_progress' || value === 'completed' || value === 'cancelled' ? value : 'pending'
}

type ToolKind =
  | 'read' | 'grep' | 'find' | 'explore' | 'edit' | 'shell' | 'writeStdin'
  | 'webSearch' | 'webFetch' | 'skillView' | 'requestUserInput' | 'createPlan'
  | 'subagentSpawn' | 'subagentMessage' | 'subagentList' | 'taskStop'

type TaskKind = 'shell' | 'agent' | 'workflow'

const STOP_VERBS: Record<TaskKind, { stopped: ToolVerb; stopping: ToolVerb }> = {
  shell: { stopped: 'stoppedShell', stopping: 'stoppingShell' },
  agent: { stopped: 'stoppedAgent', stopping: 'stoppingAgent' },
  workflow: { stopped: 'stoppedWorkflow', stopping: 'stoppingWorkflow' },
}

export function str(value: unknown): string {
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

export function inTurn(turnId: string, id: string): string {
  return `${turnId}/${id}`
}

export function entryId(item: HistoryItem): string {
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
const HIDDEN_COORDINATION_PRESENTATIONS = new Set(['core.send-user-message-async'])

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

export function imageUri(mediaType: string, data: string): string {
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

export function planEntry(item: HistoryItem, result: HistoryItem | undefined): TranscriptEntry | null {
  if (item.type !== 'toolCall' || toolKind(item, undefined) !== 'createPlan') return null
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

interface Presentation {
  id: string
  operation: string
  skill: string
  skillName: string
  skillFile: string
}

function presentationOf(item: HistoryItem, result: HistoryItem | undefined): Presentation {
  const value = (item.payload.presentation ?? result?.payload.presentation) as
    | { presentationId?: unknown; options?: { operation?: unknown; skill?: unknown; skillName?: unknown; skillFile?: unknown } }
    | null
    | undefined
  const options = value?.options
  return {
    id: str(value?.presentationId),
    operation: str(options?.operation),
    skill: str(options?.skill),
    skillName: str(options?.skillName),
    skillFile: str(options?.skillFile),
  }
}

const SKILL_VERBS: Record<string, ToolVerb> = { read: 'readSkill', create: 'createdSkill', update: 'updatedSkill' }

function skillOf(presentation: Presentation): { verb: ToolVerb; name: string; file: string | null } | null {
  const verb = SKILL_VERBS[presentation.skill]
  if (!verb || !presentation.skillName || (presentation.id !== 'core.read-file' && presentation.id !== 'core.file-write')) return null
  return { verb, name: presentation.skillName, file: presentation.skillFile || null }
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

export function toolEntry(
  item: HistoryItem,
  result: HistoryItem | undefined,
  execution: HistoryItem | undefined,
  turnRunning: boolean,
): ToolEntry | null {
  const toolName = str(item.payload.toolName)
  result = item.type === 'toolCall' ? result : item
  const kind = toolKind(item, result)
  if (!toolName || kind === 'requestUserInput' || isCodeModeWrapper(item) || isHiddenCoordinationTool(item, result)) return null
  const id = entryId(item)
  const input = item.type === 'toolCall' ? args(item.payload) : {}
  const background = kind === 'shell' && item.type === 'toolCall' ? shellBackground(execution, result) : null
  let shape: ToolShape = item.type === 'toolCall' ? toolShape(kind, toolName, input, result) : { verb: 'used', target: toolName }
  if (background && !turnRunning) shape = { ...shape, verb: background === 'running' ? 'backgroundStarted' : 'backgroundStopped' }
  const presentation = presentationOf(item, result)
  const skill = item.type === 'toolCall' ? skillOf(presentation) : null
  if (skill) shape = { ...shape, verb: skill.verb, target: skill.name, full: str(input.path) || shape.full }
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
    ...(skill ? { skill: { name: skill.name, file: skill.file } } : {}),
  }
  if (!shape.target && !isComplete(item)) return { ...base, subject: null, details: [] }
  return {
    ...base,
    subject: shape.target || (OPTIONAL_SUBJECT.has(shape.verb) ? null : shape.verb === 'listedAgents' ? '' : toolName),
    details: shape.details ?? [{ target: shape.full || shape.target || toolName, output: outputOf(result) }],
    ...shape.counts,
  }
}

export function subagentEndingEntry(item: HistoryItem): ToolEntry | null {
  const name = str(item.payload.triggerLabel).trim() || str(item.payload.triggerRefId).trim()
  if (!name) return null
  const status = str(item.payload.triggerStatus)
  return {
    kind: 'tool',
    id: entryId(item),
    verb: status === 'failed' || status === 'cancelled' ? 'agentInterrupted' : 'agentFinished',
    icon: 'agent',
    subject: name,
    details: [],
    images: [],
    startedAt: item.createdAt,
    category: null,
    web: null,
    path: '',
    created: false,
    live: false,
    failed: false,
  }
}
