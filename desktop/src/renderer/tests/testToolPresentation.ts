import type { ConversationItem } from '../types/conversation'

export function withTestCorePresentation(
  item: ConversationItem,
  presentationId: string,
  options?: Record<string, unknown>,
  sourceToolId = item.toolName
): ConversationItem {
  return {
    ...item,
    source: {
      kind: 'CoreNative',
      sourceId: 'core-native',
      ...(sourceToolId ? { sourceToolId } : {})
    },
    presentation: {
      presentationId,
      ...(options ? { options } : {})
    }
  }
}

// The presentations the server declares on these first-party tools.
const CORE_TOOL_PRESENTATIONS: Readonly<Record<string, readonly [string, string?]>> = {
  ReadFile: ['core.read-file', 'read'],
  GrepFiles: ['core.read-file', 'grep'],
  FindFiles: ['core.read-file', 'find'],
  WriteFile: ['core.file-write', 'write'],
  EditFile: ['core.file-write', 'edit'],
  Exec: ['core.shell', 'exec'],
  WriteStdin: ['core.shell', 'writeStdin'],
  WebSearch: ['core.web', 'search'],
  WebFetch: ['core.web', 'fetch'],
  SearchTools: ['core.deferred-search'],
  SpawnAgent: ['core.subagent', 'spawn'],
  SendMessage: ['core.subagent', 'sendMessage'],
  ListAgents: ['core.subagent', 'list'],
  TaskStop: ['core.task', 'stop'],
  SendChannelMessage: ['core.channel'],
  CreatePlan: ['core.create-plan'],
  TodoWrite: ['core.todo', 'todoWrite'],
  SkillView: ['core.skill-view'],
  LSP: ['core.lsp'],
  RequestUserInput: ['core.request-user-input'],
  CommitSuggest: ['core.commit-suggest']
}

export function coreToolFields(toolName: string): Pick<ConversationItem, 'source' | 'presentation'> {
  const declared = CORE_TOOL_PRESENTATIONS[toolName]
  if (!declared) throw new Error(`No test presentation for ${toolName}`)
  const [presentationId, operation] = declared
  return {
    source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: toolName },
    presentation: { presentationId, ...(operation ? { options: { operation } } : {}) }
  }
}

/** The item/started notification a live server sends before a tool call's first argument delta. */
export function streamingToolCallStarted(turnId: string, itemId: string, toolName: string, callId: string) {
  return {
    turnId,
    item: { id: itemId, type: 'toolCall', payload: { callId, toolName, ...coreToolFields(toolName) } }
  }
}
