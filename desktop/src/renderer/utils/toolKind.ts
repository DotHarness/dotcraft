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
  | 'spawnAgent' | 'waitAgent' | 'sendMessage' | 'followupTask' | 'listAgents' | 'closeAgent'
  | 'lsp' | 'requestUserInput' | 'commitSuggest' | 'createPlan'
  | 'todoWrite' | 'updateTodos' | 'todo'
  | 'skillManage' | 'skillView'
  | 'board' | 'automation' | 'agentBuilder' | 'remoteToolHost'
  | 'workflow'

export type ToolKindSource = Pick<ConversationItem, 'toolName' | 'presentation' | 'source'>

// Dynamic Workflows is DotCraft's own product tool but registers with plugin provenance, so
// it cannot carry a trusted Core presentation.
const WORKFLOW_TOOL_NAME = 'Workflow'

const SUBAGENT_KINDS: Readonly<Record<string, ToolKind>> = {
  spawn: 'spawnAgent',
  wait: 'waitAgent',
  sendMessage: 'sendMessage',
  followupTask: 'followupTask',
  list: 'listAgents',
  close: 'closeAgent'
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
      return plan.family
  }
}

export function resolveToolOperation(item: ToolKindSource): unknown {
  return resolveCoreToolRenderPlan(item as ConversationItem)?.options.operation
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
