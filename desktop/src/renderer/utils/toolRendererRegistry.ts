import type { ConversationItem, ToolPresentationDescriptor, ToolSourceProvenance } from '../types/conversation'
import type { ToolGroupCategory } from './toolCallAggregation'
import { isBuilderField } from '../components/agents/agentBuilderDraftSync'
import { BOARD_OPERATIONS } from './boardToolDisplay'

export type ToolRendererFamily =
  | 'createPlan'
  | 'automation'
  | 'skillManage'
  | 'skillView'
  | 'subagent'
  | 'task'
  | 'board'
  | 'shell'
  | 'fileWrite'
  | 'web'
  | 'requestUserInput'
  | 'readFile'
  | 'lsp'
  | 'commitSuggest'
  | 'todo'
  | 'deferredSearch'
  | 'agentBuilder'
  | 'remoteToolHost'
  | 'nodeRepl'

export interface ToolRendererPlan {
  family: ToolRendererFamily
  mode: 'standalone' | 'collapsible'
  groupCategory?: ToolGroupCategory
  placement?: 'flow' | 'pin-last-per-turn'
  successOverride?: boolean
  options: Readonly<Record<string, unknown>>
}

export interface ToolRendererContext {
  item: ConversationItem
  presentation: ToolPresentationDescriptor
  provenance: ToolSourceProvenance
}

export interface ToolRendererRegistration {
  presentationId: string
  resolve(context: ToolRendererContext): ToolRendererPlan | null
}

const MAX_OPTIONS_BYTES = 4096

export class ToolRendererRegistry {
  private readonly registrations = new Map<string, ToolRendererRegistration>()

  constructor(registrations: readonly ToolRendererRegistration[]) {
    for (const registration of registrations) {
      if (this.registrations.has(registration.presentationId)) {
        throw new Error(`Duplicate tool renderer presentation id: ${registration.presentationId}`)
      }
      this.registrations.set(registration.presentationId, registration)
    }
  }

  resolve(item: ConversationItem): ToolRendererPlan | null {
    const presentation = item.presentation
    const provenance = item.source
    if (!presentation || !isTrustedCoreProvenance(provenance)) return null
    if (!isValidPresentation(presentation)) return null

    const registration = this.registrations.get(presentation.presentationId)
    if (!registration) return null
    return registration.resolve({ item, presentation, provenance })
  }
}

function isTrustedCoreProvenance(
  provenance: ToolSourceProvenance | undefined
): provenance is ToolSourceProvenance {
  return provenance?.kind === 'CoreNative'
}

function isValidPresentation(presentation: ToolPresentationDescriptor): boolean {
  if (!presentation.presentationId || presentation.presentationId.trim() !== presentation.presentationId) {
    return false
  }
  const options = presentation.options
  if (options == null) return true
  if (Object.getPrototypeOf(options) !== Object.prototype) return false
  try {
    return new TextEncoder().encode(JSON.stringify(options)).byteLength <= MAX_OPTIONS_BYTES
  } catch {
    return false
  }
}

function registration(
  presentationId: string,
  family: ToolRendererFamily,
  plan: Omit<ToolRendererPlan, 'family' | 'options'>,
  validateOptions: (options: Readonly<Record<string, unknown>>) => boolean = hasNoOptions
): ToolRendererRegistration {
  return {
    presentationId,
    resolve: ({ presentation }) => {
      const options = Object.freeze({ ...(presentation.options ?? {}) })
      return validateOptions(options)
        ? { family, ...plan, options }
        : null
    }
  }
}

function hasNoOptions(options: Readonly<Record<string, unknown>>): boolean {
  return Object.keys(options).length === 0
}

function hasBuilderField(options: Readonly<Record<string, unknown>>): boolean {
  return Object.keys(options).length === 1
    && typeof options.field === 'string'
    && isBuilderField(options.field)
}

function hasSurface(options: Readonly<Record<string, unknown>>): boolean {
  return Object.keys(options).length === 1
    && (options.surface === 'browser' || options.surface === 'chrome' || options.surface === 'computer')
}

function hasOperation(...allowed: readonly string[]) {
  return (options: Readonly<Record<string, unknown>>): boolean => (
    Object.keys(options).length === 1
    && typeof options.operation === 'string'
    && allowed.includes(options.operation)
  )
}

function isTaskStop(options: Readonly<Record<string, unknown>>): boolean {
  const { kind, ...rest } = options
  return hasOperation('stop')(rest) && (kind === undefined || kind === 'shell' || kind === 'agent' || kind === 'workflow')
}

/** Items persisted before these families named an operation carry no options. */
function hasOptionalOperation(...allowed: readonly string[]) {
  const named = hasOperation(...allowed)
  return (options: Readonly<Record<string, unknown>>): boolean => hasNoOptions(options) || named(options)
}

function allowsMemoryMark(
  mark: 'recall' | 'update',
  validate: (options: Readonly<Record<string, unknown>>) => boolean
) {
  return (options: Readonly<Record<string, unknown>>): boolean => {
    if (!Object.hasOwn(options, 'memory')) return validate(options)
    const { memory, ...rest } = options
    return memory === mark && validate(rest)
  }
}

export const CORE_TOOL_PRESENTATION_IDS = {
  createPlan: 'core.create-plan',
  automation: 'core.automation',
  skillManage: 'core.skill-manage',
  skillView: 'core.skill-view',
  subagent: 'core.subagent',
  task: 'core.task',
  board: 'core.board',
  shell: 'core.shell',
  fileWrite: 'core.file-write',
  web: 'core.web',
  requestUserInput: 'core.request-user-input',
  sendUserMessageAsync: 'core.send-user-message-async',
  readFile: 'core.read-file',
  lsp: 'core.lsp',
  commitSuggest: 'core.commit-suggest',
  todo: 'core.todo',
  deferredSearch: 'core.deferred-search',
  agentBuilder: 'core.agent-builder',
  remoteToolHost: 'core.remote-tool-host',
  nodeRepl: 'core.node-repl'
} as const

export const coreToolRendererRegistry = new ToolRendererRegistry([
  registration(CORE_TOOL_PRESENTATION_IDS.createPlan, 'createPlan', {
    mode: 'standalone', placement: 'pin-last-per-turn'
  }),
  registration(CORE_TOOL_PRESENTATION_IDS.automation, 'automation', { mode: 'standalone' }),
  registration(CORE_TOOL_PRESENTATION_IDS.skillManage, 'skillManage', { mode: 'standalone' }),
  registration(CORE_TOOL_PRESENTATION_IDS.skillView, 'skillView', { mode: 'standalone' }),
  registration(CORE_TOOL_PRESENTATION_IDS.subagent, 'subagent', {
    mode: 'standalone', groupCategory: 'subagent'
  }, hasOperation('spawn', 'sendMessage', 'list')),
  registration(CORE_TOOL_PRESENTATION_IDS.task, 'task', { mode: 'standalone' }, isTaskStop),
  registration(CORE_TOOL_PRESENTATION_IDS.board, 'board', {
    mode: 'standalone', groupCategory: 'board'
  }, hasOperation(...BOARD_OPERATIONS)),
  registration(CORE_TOOL_PRESENTATION_IDS.shell, 'shell', {
    mode: 'collapsible', groupCategory: 'shell', successOverride: true
  }, hasOptionalOperation('exec', 'writeStdin')),
  registration(CORE_TOOL_PRESENTATION_IDS.fileWrite, 'fileWrite', {
    mode: 'collapsible', groupCategory: 'write'
  }, allowsMemoryMark('update', hasOperation('write', 'edit'))),
  registration(CORE_TOOL_PRESENTATION_IDS.web, 'web', {
    mode: 'collapsible', groupCategory: 'web'
  }, hasOperation('search', 'fetch')),
  registration(CORE_TOOL_PRESENTATION_IDS.requestUserInput, 'requestUserInput', {
    mode: 'collapsible'
  }),
  registration(CORE_TOOL_PRESENTATION_IDS.readFile, 'readFile', {
    mode: 'collapsible', groupCategory: 'explore'
  }, allowsMemoryMark('recall', hasOptionalOperation('read', 'grep', 'find'))),
  registration(CORE_TOOL_PRESENTATION_IDS.lsp, 'lsp', {
    mode: 'collapsible', groupCategory: 'explore'
  }),
  registration(CORE_TOOL_PRESENTATION_IDS.commitSuggest, 'commitSuggest', {
    mode: 'collapsible'
  }),
  registration(CORE_TOOL_PRESENTATION_IDS.todo, 'todo', { mode: 'collapsible' }, hasOptionalOperation('todoWrite', 'updateTodos')),
  registration(CORE_TOOL_PRESENTATION_IDS.deferredSearch, 'deferredSearch', {
    mode: 'collapsible'
  }),
  registration(CORE_TOOL_PRESENTATION_IDS.agentBuilder, 'agentBuilder', {
    mode: 'standalone'
  }, hasBuilderField),
  registration(CORE_TOOL_PRESENTATION_IDS.nodeRepl, 'nodeRepl', { mode: 'collapsible' }, hasSurface),
  registration(CORE_TOOL_PRESENTATION_IDS.remoteToolHost, 'remoteToolHost', {
    mode: 'collapsible'
  }, hasOperation('list', 'connect', 'disconnect', 'transfer'))
])

export function resolveCoreToolRenderPlan(item: ConversationItem): ToolRendererPlan | null {
  return coreToolRendererRegistry.resolve(item)
}
