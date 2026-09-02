import type { ConversationItem, ToolPresentationDescriptor, ToolSourceProvenance } from '../types/conversation'
import type { ToolGroupCategory } from './toolCallAggregation'
import { isBuilderField } from '../components/agents/agentBuilderDraftSync'

export type ToolRendererFamily =
  | 'createPlan'
  | 'cron'
  | 'skillManage'
  | 'skillView'
  | 'subagent'
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

function hasOperation(...allowed: readonly string[]) {
  return (options: Readonly<Record<string, unknown>>): boolean => (
    Object.keys(options).length === 1
    && typeof options.operation === 'string'
    && allowed.includes(options.operation)
  )
}

export const CORE_TOOL_PRESENTATION_IDS = {
  createPlan: 'core.create-plan',
  cron: 'core.cron',
  skillManage: 'core.skill-manage',
  skillView: 'core.skill-view',
  subagent: 'core.subagent',
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
  agentBuilder: 'core.agent-builder'
} as const

export const coreToolRendererRegistry = new ToolRendererRegistry([
  registration(CORE_TOOL_PRESENTATION_IDS.createPlan, 'createPlan', {
    mode: 'standalone', placement: 'pin-last-per-turn'
  }),
  registration(CORE_TOOL_PRESENTATION_IDS.cron, 'cron', { mode: 'standalone' }),
  registration(CORE_TOOL_PRESENTATION_IDS.skillManage, 'skillManage', { mode: 'standalone' }),
  registration(CORE_TOOL_PRESENTATION_IDS.skillView, 'skillView', { mode: 'standalone' }),
  registration(CORE_TOOL_PRESENTATION_IDS.subagent, 'subagent', {
    mode: 'standalone', groupCategory: 'subagent'
  }, hasOperation('spawn', 'wait', 'sendMessage', 'followupTask', 'list', 'close', 'sendInput', 'resume')),
  registration(CORE_TOOL_PRESENTATION_IDS.shell, 'shell', {
    mode: 'collapsible', groupCategory: 'shell', successOverride: true
  }),
  registration(CORE_TOOL_PRESENTATION_IDS.fileWrite, 'fileWrite', {
    mode: 'collapsible', groupCategory: 'write'
  }, hasOperation('write', 'edit')),
  registration(CORE_TOOL_PRESENTATION_IDS.web, 'web', {
    mode: 'collapsible', groupCategory: 'web'
  }, hasOperation('search', 'fetch')),
  registration(CORE_TOOL_PRESENTATION_IDS.requestUserInput, 'requestUserInput', {
    mode: 'collapsible'
  }),
  registration(CORE_TOOL_PRESENTATION_IDS.readFile, 'readFile', {
    mode: 'collapsible', groupCategory: 'explore'
  }),
  registration(CORE_TOOL_PRESENTATION_IDS.lsp, 'lsp', {
    mode: 'collapsible', groupCategory: 'explore'
  }),
  registration(CORE_TOOL_PRESENTATION_IDS.commitSuggest, 'commitSuggest', {
    mode: 'collapsible'
  }),
  registration(CORE_TOOL_PRESENTATION_IDS.todo, 'todo', { mode: 'collapsible' }),
  registration(CORE_TOOL_PRESENTATION_IDS.deferredSearch, 'deferredSearch', {
    mode: 'collapsible'
  }),
  registration(CORE_TOOL_PRESENTATION_IDS.agentBuilder, 'agentBuilder', {
    mode: 'standalone'
  }, hasBuilderField)
])

export function resolveCoreToolRenderPlan(item: ConversationItem): ToolRendererPlan | null {
  return coreToolRendererRegistry.resolve(item)
}
