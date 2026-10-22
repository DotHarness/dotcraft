import { translate, type AppLocale } from '../../shared/locales'
import { formatBoardToolLabel, isBoardOperation } from './boardToolDisplay'
import { formatSkillManageRunningLabel } from './skillManageToolDisplay'
import { formatSkillViewRunningLabel } from './skillViewToolDisplay'
import {
  extractBoardArguments,
  extractPartialJsonStringValue,
  extractPlanTitle,
  getFilename,
  getRequestUserInputQuestionCount
} from './toolCallDisplay'
import { resolveMemoryOperation, resolveToolKind, resolveToolOperation, type ToolKindSource } from './toolKind'
import { formatNodeReplLabel } from './nodeReplToolDisplay'
import { formatTaskStopRunningLabel } from './taskStopDisplay'
import { formatChannelMessageRunningLabel } from './channelMessageDisplay'

/**
 * Parsed preview extracted from the streaming JSON delta for a tool call.
 * Populated only for built-in tools with a streaming-aware rendering.
 */
export interface StreamingParsedPreview {
  /** File path preview for WriteFile / EditFile (used to render a diff header). */
  path?: string | null
  /** File content / newText preview for WriteFile / EditFile. */
  content?: string | null
  /** Partial command preview for Exec. */
  command?: string | null
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

function formatRequestUserInputStreamingLabel(rawArgs: string, locale: AppLocale): string {
  let count: number | undefined
  try {
    const parsed = JSON.parse(rawArgs) as unknown
    if (parsed && typeof parsed === 'object' && !Array.isArray(parsed)) {
      count = getRequestUserInputQuestionCount(parsed as Record<string, unknown>)
    }
  } catch {
    count = undefined
  }
  if (count === 1) return translate(locale, 'toolCall.streaming.requestUserInput.single')
  if (count && count > 1) {
    return translate(locale, 'toolCall.streaming.requestUserInput.multiple', { count })
  }
  return translate(locale, 'toolCall.streaming.requestUserInput.generic')
}

function subAgentLabel(locale: AppLocale, key: string): StreamingToolDisplay {
  return { label: translate(locale, key, { name: translate(locale, 'toolCall.subAgent.agent') }) }
}

/**
 * `argumentsPreview` is the concatenated argument JSON fragments received so far
 * and may be invalid JSON. Untrusted tools get a placeholder that never contains it.
 */
export function getStreamingToolDisplay(
  item: ToolKindSource,
  argumentsPreview: string | null | undefined,
  locale: AppLocale
): StreamingToolDisplay {
  const toolName = item.toolName ?? 'tool'
  const rawArgs = argumentsPreview ?? ''
  const kind = resolveToolKind(item)
  if (kind == null) {
    return { label: translate(locale, 'toolCall.streaming.genericExternal', { toolName }) }
  }

  const memory = resolveMemoryOperation(item)
  if (memory === 'recall') return { label: translate(locale, 'toolCall.memory.recalling') }
  if (memory === 'update') return { label: translate(locale, 'toolCall.memory.updating') }

  switch (kind) {
    case 'board': {
      const operation = resolveToolOperation(item)
      if (isBoardOperation(operation)) {
        return { label: formatBoardToolLabel(operation, extractBoardArguments(rawArgs), locale, true) }
      }
      break
    }
    case 'read':
    case 'explore': {
      const path = extractPartialJsonStringValue(rawArgs, 'path')
      const filename = path ? getFilename(path) : ''
      return {
        label: filename
          ? translate(locale, 'toolCall.streaming.readingFile', { filename })
          : translate(locale, 'toolCall.streaming.readingGeneric')
      }
    }
    case 'write': {
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
    case 'edit': {
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
    case 'grep': {
      const pattern = extractPartialJsonStringValue(rawArgs, 'pattern')
      const path = extractPartialJsonStringValue(rawArgs, 'path')
      if (pattern && path) {
        return { label: translate(locale, 'toolCall.streaming.searchingGrepIn', { pattern, path }) }
      }
      if (pattern) {
        return { label: translate(locale, 'toolCall.streaming.searchingGrep', { pattern }) }
      }
      return { label: translate(locale, 'toolCall.streaming.searchingGeneric') }
    }
    case 'find': {
      const pattern = extractPartialJsonStringValue(rawArgs, 'pattern')
      const path = extractPartialJsonStringValue(rawArgs, 'path')
      if (pattern && path) {
        return { label: translate(locale, 'toolCall.streaming.findingFilesIn', { pattern, path }) }
      }
      if (pattern) {
        return { label: translate(locale, 'toolCall.streaming.findingFiles', { pattern }) }
      }
      return { label: translate(locale, 'toolCall.streaming.findingGeneric') }
    }
    case 'nodeRepl':
      return { label: formatNodeReplLabel(item, extractPartialJsonStringValue(rawArgs, 'title'), locale) }
    case 'exec':
    case 'shell': {
      const command = extractPartialJsonStringValue(rawArgs, 'command')
      if (command) {
        const firstLine = command.split('\n')[0] ?? command
        return {
          label: translate(locale, 'toolCall.streaming.runningCommand', { command: firstLine }),
          parsedPreview: { command }
        }
      }
      return { label: translate(locale, 'toolCall.streaming.runningGeneric') }
    }
    case 'webSearch': {
      const query = extractPartialJsonStringValue(rawArgs, 'query')
      return {
        label: query
          ? translate(locale, 'toolCall.streaming.webSearch', { query })
          : translate(locale, 'toolCall.streaming.webSearchGeneric')
      }
    }
    case 'webFetch': {
      const url = extractPartialJsonStringValue(rawArgs, 'url')
      return {
        label: url
          ? translate(locale, 'toolCall.streaming.webFetch', { url })
          : translate(locale, 'toolCall.streaming.webFetchGeneric')
      }
    }
    case 'spawnAgent': {
      const label = extractPartialJsonStringValue(rawArgs, 'agentNickname')
      const task = extractPartialJsonStringValue(rawArgs, 'message')
        ?? extractPartialJsonStringValue(rawArgs, 'agentPrompt')
      const profile = extractPartialJsonStringValue(rawArgs, 'profile')
      if (label) return { label: translate(locale, 'toolCall.streaming.spawnAgent', { label }) }
      if (task) return { label: translate(locale, 'toolCall.streaming.spawnAgentTask', { task }) }
      if (profile) return { label: translate(locale, 'toolCall.streaming.spawnAgentProfile', { profile }) }
      return { label: translate(locale, 'toolCall.streaming.spawnAgentGeneric') }
    }
    case 'workflow': {
      const name = extractPartialJsonStringValue(rawArgs, 'name')
      return {
        label: name
          ? translate(locale, 'toolCall.streaming.workflowNamed', { name })
          : translate(locale, 'toolCall.streaming.workflow')
      }
    }
    case 'sendMessage':
      return subAgentLabel(locale, 'toolCall.subAgent.sendingMessage')
    case 'listAgents':
      return { label: translate(locale, 'toolCall.subAgent.listing') }
    case 'taskStop':
      return { label: formatTaskStopRunningLabel(extractPartialJsonStringValue(rawArgs, 'taskId'), locale) }
    case 'channelMessage':
      return { label: formatChannelMessageRunningLabel(locale) }
    case 'lsp': {
      const op = extractPartialJsonStringValue(rawArgs, 'operation')
      const filePath = extractPartialJsonStringValue(rawArgs, 'filePath')
      const filename = filePath ? getFilename(filePath) : ''
      if (op && filename) return { label: translate(locale, 'toolCall.streaming.lspOpOnFile', { op, filename }) }
      if (op) return { label: translate(locale, 'toolCall.streaming.lspOp', { op }) }
      return { label: translate(locale, 'toolCall.streaming.lspGeneric') }
    }
    case 'toolSearch': {
      const query = extractPartialJsonStringValue(rawArgs, 'query')
        ?? extractPartialJsonStringValue(rawArgs, 'q')
      return {
        label: query
          ? translate(locale, 'toolCall.streaming.searchTools', { query })
          : translate(locale, 'toolCall.streaming.searchToolsGeneric')
      }
    }
    case 'requestUserInput':
      return { label: formatRequestUserInputStreamingLabel(rawArgs, locale) }
    case 'commitSuggest':
      return { label: translate(locale, 'toolCall.streaming.commitSuggest') }
    case 'createPlan': {
      const plan = extractPartialJsonStringValue(rawArgs, 'plan')
      const title = extractPlanTitle(plan)
      return {
        label: title
          ? translate(locale, 'toolCall.streaming.draftingPlanTitled', { title })
          : translate(locale, 'toolCall.streaming.draftingPlan'),
        parsedPreview: { planDraft: { title, plan } }
      }
    }
    case 'todoWrite':
    case 'todo':
      return { label: translate(locale, 'toolCall.streaming.updatingTodos') }
    case 'skillManage':
      return {
        label: formatSkillManageRunningLabel(
          {
            action: extractPartialJsonStringValue(rawArgs, 'action'),
            name: extractPartialJsonStringValue(rawArgs, 'name')
          },
          locale
        )
      }
    case 'skillView':
      return {
        label: formatSkillViewRunningLabel({ name: extractPartialJsonStringValue(rawArgs, 'name') }, locale)
      }
  }
  return { label: translate(locale, 'toolCall.streaming.genericBuiltin', { toolName }) }
}
