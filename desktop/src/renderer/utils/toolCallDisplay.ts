import { translate, type AppLocale } from '../../shared/locales'
import { isShellToolName } from './shellTools'
import {
  CRON_TOOL_NAME,
  formatCronCollapsedLabel
} from './cronToolDisplay'
import {
  SKILL_MANAGE_TOOL_NAME,
  formatSkillManageLabel,
  formatSkillManageRunningLabel
} from './skillManageToolDisplay'
import {
  SKILL_VIEW_TOOL_NAME,
  formatSkillViewLabel,
  formatSkillViewRunningLabel
} from './skillViewToolDisplay'
import {
  formatInvocationDisplay,
  invocationNeedsCallingPrefix,
  isWebToolName
} from './webToolDisplay'

type ToolArgs = Record<string, unknown> | undefined
type PlanTodoLike = { id?: string; content?: string }

const EXPLORE_TOOLS = new Set(['ReadFile', 'GrepFiles', 'FindFiles'])
const REQUEST_USER_INPUT_TOOL_NAME = 'RequestUserInput'
export const FILE_WRITE_TOOLS = new Set(['WriteFile', 'EditFile'])

/**
 * Built-in DotCraft tool names (PascalCase). Tools in this set get bespoke
 * streaming-running copy and parsed parameter previews; tools outside it are
 * treated as external (MCP / module-contributed) and never render raw argument
 * JSON to the user while streaming — we show `Generating parameters for X...`
 * until the final tool-call arrives.
 */
export const BUILTIN_TOOLS = new Set<string>([
  'ReadFile',
  'WriteFile',
  'EditFile',
  'GrepFiles',
  'FindFiles',
  'Exec',
  'WebSearch',
  'WebFetch',
  'SpawnAgent',
  'WaitAgent',
  'SendMessage',
  'FollowupTask',
  'ListAgents',
  'CloseAgent',
  'LSP',
  'SearchTools',
  'tool_search',
  'Cron',
  'CommitSuggest',
  'CreatePlan',
  'UpdateTodos',
  'TodoWrite',
  REQUEST_USER_INPUT_TOOL_NAME,
  'SkillManage',
  'SkillView'
])

export function isBuiltinTool(toolName: string): boolean {
  return BUILTIN_TOOLS.has(toolName)
}

/**
 * Best-effort extraction of a string field from a partial JSON fragment.
 * Tolerates incomplete strings (returns whatever has arrived so far when the
 * closing quote is missing). Mirrors the helper in TUI's `tool_format.rs`.
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

function truncateChars(value: string, max: number): string {
  const arr = Array.from(value)
  if (arr.length <= max) return value
  return arr.slice(0, max).join('') + '...'
}

function getFilename(path: string): string {
  return path.split(/[\\/]/).pop() ?? path
}

function extractPlanTitle(markdown: string | null | undefined): string {
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

function truncateSummary(content: string, maxLen = 28): string {
  const compact = content.replace(/\s+/g, ' ').trim()
  if (compact.length <= maxLen) return compact
  return `${compact.slice(0, maxLen)}...`
}

function asObjectArray(value: unknown): Array<Record<string, unknown>> {
  if (!Array.isArray(value)) return []
  return value.filter((entry): entry is Record<string, unknown> => (
    typeof entry === 'object' && entry != null
  ))
}

function getRequestUserInputQuestionCount(args: ToolArgs): number | undefined {
  const questions = args?.questions
  return Array.isArray(questions) && questions.length > 0 ? questions.length : undefined
}

function getRequestUserInputQuestionCountFromJson(rawArgs: string): number | undefined {
  try {
    const parsed = JSON.parse(rawArgs) as unknown
    if (parsed && typeof parsed === 'object' && !Array.isArray(parsed)) {
      return getRequestUserInputQuestionCount(parsed as ToolArgs)
    }
  } catch {
    return undefined
  }
  return undefined
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

function formatRequestUserInputStreamingLabel(rawArgs: string, locale: AppLocale): string {
  const count = getRequestUserInputQuestionCountFromJson(rawArgs)
  if (count === 1) return translate(locale, 'toolCall.streaming.requestUserInput.single')
  if (count && count > 1) {
    return translate(locale, 'toolCall.streaming.requestUserInput.multiple', { count })
  }
  return translate(locale, 'toolCall.streaming.requestUserInput.generic')
}

function getTodoWriteSummary(args: ToolArgs, preferStarted: boolean): string | null {
  const todos = asObjectArray(args?.todos)
  if (todos.length === 0) return null

  const candidate = preferStarted
    ? todos.find((todo) => normalizeStatus(todo.status) === 'in_progress') ?? todos[0]
    : todos[0]

  const content = typeof candidate.content === 'string' ? candidate.content : ''
  if (!content.trim()) return null
  return truncateSummary(content)
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
  return truncateSummary(content)
}

function formatTodoLabel(
  toolName: string,
  args: ToolArgs,
  locale: AppLocale,
  planTodos: readonly PlanTodoLike[] | undefined
): string | null {
  if (toolName !== 'TodoWrite' && toolName !== 'UpdateTodos') return null

  if (toolName === 'TodoWrite') {
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

export function formatCollapsedToolLabel(
  toolName: string,
  args: ToolArgs,
  locale: AppLocale,
  options?: ToolCallDisplayOptions
): string {
  if (toolName === 'ReadFile') {
    const readLabel = formatReadFileLabel(args, locale)
    if (readLabel) return readLabel
  }

  const todoLabel = formatTodoLabel(toolName, args, locale, options?.planTodos)
  if (todoLabel) return todoLabel

  if (toolName === REQUEST_USER_INPUT_TOOL_NAME) {
    return formatRequestUserInputLabel(args, locale, false)
  }

  if (toolName === 'CreatePlan') {
    const planTitle = typeof args?.plan === 'string' ? extractPlanTitle(args.plan) : ''
    const fallbackTitle = typeof args?.title === 'string' ? args.title.trim() : ''
    const title = planTitle || fallbackTitle
    if (title) {
      return translate(locale, 'toolCall.plan.collapsedLabel', { title })
    }
    return translate(locale, 'toolCall.plan.collapsedLabelFallback')
  }

  if (EXPLORE_TOOLS.has(toolName)) {
    const path = (args?.path as string | undefined) ?? (args?.pattern as string | undefined) ?? ''
    const filename = path ? getFilename(path) : ''
    return filename
      ? translate(locale, 'toolCall.explored', { filename })
      : translate(locale, 'toolCall.exploredFiles')
  }

  if (FILE_WRITE_TOOLS.has(toolName)) {
    const path = (args?.path as string | undefined) ?? ''
    const filename = path ? getFilename(path) : ''
    return filename
      ? translate(locale, 'toolCall.edited', { filename })
      : translate(locale, 'toolCall.editedFile')
  }

  if (isShellToolName(toolName)) {
    const cmd = (args?.command as string | undefined) ?? toolName
    const short = cmd.length > 40 ? cmd.slice(0, 40) + '…' : cmd
    return translate(locale, 'toolCall.ran', { cmd: short })
  }

  if (toolName === CRON_TOOL_NAME) {
    return formatCronCollapsedLabel(args, locale)
  }

  if (toolName === SKILL_MANAGE_TOOL_NAME) {
    return formatSkillManageLabel(args, undefined, locale)
  }

  if (toolName === SKILL_VIEW_TOOL_NAME) {
    return formatSkillViewLabel(args, locale)
  }

  if (isWebToolName(toolName)) {
    const inv = formatInvocationDisplay(toolName, args, locale)
    if (inv) return inv
  }

  return translate(locale, 'toolCall.called', { toolName })
}

function formatGenericInvocation(toolName: string, args: ToolArgs): string | null {
  if (!args || Object.keys(args).length === 0) return null
  // Non-built-in tools (MCP / external modules) never surface raw argument
  // JSON to the user; show only the tool name as a label.
  if (!isBuiltinTool(toolName)) {
    return toolName
  }
  const entries = Object.entries(args).map(([k, v]) => `${k}: ${JSON.stringify(v)}`)
  return `${toolName}(${entries.join(', ')})`
}

/**
 * Parsed preview extracted from the streaming JSON delta for a tool call.
 * Populated only for built-in tools with a streaming-aware rendering.
 */
export interface StreamingParsedPreview {
  /** File path preview for WriteFile / EditFile (used to render a diff header). */
  path?: string | null
  /** File content / newText preview for WriteFile / EditFile. */
  content?: string | null
  /** Partial CreatePlan draft, populated progressively while streaming. */
  planDraft?: {
    title?: string | null
    plan?: string | null
  }
}

export interface StreamingToolDisplay {
  /** Running label shown next to the spinner. */
  label: string
  /** Parsed preview data; absent for non-built-in tools. */
  parsedPreview?: StreamingParsedPreview
}

/**
 * Builds the running label + parsed preview for a streaming tool call.
 *
 * `argumentsPreview` is the concatenated argument JSON fragments received so
 * far (may be invalid JSON). For built-in tools we extract known parameter
 * fields and return a human-readable present-progress label; for unknown
 * tools we return a generic placeholder that never contains raw JSON.
 */
export function getStreamingToolDisplay(
  toolName: string,
  argumentsPreview: string | null | undefined,
  locale: AppLocale
): StreamingToolDisplay {
  const rawArgs = argumentsPreview ?? ''

  if (!isBuiltinTool(toolName)) {
    return {
      label: translate(locale, 'toolCall.streaming.genericExternal', { toolName })
    }
  }

  switch (toolName) {
    case 'ReadFile': {
      const path = extractPartialJsonStringValue(rawArgs, 'path')
      const filename = path ? getFilename(path) : ''
      return {
        label: filename
          ? translate(locale, 'toolCall.streaming.readingFile', { filename })
          : translate(locale, 'toolCall.streaming.readingGeneric')
      }
    }
    case 'WriteFile': {
      const path = extractPartialJsonStringValue(rawArgs, 'path')
      const content = extractPartialJsonStringValue(rawArgs, 'content')
      const filename = path ? getFilename(path) : ''
      return {
        label: filename
          ? translate(locale, 'toolCall.writingFile', { filename })
          : translate(locale, 'toolCall.writingGeneric'),
        parsedPreview: { path, content }
      }
    }
    case 'EditFile': {
      const path = extractPartialJsonStringValue(rawArgs, 'path')
      const content = extractPartialJsonStringValue(rawArgs, 'newText')
        ?? extractPartialJsonStringValue(rawArgs, 'content')
      const filename = path ? getFilename(path) : ''
      return {
        label: filename
          ? translate(locale, 'toolCall.editingFile', { filename })
          : translate(locale, 'toolCall.editingGeneric'),
        parsedPreview: { path, content }
      }
    }
    case 'GrepFiles': {
      const pattern = extractPartialJsonStringValue(rawArgs, 'pattern')
      const path = extractPartialJsonStringValue(rawArgs, 'path')
      const truncatedPattern = pattern ? truncateChars(pattern, 40) : ''
      if (truncatedPattern && path) {
        return {
          label: translate(locale, 'toolCall.streaming.searchingGrepIn', {
            pattern: truncatedPattern,
            path
          })
        }
      }
      if (truncatedPattern) {
        return {
          label: translate(locale, 'toolCall.streaming.searchingGrep', {
            pattern: truncatedPattern
          })
        }
      }
      return { label: translate(locale, 'toolCall.streaming.searchingGeneric') }
    }
    case 'FindFiles': {
      const pattern = extractPartialJsonStringValue(rawArgs, 'pattern')
      const path = extractPartialJsonStringValue(rawArgs, 'path')
      const truncatedPattern = pattern ? truncateChars(pattern, 40) : ''
      if (truncatedPattern && path) {
        return {
          label: translate(locale, 'toolCall.streaming.findingFilesIn', {
            pattern: truncatedPattern,
            path
          })
        }
      }
      if (truncatedPattern) {
        return {
          label: translate(locale, 'toolCall.streaming.findingFiles', {
            pattern: truncatedPattern
          })
        }
      }
      return { label: translate(locale, 'toolCall.streaming.findingGeneric') }
    }
    case 'Exec': {
      const command = extractPartialJsonStringValue(rawArgs, 'command')
      if (command) {
        const firstLine = command.split('\n')[0] ?? command
        return {
          label: translate(locale, 'toolCall.streaming.runningCommand', {
            command: truncateChars(firstLine, 80)
          })
        }
      }
      return { label: translate(locale, 'toolCall.streaming.runningGeneric') }
    }
    case 'WebSearch': {
      const query = extractPartialJsonStringValue(rawArgs, 'query')
      if (query) {
        return {
          label: translate(locale, 'toolCall.streaming.webSearch', {
            query: truncateChars(query, 80)
          })
        }
      }
      return { label: translate(locale, 'toolCall.streaming.webSearchGeneric') }
    }
    case 'WebFetch': {
      const url = extractPartialJsonStringValue(rawArgs, 'url')
      if (url) {
        return {
          label: translate(locale, 'toolCall.streaming.webFetch', {
            url: truncateChars(url, 80)
          })
        }
      }
      return { label: translate(locale, 'toolCall.streaming.webFetchGeneric') }
    }
    case 'SpawnAgent': {
      const label = extractPartialJsonStringValue(rawArgs, 'agentNickname')
      const task = extractPartialJsonStringValue(rawArgs, 'message')
        ?? extractPartialJsonStringValue(rawArgs, 'agentPrompt')
      const profile = extractPartialJsonStringValue(rawArgs, 'profile')
      if (label) {
        return {
          label: translate(locale, 'toolCall.streaming.spawnAgent', {
            label: truncateChars(label, 60)
          })
        }
      }
      if (task) {
        return {
          label: translate(locale, 'toolCall.streaming.spawnAgentTask', {
            task: truncateChars(task, 60)
          })
        }
      }
      if (profile) {
        return {
          label: translate(locale, 'toolCall.streaming.spawnAgentProfile', {
            profile: truncateChars(profile, 40)
          })
        }
      }
      return { label: translate(locale, 'toolCall.streaming.spawnAgentGeneric') }
    }
    case 'WaitAgent': {
      return {
        label: translate(locale, 'toolCall.subAgent.waiting', {
          name: translate(locale, 'toolCall.subAgent.agent')
        })
      }
    }
    case 'SendMessage': {
      return {
        label: translate(locale, 'toolCall.subAgent.sendingMessage', {
          name: translate(locale, 'toolCall.subAgent.agent')
        })
      }
    }
    case 'FollowupTask': {
      return {
        label: translate(locale, 'toolCall.subAgent.followingUp', {
          name: translate(locale, 'toolCall.subAgent.agent')
        })
      }
    }
    case 'ListAgents': {
      return {
        label: translate(locale, 'toolCall.subAgent.listing')
      }
    }
    case 'CloseAgent': {
      return {
        label: translate(locale, 'toolCall.subAgent.closing', {
          name: translate(locale, 'toolCall.subAgent.agent')
        })
      }
    }
    case 'LSP': {
      const op = extractPartialJsonStringValue(rawArgs, 'operation')
      const filePath = extractPartialJsonStringValue(rawArgs, 'filePath')
      const filename = filePath ? getFilename(filePath) : ''
      if (op && filename) {
        return {
          label: translate(locale, 'toolCall.streaming.lspOpOnFile', { op, filename })
        }
      }
      if (op) {
        return { label: translate(locale, 'toolCall.streaming.lspOp', { op }) }
      }
      return { label: translate(locale, 'toolCall.streaming.lspGeneric') }
    }
    case 'SearchTools':
    case 'tool_search': {
      const query = extractPartialJsonStringValue(rawArgs, 'query')
        ?? extractPartialJsonStringValue(rawArgs, 'q')
      if (query) {
        return {
          label: translate(locale, 'toolCall.streaming.searchTools', {
            query: truncateChars(query, 60)
          })
        }
      }
      return { label: translate(locale, 'toolCall.streaming.searchToolsGeneric') }
    }
    case REQUEST_USER_INPUT_TOOL_NAME: {
      return { label: formatRequestUserInputStreamingLabel(rawArgs, locale) }
    }
    case 'Cron': {
      const action = extractPartialJsonStringValue(rawArgs, 'action')
      if (action === 'add') return { label: translate(locale, 'toolCall.streaming.cronAdd') }
      if (action === 'list') return { label: translate(locale, 'toolCall.streaming.cronList') }
      if (action === 'remove') return { label: translate(locale, 'toolCall.streaming.cronRemove') }
      return { label: translate(locale, 'toolCall.streaming.cronGeneric') }
    }
    case 'CommitSuggest': {
      return { label: translate(locale, 'toolCall.streaming.commitSuggest') }
    }
    case 'CreatePlan': {
      const plan = extractPartialJsonStringValue(rawArgs, 'plan')
      const title = extractPlanTitle(plan)
      return {
        label: title
          ? translate(locale, 'toolCall.streaming.draftingPlanTitled', {
              title: truncateChars(title, 60)
            })
          : translate(locale, 'toolCall.streaming.draftingPlan'),
        parsedPreview: {
          planDraft: { title, plan }
        }
      }
    }
    case 'TodoWrite':
    case 'UpdateTodos': {
      return { label: translate(locale, 'toolCall.streaming.updatingTodos') }
    }
    case 'SkillManage': {
      return {
        label: formatSkillManageRunningLabel(
          {
            action: extractPartialJsonStringValue(rawArgs, 'action'),
            name: extractPartialJsonStringValue(rawArgs, 'name')
          },
          locale
        )
      }
    }
    case 'SkillView': {
      return {
        label: formatSkillViewRunningLabel(
          { name: extractPartialJsonStringValue(rawArgs, 'name') },
          locale
        )
      }
    }
    default: {
      return {
        label: translate(locale, 'toolCall.streaming.genericBuiltin', { toolName })
      }
    }
  }
}

export function formatExpandedInvocation(
  toolName: string,
  args: ToolArgs,
  locale: AppLocale,
  options?: ToolCallDisplayOptions
): string | null {
  if (toolName === 'ReadFile') {
    return formatReadFileLabel(args, locale) ?? formatGenericInvocation(toolName, args)
  }

  if (toolName === 'TodoWrite' || toolName === 'UpdateTodos') {
    return formatTodoLabel(toolName, args, locale, options?.planTodos)
      ?? formatGenericInvocation(toolName, args)
  }

  if (toolName === REQUEST_USER_INPUT_TOOL_NAME) {
    return formatRequestUserInputLabel(args, locale, false)
  }

  if (isWebToolName(toolName) && !invocationNeedsCallingPrefix(toolName, args)) {
    return formatInvocationDisplay(toolName, args, locale)
  }

  if (toolName === SKILL_MANAGE_TOOL_NAME) {
    return formatSkillManageLabel(args, undefined, locale)
  }

  if (toolName === SKILL_VIEW_TOOL_NAME) {
    return formatSkillViewLabel(args, locale)
  }

  return formatGenericInvocation(toolName, args)
}
