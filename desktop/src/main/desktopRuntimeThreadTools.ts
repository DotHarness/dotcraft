import { handleDesktopThreadHistoryTool, threadHistoryToolDefinitions, threadHistoryToolNames } from './desktopThreadHistoryTools'
import { DesktopThreadBindings } from './desktopThreadBindings'
import { devServerToolDefinitions, devServerToolNames, handleDevServerToolCall } from './devServers/devServerTools'
import devServerGuidance from './prompts/devServers.md?raw'
import cdpDebuggingGuidance from './prompts/cdpDebugging.md?raw'
import { creatorAdditionalContext, creatorContextRevision } from './creatorMode'
import { linkedChatsAdditionalContext, linkedChatsContextFingerprint } from './threadTasks/linkedChatsContext'
import { handleThreadManagementTool, MAX_LIST_THREADS_LIMIT, requestClient, resolveTargetThread } from './threadTasks/threadManagementTools'
import { PROJECT_PROPERTY, ThreadProjectError, type ThreadProject, type ThreadProjectHost } from './threadTasks/threadProjects'
import { themeToolDefinition, themeTool, type DesktopThemeHost } from './themes/themeTools'
import {
  ACTIVATE_PLUGIN_DESCRIPTION,
  ACTIVATE_PLUGIN_PROPERTIES,
  ACTIVATE_PLUGIN_TOOL_NAME,
  LIST_PLUGINS_DESCRIPTION,
  LIST_PLUGINS_TOOL_NAME,
  activatePluginTool,
  listPluginsTool
} from './plugins/pluginTools'
import type { DesktopToolConfirmDetails, DesktopToolConfirmOutcome } from '../shared/desktopToolConfirm'
import {
  listTerminalTabsTool,
  listTerminalTabsToolDefinition,
  readTerminalTool,
  readTerminalToolDefinition,
  type DesktopTerminalHost
} from './desktopTerminalTools'
import type { ThreadTaskConnection, ThreadTaskWorkspace } from './threadTasks/childThreads'
import { readAppServerErrorFields } from '../shared/appServerError'
import {
  CREATE_THREAD_DESCRIPTION,
  CREATE_THREAD_PROPERTIES,
  DISMISS_PROPOSAL_DESCRIPTION,
  DISMISS_PROPOSAL_PROPERTIES,
  THREAD_COORDINATION_GUIDANCE
} from './threadTasks/threadTaskPrompts'
import {
  CREATE_PROJECT_DESCRIPTION,
  CREATE_PROJECT_PROPERTIES,
  CREATE_PROJECT_TOOL_NAME,
  createProjectTool,
  type DesktopProjectCreatorHost
} from './threadTasks/projectTools'
import type { DesktopThreadTasks } from './threadTasks/threadTaskService'
import { createThreadTool, dismissProposalTool } from './threadTasks/threadTaskTools'
import { WRAP_UP_DESCRIPTION, WRAP_UP_PROPERTIES, wrapUpTool } from './threadTasks/wrapUpTool'

export const DESKTOP_THREAD_TOOL_NAMESPACE = 'desktop'
export const DESKTOP_CONVERSATION_TOOL_NAMESPACE = 'desktop_conversation'
export const DESKTOP_THREAD_COORDINATION_CONTEXT_KEY = 'desktop.threadCoordination'
const DEV_SERVERS_CONTEXT_KEY = 'desktop.devServers'
const CDP_CONTEXT_KEY = 'desktop.cdp'

let cdpEndpoint: string | undefined

export function setDesktopCdpEndpoint(endpoint: string | undefined): void {
  cdpEndpoint = endpoint
}

type JsonObject = Record<string, unknown>

export interface RuntimeAdditionalContextEntry {
  kind: 'application'
  value: string
}

export interface DynamicToolFunctionSpec {
  type: 'function'
  name: string
  description: string
  inputSchema: JsonObject
  deferLoading?: boolean
  approval?: { kind: 'remoteResource'; targetArgument: string; operation: string }
}

export interface DynamicToolNamespaceSpec {
  type: 'namespace'
  name: string
  description: string
  tools: DynamicToolFunctionSpec[]
}

export type DynamicToolSpec = DynamicToolFunctionSpec | DynamicToolNamespaceSpec

interface DesktopThreadFunctionAuthoring {
  namespace: string
  name: string
  description: string
  inputSchema: JsonObject
  outputSchema?: JsonObject
  display?: { title?: string; subtitle?: string }
  deferLoading?: boolean
  approval?: DynamicToolFunctionSpec['approval']
}

export interface AppServerRequestClient {
  sendRequest<T = unknown>(method: string, params?: unknown, timeoutMs?: number | null, attachmentOwner?: string, onResponse?: (result: T) => void, onError?: (error: unknown) => void): Promise<T>
}

export interface DesktopAppServerRequestOptions {
  ownerId?: number
  attachmentOwner?: string
  onResponse?: (result: unknown) => void
  onError?: (error: unknown) => void
  supportsDynamicToolRebind?: boolean
  settingsHost?: DesktopThreadToolSettingsHost
  project?: ThreadProject
  projects?: ThreadProjectHost
  projectCreator?: DesktopProjectCreatorHost
  remote?: boolean
  holdThreadRequest?: <T>(request: () => Promise<T>) => Promise<T>
  createChatFolder?: (firstMessage: string) => Promise<string>
  projectWorkspaceFor?: (client: AppServerRequestClient, workspacePath: string) => ThreadTaskWorkspace | undefined
  tasks?: DesktopThreadTasks
  confirmTool?: (details: DesktopToolConfirmDetails, threadId: string, turnId: string | undefined) => Promise<DesktopToolConfirmOutcome>
  terminals?: DesktopTerminalHost
  themes?: DesktopThemeHost
  openDevServerPreview?: (preview: { threadId: string; name: string; url: string }) => void
}

export interface DesktopThreadToolSettingsHost {
  getSettings(): { pinnedThreadIdsByWorkspace?: Record<string, string[]> }
  updateSettings(partial: { pinnedThreadIdsByWorkspace: Record<string, string[]> }): Promise<void>
  onPinnedThreadIdsChanged?(workspacePath: string, threadIds: string[]): void
}

export interface DynamicToolCallParams {
  threadId?: string
  turnId?: string
  callId?: string
  namespace?: string | null
  tool?: string
  arguments?: unknown
}

export type DynamicToolContentItem =
  | { type: 'text'; text: string }
  | { type: 'image'; mediaType: string; url: string; dataBase64?: never }
  | { type: 'image'; mediaType: string; dataBase64: string; url?: never }

export interface DynamicToolCallResult {
  success: boolean
  contentItems?: DynamicToolContentItem[]
  structuredContent?: unknown
  errorCode?: string
  errorMessage?: string
}



const CONVERSATION_TOOL_NAMES = new Set(['CreateThread', 'DismissProposal', 'WrapUp', ACTIVATE_PLUGIN_TOOL_NAME, CREATE_PROJECT_TOOL_NAME])

const TOOL_NAMES = new Set([
  'ListProjects',
  LIST_PLUGINS_TOOL_NAME,
  'ListThreads',
  'FollowThread',
  'UnfollowThread',
  'StopThread',
  ...threadHistoryToolNames,
  'SendMessageToThread',
  'SetThreadTitle',
  'SetThreadArchived',
  'SetThreadPinned',
  'ReadTerminal',
  'ListTerminalTabs',
  'Theme',
  ...devServerToolNames
])

const DESKTOP_THREAD_COORDINATION_CONTEXT: RuntimeAdditionalContextEntry = {
  kind: 'application',
  value: THREAD_COORDINATION_GUIDANCE
}

function desktopThreadContextRevision(threadId: string): string {
  return `${creatorContextRevision(threadId)}\0${linkedChatsContextFingerprint(threadId)}`
}

const bindings = new DesktopThreadBindings({
  withTools: withDesktopThreadDynamicTools,
  includesTools: requestIncludesDesktopThreadTools,
  contextRevision: desktopThreadContextRevision
})

export function resetDesktopThreadToolBindings(client: AppServerRequestClient): void {
  bindings.reset(client)
}

export function buildDesktopThreadDynamicTools(local = true): DynamicToolSpec[] {
  const functions: DesktopThreadFunctionAuthoring[] = [
    {
      namespace: DESKTOP_CONVERSATION_TOOL_NAMESPACE,
      name: 'CreateThread',
      description: CREATE_THREAD_DESCRIPTION,
      inputSchema: {
        type: 'object',
        properties: CREATE_THREAD_PROPERTIES,
        required: [],
        additionalProperties: false
      },
      outputSchema: {
        type: 'object',
        properties: {
          thread: { type: 'object' },
          turn: { type: 'object' },
          started: { type: 'boolean' },
          task: { type: 'object' },
          outcome: { type: 'string' }
        }
      },
      display: { title: 'Create chat', subtitle: 'Desktop' }
    },
    {
      namespace: DESKTOP_CONVERSATION_TOOL_NAMESPACE,
      name: 'DismissProposal',
      description: DISMISS_PROPOSAL_DESCRIPTION,
      inputSchema: {
        type: 'object',
        properties: DISMISS_PROPOSAL_PROPERTIES,
        required: ['taskId'],
        additionalProperties: false
      },
      display: { title: 'Dismiss proposal', subtitle: 'Desktop' }
    },
    {
      namespace: DESKTOP_CONVERSATION_TOOL_NAMESPACE,
      name: 'WrapUp',
      description: WRAP_UP_DESCRIPTION,
      inputSchema: {
        type: 'object',
        properties: WRAP_UP_PROPERTIES,
        required: ['recap'],
        additionalProperties: false
      },
      deferLoading: false
    },
    {
      namespace: DESKTOP_CONVERSATION_TOOL_NAMESPACE,
      name: ACTIVATE_PLUGIN_TOOL_NAME,
      description: ACTIVATE_PLUGIN_DESCRIPTION,
      inputSchema: {
        type: 'object',
        properties: ACTIVATE_PLUGIN_PROPERTIES,
        required: ['id'],
        additionalProperties: false
      },
      display: { title: 'Activate plugin', subtitle: 'Desktop' }
    },
    ...(local ? [{
      namespace: DESKTOP_CONVERSATION_TOOL_NAMESPACE,
      name: CREATE_PROJECT_TOOL_NAME,
      description: CREATE_PROJECT_DESCRIPTION,
      inputSchema: {
        type: 'object',
        properties: CREATE_PROJECT_PROPERTIES,
        additionalProperties: false
      },
      outputSchema: {
        type: 'object',
        properties: {
          project: { type: 'object' },
          createdNewFolder: { type: 'boolean' },
          gitInitialized: { type: 'boolean' }
        }
      },
      display: { title: 'Create project', subtitle: 'Desktop' }
    }] : []),
    {
      namespace: DESKTOP_THREAD_TOOL_NAMESPACE,
      name: 'ListProjects',
      description: 'List the projects Desktop thread tools can reach, including projects that are not running. Use a returned id to target another project.',
      inputSchema: { type: 'object', properties: {}, additionalProperties: false }
    },
    {
      namespace: DESKTOP_THREAD_TOOL_NAMESPACE,
      name: LIST_PLUGINS_TOOL_NAME,
      description: LIST_PLUGINS_DESCRIPTION,
      inputSchema: { type: 'object', properties: {}, additionalProperties: false },
      display: { title: 'List plugins', subtitle: 'Desktop' }
    },
    {
      namespace: DESKTOP_THREAD_TOOL_NAMESPACE,
      name: 'FollowThread',
      description: 'Follow an existing chat for automatic reports without messaging it.',
      inputSchema: {
        type: 'object',
        properties: { threadId: { type: 'string' }, project: PROJECT_PROPERTY },
        required: ['threadId'],
        additionalProperties: false
      },
      display: { title: 'Follow chat', subtitle: 'Desktop' }
    },
    {
      namespace: DESKTOP_THREAD_TOOL_NAMESPACE,
      name: 'UnfollowThread',
      description: 'Stop following a chat added with FollowThread.',
      inputSchema: {
        type: 'object',
        properties: { threadId: { type: 'string' }, project: PROJECT_PROPERTY },
        required: ['threadId'],
        additionalProperties: false
      },
      display: { title: 'Unfollow chat', subtitle: 'Desktop' }
    },
    {
      namespace: DESKTOP_THREAD_TOOL_NAMESPACE,
      name: 'StopThread',
      description: 'Stop the active turn in a chat started by this chat. An idle chat stays open. Do not stop a chat waiting for the user in place of asking them.',
      inputSchema: {
        type: 'object',
        properties: { threadId: { type: 'string' }, project: PROJECT_PROPERTY },
        required: ['threadId'],
        additionalProperties: false
      }
    },
    {
      namespace: DESKTOP_THREAD_TOOL_NAMESPACE,
      name: 'ListThreads',
      description: 'List recent chats in a project, active chats across running projects, or chats linked to this chat.',
      inputSchema: {
        type: 'object',
        properties: {
          project: PROJECT_PROPERTY,
          linked: { type: 'boolean', description: 'List chats started or followed by this chat across projects, newest first. Ignores project.' },
          activeOnly: { type: 'boolean', description: 'Return only active chats (running, waiting for the user, or with background work). With linked false and project omitted, aggregates active chats across running projects without starting stopped ones.' },
          query: { type: 'string', description: 'Optional local text filter for thread id, title, or origin.' },
          limit: { type: 'integer', minimum: 1, maximum: MAX_LIST_THREADS_LIMIT },
          cursor: { type: 'string', description: 'Opaque cursor from a previous ListThreads result.' },
          includeArchived: { type: 'boolean', description: 'Include archived threads in the result page.' }
        },
        additionalProperties: false
      },
      outputSchema: {
        type: 'object',
        properties: {
          threads: { type: 'array', items: { type: 'object' } },
          count: { type: 'integer' },
          nextCursor: { type: ['string', 'null'] },
          totalMatched: { type: 'integer' }
        }
      },
      display: { title: 'List chats', subtitle: 'Desktop' }
    },
    ...threadHistoryToolDefinitions.map(tool => ({
      ...tool,
      inputSchema: { ...tool.inputSchema, properties: { ...tool.inputSchema.properties, project: PROJECT_PROPERTY } }
    })),
    {
      namespace: DESKTOP_THREAD_TOOL_NAMESPACE,
      name: 'SendMessageToThread',
      description: 'Send a follow-up prompt to an existing DotCraft thread without changing Desktop focus.',
      inputSchema: {
        type: 'object',
        properties: {
          threadId: { type: 'string' },
          project: PROJECT_PROPERTY,
          prompt: { type: 'string' },
          model: { type: 'string', description: 'Model for a new turn and subsequent turns; an active turn keeps its current model.' },
          reasoningEffort: {
            type: 'string',
            enum: ['low', 'medium', 'high', 'extraHigh', 'max', 'ultra'],
            description: 'Optional persistent reasoning effort for future turns in the target thread.'
          }
        },
        required: ['threadId', 'prompt'],
        additionalProperties: false
      },
      outputSchema: {
        type: 'object',
        properties: {
          turn: { type: 'object' },
          accepted: { type: 'boolean' }
        }
      },
      display: { title: 'Send message', subtitle: 'Desktop' }
    },
    {
      namespace: DESKTOP_THREAD_TOOL_NAMESPACE,
      name: 'SetThreadTitle',
      description: 'Rename a DotCraft thread.',
      inputSchema: {
        type: 'object',
        properties: {
          threadId: { type: 'string' },
          project: PROJECT_PROPERTY,
          title: { type: 'string' }
        },
        required: ['threadId', 'title'],
        additionalProperties: false
      },
      outputSchema: {
        type: 'object',
        properties: {
          threadId: { type: 'string' },
          title: { type: 'string' }
        }
      },
      display: { title: 'Rename chat', subtitle: 'Desktop' }
    },
    {
      namespace: DESKTOP_THREAD_TOOL_NAMESPACE,
      name: 'SetThreadArchived',
      description: 'Archive or restore a DotCraft thread.',
      inputSchema: {
        type: 'object',
        properties: {
          threadId: { type: 'string' },
          project: PROJECT_PROPERTY,
          archived: { type: 'boolean' }
        },
        required: ['threadId', 'archived'],
        additionalProperties: false
      },
      outputSchema: {
        type: 'object',
        properties: {
          threadId: { type: 'string' },
          archived: { type: 'boolean' }
        }
      },
      display: { title: 'Archive chat', subtitle: 'Desktop' }
    },
    {
      namespace: DESKTOP_THREAD_TOOL_NAMESPACE,
      name: 'SetThreadPinned',
      description: 'Pin or unpin a top-level DotCraft thread in the current Desktop workspace.',
      inputSchema: {
        type: 'object',
        properties: {
          threadId: { type: 'string' },
          project: PROJECT_PROPERTY,
          pinned: { type: 'boolean' }
        },
        required: ['threadId', 'pinned'],
        additionalProperties: false
      },
      outputSchema: {
        type: 'object',
        properties: {
          threadId: { type: 'string' },
          pinned: { type: 'boolean' },
          pinnedThreadIds: { type: 'array', items: { type: 'string' } }
        }
      },
      display: { title: 'Pin chat', subtitle: 'Desktop' }
    },
    readTerminalToolDefinition,
    listTerminalTabsToolDefinition,
    { ...themeToolDefinition, namespace: DESKTOP_THREAD_TOOL_NAMESPACE },
    ...(local ? devServerToolDefinitions.map(tool => ({ ...tool, namespace: DESKTOP_THREAD_TOOL_NAMESPACE })) : [])
  ]

  const namespace = (name: string, description: string): DynamicToolNamespaceSpec => ({
    type: 'namespace',
    name,
    description,
    tools: functions
      .filter((tool) => tool.namespace === name)
      .map(({ namespace: _namespace, outputSchema: _outputSchema, display: _display, ...tool }) => ({
        type: 'function',
        deferLoading: true,
        ...tool
      }))
  })

  return [
    namespace(DESKTOP_CONVERSATION_TOOL_NAMESPACE, 'Desktop cards, confirmations, and the next-step box the user answers.'),
    namespace(DESKTOP_THREAD_TOOL_NAMESPACE, local
      ? 'Desktop chats, plugins, terminal output, theme authoring, and local dev servers.'
      : 'Desktop chats, plugins, terminal output, and theme authoring.')
  ]
}

export function buildDesktopThreadAdditionalContext(local = true): Record<string, RuntimeAdditionalContextEntry> {
  return {
    [DESKTOP_THREAD_COORDINATION_CONTEXT_KEY]: { ...DESKTOP_THREAD_COORDINATION_CONTEXT },
    ...(local ? { [DEV_SERVERS_CONTEXT_KEY]: { kind: 'application', value: devServerGuidance.trimEnd() } } : {}),
    ...(local && cdpEndpoint
      ? { [CDP_CONTEXT_KEY]: { kind: 'application', value: cdpDebuggingGuidance.trimEnd().replace('{{endpoint}}', cdpEndpoint) } }
      : {})
  }
}

export async function sendDesktopAppServerRequest<T = unknown>(
  client: AppServerRequestClient,
  method: string,
  params?: unknown,
  timeoutMs?: number | null,
  options: DesktopAppServerRequestOptions = {}
): Promise<T> {
  return bindings.sendRequest<T>(client, method, params, timeoutMs, options)
}

export async function handleDesktopRuntimeThreadToolCall(
  client: AppServerRequestClient,
  params: unknown,
  workspacePath: string,
  options: DesktopAppServerRequestOptions = {}
): Promise<DynamicToolCallResult | undefined> {
  const p = isRecord(params) ? params as DynamicToolCallParams : {}
  const known = p.namespace === DESKTOP_CONVERSATION_TOOL_NAMESPACE
    ? CONVERSATION_TOOL_NAMES
    : p.namespace === DESKTOP_THREAD_TOOL_NAMESPACE ? TOOL_NAMES : null
  if (!known || !p.tool || !known.has(p.tool)) {
    return undefined
  }

  const args = isRecord(p.arguments) ? p.arguments : {}

  try {
    if (devServerToolNames.has(p.tool)) {
      if (options.remote) return fail('DevServersUnavailable', 'Dev servers are only available in local projects.')
      return await handleDevServerToolCall(client, p, options.openDevServerPreview)
    }
    const connection = threadTaskConnection(client, workspacePath, options)
    if (threadHistoryToolNames.has(p.tool)) {
      const { project: _project, ...historyArgs } = args
      const target = await resolveTargetThread(connection, options.tasks?.linkedChats, args, p.threadId)
      return await handleDesktopThreadHistoryTool(requestClient(target), p.tool, isRecord(p.arguments) ? historyArgs : p.arguments)
    }
    switch (p.tool) {
      case 'CreateThread':
        return await createThreadTool(connection, options.tasks, args, p)
      case CREATE_PROJECT_TOOL_NAME:
        return await createProjectTool(connection, options.tasks, options.projectCreator, args, p)
      case ACTIVATE_PLUGIN_TOOL_NAME:
        return await activatePluginTool(client, options.confirmTool && {
          canSeeCard: async (threadId, turnId) => options.tasks?.canSeeCard(connection, threadId, turnId) ?? false,
          confirm: options.confirmTool
        }, args, p)
      case LIST_PLUGINS_TOOL_NAME:
        return await listPluginsTool(client)
      case 'DismissProposal':
        return dismissProposalTool(options.tasks, args, p)
      case 'WrapUp':
        return await wrapUpTool(connection, options.tasks, args, p)
      case 'ListProjects':
      case 'ListThreads':
      case 'SendMessageToThread':
      case 'FollowThread':
      case 'UnfollowThread':
      case 'StopThread':
      case 'SetThreadTitle':
      case 'SetThreadArchived':
      case 'SetThreadPinned':
        return await handleThreadManagementTool(connection, options.tasks?.linkedChats, options.settingsHost, p.tool, args, p.threadId)
      case 'ReadTerminal':
        return await readTerminalTool(options.terminals, p.threadId, p.arguments)
      case 'ListTerminalTabs':
        return listTerminalTabsTool(options.terminals, p.threadId)
      case 'Theme':
        return await themeTool(options.themes, args, p.threadId)
      default:
        return fail('UnsupportedTool', `Desktop tool '${p.tool}' is not supported.`)
    }
  } catch (error) {
    return fail(mapRequestErrorCode(error), requestErrorMessage(error))
  }
}

export function threadTaskConnection(
  client: AppServerRequestClient,
  workspacePath: string,
  options: DesktopAppServerRequestOptions
): ThreadTaskConnection {
  const hold = options.holdThreadRequest
  return {
    ownerId: options.ownerId,
    workspacePath,
    client: hold
      ? { sendRequest: <T>(method: string, params?: unknown, timeout?: number | null) =>
          hold(() => client.sendRequest<T>(method, params, timeout)) }
      : client,
    project: options.project,
    projects: options.projects,
    remote: options.remote,
    getProjectWorkspace: () => options.projectWorkspaceFor?.(client, workspacePath),
    request: <T>(method: string, params?: unknown, timeoutMs?: number | null) => {
      const send = () => sendDesktopAppServerRequest<T>(client, method, params, timeoutMs, { ...options, attachmentOwner: 'task' })
      return hold ? hold(send) : send()
    },
    ...(options.createChatFolder ? { createChatFolder: options.createChatFolder } : {})
  }
}

export function hasDesktopThreadTools(client: AppServerRequestClient | null, threadId: string, options: DesktopAppServerRequestOptions): boolean {
  return bindings.hasTools(client, threadId, options)
}

function withDesktopThreadDynamicTools(params: unknown, local: boolean): unknown {
  if (!isRecord(params)) return params

  const existing = Array.isArray(params.dynamicTools)
    ? params.dynamicTools.filter((tool) => !isDesktopThreadToolSpec(tool))
    : []
  const existingAdditionalContext = isRecord(params.additionalContext)
    ? params.additionalContext
    : {}

  const startsThread = !('threadId' in params)
  return {
    ...params,
    ...(startsThread
      ? {
          config: {
            ...(isRecord(params.config) ? params.config : {}),
            codeModeExcludedNamespaces: [DESKTOP_CONVERSATION_TOOL_NAMESPACE]
          }
        }
      : {}),
    additionalContext: {
      ...existingAdditionalContext,
      ...buildDesktopThreadAdditionalContext(local),
      ...creatorAdditionalContext(typeof params.threadId === 'string' ? params.threadId : null),
      ...linkedChatsAdditionalContext(typeof params.threadId === 'string' ? params.threadId : null)
    },
    dynamicTools: [
      ...existing,
      ...buildDesktopThreadDynamicTools(local)
    ]
  }
}

function requestIncludesDesktopThreadTools(params: unknown): boolean {
  return isRecord(params)
    && Array.isArray(params.dynamicTools)
    && params.dynamicTools.some(isDesktopThreadToolSpec)
}

function isDesktopThreadToolSpec(value: unknown): boolean {
  if (!isRecord(value)) return false
  return value.type === 'namespace'
    && (value.name === DESKTOP_THREAD_TOOL_NAMESPACE || value.name === DESKTOP_CONVERSATION_TOOL_NAMESPACE)
    && Array.isArray(value.tools)
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return value != null && typeof value === 'object' && !Array.isArray(value)
}

function fail(errorCode: string, errorMessage: string): DynamicToolCallResult {
  return { success: false, errorCode, errorMessage }
}

function requestErrorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error)
}

function mapRequestErrorCode(error: unknown): string {
  if (error instanceof ThreadProjectError) return error.code
  const { rpcCode } = readAppServerErrorFields(error)
  if (rpcCode === -32602) return 'InvalidArguments'
  if (rpcCode === -32010) return 'ThreadNotFound'
  if (rpcCode === -32012) return 'ThreadBusy'
  const message = requestErrorMessage(error).toLowerCase()
  if (message.includes('not found')) return 'ThreadNotFound'
  if (message.includes('archived')) return 'ThreadArchived'
  if (isThreadBusyError(error)) return 'ThreadBusy'
  return 'AppServerRequestFailed'
}

function isThreadBusyError(error: unknown): boolean {
  const message = requestErrorMessage(error).toLowerCase()
  return message.includes('turninprogress')
    || message.includes('turn in progress')
    || message.includes('threadbusy')
    || message.includes('thread busy')
    || message.includes('already running')
    || message.includes('is running')
    || message.includes('maintenance')
}
