import type { ConversationItem } from '../types/conversation'
import { resolveCoreToolRenderPlan } from './toolRendererRegistry'

/**
 * What a trusted tool call does, read from its presentation rather than its name. Families whose
 * older records carry no operation resolve to their generic kind (`explore`, `shell`, `todo`).
 */
export type ToolKind =
  | 'read' | 'grep' | 'find' | 'explore'
  | 'write' | 'edit'
  | 'exec' | 'writeStdin' | 'shell'
  | 'webSearch' | 'webFetch' | 'toolSearch'
  | 'spawnAgent' | 'sendMessage' | 'listAgents'
  | 'taskStop'
  | 'lsp' | 'requestUserInput' | 'commitSuggest' | 'createPlan'
  | 'todoWrite' | 'updateTodos' | 'todo'
  | 'skillManage' | 'skillView'
  | 'board' | 'automation' | 'agentBuilder' | 'remoteToolHost'
  | 'workflow'
  | 'nodeRepl'

export type ToolKindSource = Pick<ConversationItem, 'toolName' | 'presentation' | 'source'>

// Dynamic Workflows is DotCraft's own product tool but registers with plugin provenance, so
// it cannot carry a trusted Core presentation.
const WORKFLOW_TOOL_NAME = 'Workflow'

const SUBAGENT_KINDS: Readonly<Record<string, ToolKind>> = {
  spawn: 'spawnAgent',
  sendMessage: 'sendMessage',
  list: 'listAgents'
}

export function resolveToolKind(item: ToolKindSource): ToolKind | null {
  if (item.toolName === WORKFLOW_TOOL_NAME) return 'workflow'
  const plan = resolveCoreToolRenderPlan(item as ConversationItem)
  if (!plan) return null
  const operation = plan.options.operation
  switch (plan.family) {
    case 'readFile':
      return operation === 'read' || operation === 'grep' || operation === 'find' ? operation : 'explore'
    case 'fileWrite':
      return operation === 'edit' ? 'edit' : 'write'
    case 'shell':
      return operation === 'exec' || operation === 'writeStdin' ? operation : 'shell'
    case 'web':
      return operation === 'fetch' ? 'webFetch' : 'webSearch'
    case 'deferredSearch':
      return 'toolSearch'
    case 'todo':
      return operation === 'todoWrite' || operation === 'updateTodos' ? operation : 'todo'
    case 'subagent':
      return typeof operation === 'string' ? SUBAGENT_KINDS[operation] ?? null : null
    case 'task':
      return 'taskStop'
    case 'lsp':
    case 'requestUserInput':
    case 'commitSuggest':
    case 'createPlan':
    case 'skillManage':
    case 'skillView':
    case 'board':
    case 'automation':
    case 'agentBuilder':
    case 'remoteToolHost':
    case 'nodeRepl':
      return plan.family
  }
}

export function resolveToolOperation(item: ToolKindSource): unknown {
  return resolveCoreToolRenderPlan(item as ConversationItem)?.options.operation
}

export type MemoryOperation = 'recall' | 'update'

/** Core's mark on a file tool call that targets the Thread's memory store. */
export function resolveMemoryOperation(item: ToolKindSource): MemoryOperation | null {
  const memory = resolveCoreToolRenderPlan(item as ConversationItem)?.options.memory
  return memory === 'recall' || memory === 'update' ? memory : null
}

export function isShellCommandKind(kind: ToolKind | null): boolean {
  return kind === 'exec' || kind === 'shell'
}

export function isFileWriteKind(kind: ToolKind | null): kind is 'write' | 'edit' {
  return kind === 'write' || kind === 'edit'
}

export function isSubAgentKind(kind: ToolKind | null): boolean {
  return kind != null && Object.values(SUBAGENT_KINDS).includes(kind)
}
