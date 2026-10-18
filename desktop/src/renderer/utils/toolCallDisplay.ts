import {
  BookOpen,
  Brain,
  CircleStop,
  Folder,
  Globe,
  MessagesSquare,
  Monitor,
  Pencil,
  Search,
  Send,
  ShieldAlert,
  SquareTerminal,
  Wrench,
  type LucideIcon
} from 'lucide-react'
import { translate, type AppLocale } from '../../shared/locales'
import type { ConversationItem } from '../types/conversation'
import { formatBoardToolLabel, isBoardOperation } from './boardToolDisplay'
import { formatMemoryRecallLabel } from './memoryToolDisplay'
import { formatSkillManageLabel } from './skillManageToolDisplay'
import { formatSkillViewLabel } from './skillViewToolDisplay'
import {
  isShellCommandKind,
  resolveMemoryOperation,
  resolveToolKind,
  resolveToolOperation,
  type ToolKind,
  type ToolKindSource
} from './toolKind'
import { formatInvocationDisplay, invocationNeedsCallingPrefix } from './webToolDisplay'
import { formatNodeReplLabel, getNodeReplSurface } from './nodeReplToolDisplay'

type ToolArgs = Record<string, unknown> | undefined
type PlanTodoLike = { id?: string; content?: string }

const EXPLORE_ICONS: Partial<Record<ToolKind, LucideIcon>> = {
  read: BookOpen,
  explore: BookOpen,
  grep: Search,
  find: Folder
}

/**
 * Best-effort extraction of a string field from a partial JSON fragment.
 * Tolerates incomplete strings by returning whatever has arrived when the
 * closing quote is missing, which keeps streaming previews useful.
 */
export function extractPartialJsonStringValue(json: string, key: string): string | null {
  const keyPattern = `"${key}"`
  const keyIndex = json.indexOf(keyPattern)
  if (keyIndex < 0) return null
  const colonIndex = json.indexOf(':', keyIndex + keyPattern.length)
  if (colonIndex < 0) return null
  const quoteIndex = json.indexOf('"', colonIndex + 1)
  if (quoteIndex < 0) return null

  let escaped = false
  let out = ''
  for (let i = quoteIndex + 1; i < json.length; i += 1) {
    const ch = json[i]
    if (escaped) {
      switch (ch) {
        case 'n': out += '\n'; break
        case 'r': out += '\r'; break
        case 't': out += '\t'; break
        case 'b': out += '\b'; break
        case 'f': out += '\f'; break
        case '\\': out += '\\'; break
        case '"': out += '"'; break
        case '/': out += '/'; break
        case 'u': {
          const codeUnit = json.slice(i + 1, i + 5)
          if (/^[0-9a-fA-F]{4}$/.test(codeUnit)) {
            out += String.fromCharCode(Number.parseInt(codeUnit, 16))
            i += 4
          } else {
            // Preserve incomplete or invalid escapes until the stream supplies
            // enough input to decode them safely on a later render.
            out += '\\u'
          }
          break
        }
        default: out += '\\' + ch; break
      }
      escaped = false
      continue
    }
    if (ch === '\\') {
      escaped = true
      continue
    }
    if (ch === '"') {
      return out
    }
    out += ch
  }

  return out
}

/** While board arguments stream, only the fields that name the call are read. */
export function extractBoardArguments(preview: string): Record<string, unknown> {
  const args: Record<string, unknown> = {}
  for (const key of ['channelName', 'newChannelName', 'threadId', 'query', 'text']) {
    const value = extractPartialJsonStringValue(preview, key)
    if (value) args[key] = value
  }
  return args
}

export function getFilename(path: string): string {
  return path.split(/[\\/]/).pop() ?? path
}

export function extractPlanTitle(markdown: string | null | undefined): string {
  if (!markdown) return ''
  const h1 = markdown
    .replace(/\r\n/g, '\n')
    .replace(/\r/g, '\n')
    .split('\n')
    .find((line) => /^#\s+/.test(line.trimStart()))
  return h1 ? stripMarkdownHeading(h1) : ''
}

function stripMarkdownHeading(line: string): string {
  return line.trim().replace(/^#+\s*/, '').replace(/\s*#+\s*$/, '').trim()
}

function toPositiveInt(value: unknown): number | undefined {
  if (typeof value === 'number' && Number.isFinite(value) && value > 0) {
    return Math.floor(value)
  }
  if (typeof value === 'string' && value.trim().length > 0) {
    const parsed = Number.parseInt(value, 10)
    if (Number.isFinite(parsed) && parsed > 0) {
      return parsed
    }
  }
  return undefined
}

function formatReadFileLabel(args: ToolArgs, locale: AppLocale): string | null {
  const path = typeof args?.path === 'string' ? args.path : ''
  if (!path) return null

  const filename = getFilename(path)
  const start = toPositiveInt(args?.offset)
  const limit = toPositiveInt(args?.limit)

  if (start && limit) {
    const end = start + limit - 1
    return translate(locale, 'toolCall.readFile.range', { filename, start, end })
  }

  if (start) {
    return translate(locale, 'toolCall.readFile.from', { filename, start })
  }

  return translate(locale, 'toolCall.readFile.single', { filename })
}

function normalizeStatus(value: unknown): string {
  return typeof value === 'string' ? value.trim().toLowerCase() : ''
}

function normalizeText(value: unknown): string {
  return typeof value === 'string' ? value.trim() : ''
}

function normalizeSummary(content: string): string {
  return content.replace(/\s+/g, ' ').trim()
}

function asObjectArray(value: unknown): Array<Record<string, unknown>> {
  if (!Array.isArray(value)) return []
  return value.filter((entry): entry is Record<string, unknown> => (
    typeof entry === 'object' && entry != null
  ))
}

export function getRequestUserInputQuestionCount(args: ToolArgs): number | undefined {
  const questions = args?.questions
  return Array.isArray(questions) && questions.length > 0 ? questions.length : undefined
}

function formatRequestUserInputLabel(
  args: ToolArgs,
  locale: AppLocale,
  streaming: boolean
): string {
  const count = getRequestUserInputQuestionCount(args)
  const prefix = streaming ? 'toolCall.streaming.requestUserInput' : 'toolCall.requestUserInput'
  if (count === 1) return translate(locale, `${prefix}.single`)
  if (count && count > 1) return translate(locale, `${prefix}.multiple`, { count })
  return translate(locale, `${prefix}.generic`)
}

function getTodoWriteSummary(args: ToolArgs, preferStarted: boolean): string | null {
  const todos = asObjectArray(args?.todos)
  if (todos.length === 0) return null

  const candidate = preferStarted
    ? todos.find((todo) => normalizeStatus(todo.status) === 'in_progress') ?? todos[0]
    : todos[0]

  const content = typeof candidate.content === 'string' ? candidate.content : ''
  if (!content.trim()) return null
  return normalizeSummary(content)
}

function getUpdateTodosSummary(
  args: ToolArgs,
  planTodos: readonly PlanTodoLike[] | undefined,
  preferStarted: boolean
): string | null {
  const updates = asObjectArray(args?.updates)
  if (updates.length === 0 || !planTodos || planTodos.length === 0) return null

  const candidate = preferStarted
    ? updates.find((update) => normalizeStatus(update.status) === 'in_progress') ?? updates[0]
    : updates[0]

  const id = normalizeText(candidate.id)
  if (!id) return null

  const matched = planTodos.find((todo) => normalizeText(todo?.id) === id)
  const content = normalizeText(matched?.content)
  if (!content) return null
  return normalizeSummary(content)
}

function formatTodoLabel(
  kind: 'todoWrite' | 'updateTodos',
  args: ToolArgs,
  locale: AppLocale,
  planTodos: readonly PlanTodoLike[] | undefined
): string | null {
  if (kind === 'todoWrite') {
    const merge = args?.merge === true
    const todos = asObjectArray(args?.todos)
    const hasInProgress = todos.some((todo) => normalizeStatus(todo.status) === 'in_progress')
    const started = merge && hasInProgress
    const summary = getTodoWriteSummary(args, started)

    if (!merge) {
      return summary
        ? translate(locale, 'toolCall.todo.createWithSummary', { summary })
        : translate(locale, 'toolCall.todo.create')
    }

    if (started) {
      return summary
        ? translate(locale, 'toolCall.todo.startedWithSummary', { summary })
        : translate(locale, 'toolCall.todo.started')
    }

    return summary
      ? translate(locale, 'toolCall.todo.updatedWithSummary', { summary })
      : translate(locale, 'toolCall.todo.updated')
  }

  const updates = asObjectArray(args?.updates)
  const hasInProgress = updates.some((update) => normalizeStatus(update.status) === 'in_progress')
  const summary = getUpdateTodosSummary(args, planTodos, hasInProgress)

  if (hasInProgress) {
    return summary
      ? translate(locale, 'toolCall.todo.startedWithSummary', { summary })
      : translate(locale, 'toolCall.todo.started')
  }

  return summary
    ? translate(locale, 'toolCall.todo.updatedWithSummary', { summary })
    : translate(locale, 'toolCall.todo.updated')
}

export interface ToolCallDisplayOptions {
  planTodos?: readonly PlanTodoLike[]
}

function formatExploredLabel(args: ToolArgs, locale: AppLocale): string {
  const path = (args?.path as string | undefined) ?? (args?.pattern as string | undefined) ?? ''
  const filename = path ? getFilename(path) : ''
  return filename
    ? translate(locale, 'toolCall.explored', { filename })
    : translate(locale, 'toolCall.exploredFiles')
}

export function formatCollapsedToolLabel(
  item: ToolKindSource,
  args: ToolArgs,
  locale: AppLocale,
  options?: ToolCallDisplayOptions
): string {
  const toolName = item.toolName ?? 'tool'
  const kind = resolveToolKind(item)
  const memory = resolveMemoryOperation(item)
  if (memory === 'recall') return formatMemoryRecallLabel(args, locale)
  if (memory === 'update') return translate(locale, 'toolCall.memory.updated.one')
  switch (kind) {
    case 'read':
      return formatReadFileLabel(args, locale) ?? formatExploredLabel(args, locale)
    case 'grep':
    case 'find':
    case 'explore':
      return formatExploredLabel(args, locale)
    case 'board': {
      const operation = resolveToolOperation(item)
      if (isBoardOperation(operation)) return formatBoardToolLabel(operation, args, locale, false)
      break
    }
    case 'todoWrite':
    case 'updateTodos': {
      const todoLabel = formatTodoLabel(kind, args, locale, options?.planTodos)
      if (todoLabel) return todoLabel
      break
    }
    case 'requestUserInput':
      return formatRequestUserInputLabel(args, locale, false)
    case 'createPlan': {
      const planTitle = typeof args?.plan === 'string' ? extractPlanTitle(args.plan) : ''
      const fallbackTitle = typeof args?.title === 'string' ? args.title.trim() : ''
      const title = planTitle || fallbackTitle
      return title
        ? translate(locale, 'toolCall.plan.collapsedLabel', { title })
        : translate(locale, 'toolCall.plan.collapsedLabelFallback')
    }
    case 'workflow': {
      const name = typeof args?.name === 'string' ? args.name.trim() : ''
      return name
        ? translate(locale, 'toolCall.workflow.started', { name })
        : translate(locale, 'toolCall.workflow.startedGeneric')
    }
    case 'write':
    case 'edit': {
      const path = (args?.path as string | undefined) ?? ''
      const filename = path ? getFilename(path) : ''
      return filename
        ? translate(locale, 'toolCall.edited', { filename })
        : translate(locale, 'toolCall.editedFile')
    }
    case 'exec':
    case 'shell': {
      const cmd = (args?.command as string | undefined) ?? toolName
      const firstLine = cmd.split(/\r?\n/, 1)[0] ?? cmd
      return translate(locale, 'toolCall.ran', { cmd: firstLine })
    }
    case 'skillManage':
      return formatSkillManageLabel(args, undefined, locale)
    case 'skillView':
      return formatSkillViewLabel(args, locale)
    case 'nodeRepl':
      return formatNodeReplLabel(item, args?.title, locale)
    case 'webSearch':
    case 'webFetch':
    case 'toolSearch': {
      const invocation = formatInvocationDisplay(kind, args, locale)
      if (invocation) return invocation
      break
    }
  }
  return translate(locale, 'toolCall.called', { toolName })
}

export interface ToolRowIcon {
  icon: LucideIcon
  tone?: 'warning'
}

export function getToolRowIcon(
  item: Pick<ConversationItem, 'type' | 'toolName' | 'presentation' | 'source' | 'errorCode' | 'executionStatus'>
): ToolRowIcon {
  if (item.type === 'webSearch') return { icon: Globe }
  const kind = resolveToolKind(item)
  if (isShellCommandKind(kind)) {
    if (item.errorCode === 'tool_approval_rejected') return { icon: ShieldAlert, tone: 'warning' }
    if (item.errorCode === 'tool_cancelled' || item.executionStatus === 'cancelled') return { icon: CircleStop }
    return { icon: SquareTerminal }
  }
  if (kind === 'taskStop') return { icon: CircleStop }
  if (kind === 'channelMessage') return { icon: Send }
  if (resolveMemoryOperation(item) === 'update') return { icon: Brain }
  const exploreIcon = kind ? EXPLORE_ICONS[kind] : undefined
  if (exploreIcon) return { icon: exploreIcon }
  if (kind === 'board') return { icon: MessagesSquare }
  if (kind === 'skillView') return { icon: BookOpen }
  if (kind === 'write' || kind === 'edit' || kind === 'skillManage') return { icon: Pencil }
  if (kind === 'webSearch' || kind === 'webFetch') return { icon: Globe }
  if (kind === 'nodeRepl') {
    const surface = getNodeReplSurface(item)
    return { icon: surface === 'computer' ? Monitor : surface ? Globe : SquareTerminal }
  }
  return { icon: Wrench }
}

export function formatWorkflowFailureLabel(args: ToolArgs, locale: AppLocale): string {
  const name = typeof args?.name === 'string' ? args.name.trim() : ''
  return name
    ? translate(locale, 'toolCall.workflow.failed', { name })
    : translate(locale, 'toolCall.workflow.failedGeneric')
}

function formatGenericInvocation(item: ToolKindSource, args: ToolArgs): string | null {
  const toolName = item.toolName ?? 'tool'
  if (!args || Object.keys(args).length === 0) return null
  // Tools without a trusted presentation (MCP / external modules) never surface
  // raw argument JSON to the user; show only the tool name as a label.
  if (resolveToolKind(item) == null) {
    return toolName
  }
  const entries = Object.entries(args).map(([k, v]) => `${k}: ${JSON.stringify(v)}`)
  return `${toolName}(${entries.join(', ')})`
}

export function formatExpandedInvocation(
  item: ToolKindSource,
  args: ToolArgs,
  locale: AppLocale,
  options?: ToolCallDisplayOptions
): string | null {
  const kind = resolveToolKind(item)
  switch (kind) {
    case 'read':
      return formatReadFileLabel(args, locale) ?? formatGenericInvocation(item, args)
    case 'todoWrite':
    case 'updateTodos':
      return formatTodoLabel(kind, args, locale, options?.planTodos) ?? formatGenericInvocation(item, args)
    case 'requestUserInput':
      return formatRequestUserInputLabel(args, locale, false)
    case 'webSearch':
    case 'webFetch':
    case 'toolSearch':
      if (!invocationNeedsCallingPrefix(kind, args)) return formatInvocationDisplay(kind, args, locale)
      break
    case 'skillManage':
      return formatSkillManageLabel(args, undefined, locale)
    case 'skillView':
      return formatSkillViewLabel(args, locale)
    case 'nodeRepl':
      return null
  }
  return formatGenericInvocation(item, args)
}

/** The authoritative failure signal for a tool call: execution status first, then the result payload. */
export function isToolExecutionFailure(item: ConversationItem): boolean {
  const executionFailed = item.executionStatus === 'failed'
    || item.executionStatus === 'cancelled'
    || (item.exitCode != null && item.exitCode !== 0)
  if (item.success === false || executionFailed) return true

  const parsedResult = parseToolResultObject(item.result)
  const resultStatus = readString(parsedResult, 'status')?.toLowerCase()
  if (resultStatus === 'timeout') return false
  return resultStatus === 'failed'
    || resultStatus === 'error'
    || resultStatus === 'cancelled'
    || resultStatus === 'canceled'
    || readString(parsedResult, 'error') != null
    || isStructuredError(parsedResult?.error)
}

function isStructuredError(value: unknown): boolean {
  if (value == null || typeof value !== 'object' || Array.isArray(value)) return false
  const error = value as Record<string, unknown>
  return readString(error, 'code') != null || readString(error, 'message') != null
}

export function parseToolResultObject(value: string | undefined): Record<string, unknown> | undefined {
  if (!value) return undefined
  try {
    const parsed = JSON.parse(value) as unknown
    if (typeof parsed === 'string') {
      const nested = JSON.parse(parsed) as unknown
      return typeof nested === 'object' && nested != null ? nested as Record<string, unknown> : undefined
    }
    return typeof parsed === 'object' && parsed != null ? parsed as Record<string, unknown> : undefined
  } catch {
    return undefined
  }
}

function readString(source: Record<string, unknown> | undefined, key: string): string | null {
  const value = source?.[key]
  return typeof value === 'string' && value.trim().length > 0 ? value.trim() : null
}
