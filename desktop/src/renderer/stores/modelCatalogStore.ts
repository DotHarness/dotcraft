import { create } from 'zustand'
import { hasConfigKeyPathChange } from '../utils/configChanged'
import { requestWorkspaceResource, workspaceResourceKey, type WorkspaceResourceScope } from './workspaceResourceScope'

export type ModelCatalogStatus = 'idle' | 'loading' | 'ready' | 'error'
export type ReasoningEffortWire = 'low' | 'medium' | 'high' | 'extraHigh' | 'max' | 'ultra'
export type ReasoningOutputWire = 'none' | 'summary' | 'full'
export type InferenceSpeedWire = 'standard' | 'fast'

export interface ModelSpeedCapability {
  supportedModes: InferenceSpeedWire[]
  defaultMode: InferenceSpeedWire
}

export interface ModelReasoningEffortOption {
  effort: ReasoningEffortWire
  label: string
  description: string
}

export interface ModelReasoningCapability {
  defaultEnabled?: boolean
  supportsDisable: boolean
  supportedEfforts: ModelReasoningEffortOption[]
  defaultEffort: ReasoningEffortWire
  supportedOutputs: ReasoningOutputWire[]
  defaultOutput: ReasoningOutputWire
}

export interface ModelCatalogItem {
  supportedProtocols?: string[]
  defaultProtocol?: string
  maxOutputTokens?: number | null
  inputModalities?: string[]
  pricing?: { inputPerMillion?: number | null; outputPerMillion?: number | null }
  id: string
  displayName?: string
  ownedBy?: string
  createdAt?: string
  reasoning?: ModelReasoningCapability | null
  speed?: ModelSpeedCapability | null
  contextWindow?: number | null
}

/** AppServer `model/list` error when the upstream endpoint does not support listing models. */
const MODEL_LIST_ERROR_ENDPOINT_NOT_SUPPORTED = 'EndpointNotSupported'

export interface ModelCatalogState {
  source?: string
  refreshedAt?: string
  refreshNotice?: string
  status: ModelCatalogStatus
  models: ModelCatalogItem[]
  modelOptions: string[]
  /**
   * Effective provider id returned by AppServer. When model/list is requested without providerId,
   * this resolves to the workspace-selected provider.
   */
  providerId: string | null
  /** True when the server reports that the upstream API does not support model listing. */
  modelListUnsupportedEndpoint: boolean
  errorCode: string | null
  errorMessage: string | null
}

interface ModelCatalogActions {
  activate(scope: WorkspaceResourceScope | null): void
  prune(liveKeys: ReadonlySet<string>): void
  revalidate(providerId?: string | null): Promise<ModelCatalogState | null>
  loadIfNeeded(force?: boolean, providerId?: string | null): Promise<ModelCatalogState | null>
  getCatalog(providerId?: string | null): ModelCatalogState
  handleConfigChanged(regions: string[]): void
  reset(): void
}

interface ModelCatalogStore extends ModelCatalogActions {
  catalogs: ReadonlyMap<string | null, ModelCatalogState>
  revision: number
  scopeKey: string | null
  scope: WorkspaceResourceScope | null
}

interface WorkspaceCatalogs {
  scope: WorkspaceResourceScope
  catalogs: Map<string | null, ModelCatalogState>
  inFlight: Map<string | null, Promise<ModelCatalogState | null>>
}

const workspaces = new Map<string, WorkspaceCatalogs>()
let activeWorkspace: WorkspaceCatalogs | null = null

function catalogKey(providerId?: string | null): string | null {
  return providerId?.trim().toLowerCase() || null
}

const initialState: ModelCatalogState = {
  source: undefined,
  refreshedAt: undefined,
  refreshNotice: undefined,
  status: 'idle',
  models: [],
  modelOptions: [],
  providerId: null,
  modelListUnsupportedEndpoint: false,
  errorCode: null,
  errorMessage: null
}

const effortValues = new Set<ReasoningEffortWire>(['low', 'medium', 'high', 'extraHigh', 'max', 'ultra'])
const outputValues = new Set<ReasoningOutputWire>(['none', 'summary', 'full'])
const speedValues = new Set<InferenceSpeedWire>(['standard', 'fast'])

function parseSpeedCapability(value: unknown): ModelSpeedCapability | null {
  if (!value || typeof value !== 'object') return null
  const typed = value as Record<string, unknown>
  const rawModes = typed.supportedModes ?? typed.SupportedModes
  if (!Array.isArray(rawModes)) return null
  const supportedModes = rawModes.filter(
    (item): item is InferenceSpeedWire => typeof item === 'string' && speedValues.has(item as InferenceSpeedWire)
  )
  if (!supportedModes.includes('fast')) return null
  const rawDefault = typed.defaultMode ?? typed.DefaultMode
  return {
    supportedModes,
    defaultMode: typeof rawDefault === 'string' && speedValues.has(rawDefault as InferenceSpeedWire)
      ? rawDefault as InferenceSpeedWire
      : 'standard'
  }
}

function parseReasoningCapability(value: unknown): ModelReasoningCapability | null {
  if (!value || typeof value !== 'object') return null
  const typed = value as {
    supportsDisable?: unknown
    defaultEnabled?: unknown
    supportedEfforts?: unknown
    defaultEffort?: unknown
    supportedOutputs?: unknown
    defaultOutput?: unknown
  }
  if (!Array.isArray(typed.supportedEfforts)) return null
  const supportedEfforts = typed.supportedEfforts
    .map((item): ModelReasoningEffortOption | null => {
      if (!item || typeof item !== 'object') return null
      const option = item as { effort?: unknown; label?: unknown; description?: unknown }
      const effort = typeof option.effort === 'string' && effortValues.has(option.effort as ReasoningEffortWire)
        ? option.effort as ReasoningEffortWire
        : null
      if (!effort) return null
      return {
        effort,
        label: typeof option.label === 'string' && option.label.trim() !== '' ? option.label : effort,
        description: typeof option.description === 'string' ? option.description : ''
      }
    })
    .filter((item): item is ModelReasoningEffortOption => item != null)
  if (supportedEfforts.length === 0) return null

  const defaultEffort = typeof typed.defaultEffort === 'string' && effortValues.has(typed.defaultEffort as ReasoningEffortWire)
    ? typed.defaultEffort as ReasoningEffortWire
    : supportedEfforts[0].effort
  const supportedOutputs = Array.isArray(typed.supportedOutputs)
    ? typed.supportedOutputs.filter((item): item is ReasoningOutputWire => typeof item === 'string' && outputValues.has(item as ReasoningOutputWire))
    : []
  return {
    supportsDisable: typed.supportsDisable !== false,
    ...(typed.defaultEnabled === true ? { defaultEnabled: true } : {}),
    supportedEfforts,
    defaultEffort,
    supportedOutputs: supportedOutputs.length > 0 ? supportedOutputs : ['full'],
    defaultOutput: typeof typed.defaultOutput === 'string' && outputValues.has(typed.defaultOutput as ReasoningOutputWire)
      ? typed.defaultOutput as ReasoningOutputWire
      : 'full'
  }
}

function parseContextWindow(value: unknown): number | null {
  return typeof value === 'number' && Number.isFinite(value) && value > 0 ? value : null
}

export function parseModelCatalogItems(payload: unknown): ModelCatalogItem[] {
  const typed = payload as {
    success?: boolean
    refreshNotice?: string
    models?: Array<Partial<ModelCatalogItem> & { Id?: string; OwnedBy?: string; CreatedAt?: string; Reasoning?: unknown; Speed?: unknown; ContextWindow?: unknown }>
  }
  if ((!typed.success && typed.refreshNotice !== 'usingCachedCatalog') || !Array.isArray(typed.models)) return []
  const byId = new Map<string, ModelCatalogItem>()
  for (const model of typed.models) {
    const id = String(model.id ?? model.Id ?? '').trim()
    if (!id || byId.has(id)) continue
    const displayName = typeof model.displayName === 'string' ? model.displayName.trim() : ''
    byId.set(id, {
      id,
      ...(Array.isArray(model.supportedProtocols) ? { supportedProtocols: model.supportedProtocols.filter((value) => typeof value === 'string') } : {}),
      ...(typeof model.defaultProtocol === 'string' ? { defaultProtocol: model.defaultProtocol } : {}),
      ...(model.maxOutputTokens != null ? { maxOutputTokens: parseContextWindow(model.maxOutputTokens) } : {}),
      ...(Array.isArray(model.inputModalities) ? { inputModalities: model.inputModalities.filter((value) => typeof value === 'string') } : {}),
      ...(model.pricing && typeof model.pricing === 'object' ? { pricing: model.pricing } : {}),
      ...(displayName ? { displayName } : {}),
      ownedBy: typeof (model.ownedBy ?? model.OwnedBy) === 'string' ? String(model.ownedBy ?? model.OwnedBy) : undefined,
      createdAt: typeof (model.createdAt ?? model.CreatedAt) === 'string' ? String(model.createdAt ?? model.CreatedAt) : undefined,
      reasoning: parseReasoningCapability(model.reasoning ?? model.Reasoning),
      speed: parseSpeedCapability(model.speed ?? model.Speed),
      contextWindow: parseContextWindow(model.contextWindow ?? model.ContextWindow)
    })
  }
  return Array.from(byId.values()).sort((a, b) => a.id.localeCompare(b.id))
}

function parseEffectiveProviderId(payload: unknown, requestedProviderId: string | null): string | null {
  if (!payload || typeof payload !== 'object') return requestedProviderId
  const typed = payload as {
    providerId?: unknown
    ProviderId?: unknown
  }
  const value = typed.providerId ?? typed.ProviderId
  return typeof value === 'string' && value.trim() !== '' ? value.trim() : requestedProviderId
}

function parseModelListUnsupportedEndpoint(payload: unknown): boolean {
  const typed = payload as {
    success?: boolean
    errorCode?: string
    ErrorCode?: string
  }
  const errorCode = typed.errorCode ?? typed.ErrorCode
  return typed.success === false && errorCode === MODEL_LIST_ERROR_ENDPOINT_NOT_SUPPORTED
}

function parseModelListError(payload: unknown): { code: string | null; message: string | null } {
  const typed = payload as {
    success?: boolean
    errorCode?: string
    ErrorCode?: string
    errorMessage?: string
    ErrorMessage?: string
  }
  if (typed.success !== false) return { code: null, message: null }
  return {
    code: typed.errorCode ?? typed.ErrorCode ?? null,
    message: typed.errorMessage ?? typed.ErrorMessage ?? null
  }
}

async function fetchCatalog(scope: WorkspaceResourceScope, providerId: string | null, refresh: boolean): Promise<ModelCatalogState> {
  const state = { ...initialState, providerId }
  try {
    const result = await requestWorkspaceResource(scope, 'model/list', {
      ...(providerId ? { providerId } : {}), ...(refresh ? { refresh: true } : {})
    }, 20_000)
    state.providerId = parseEffectiveProviderId(result, providerId)
    const metadata = result as { source?: string; refreshedAt?: string; refreshNotice?: string }
    state.source = metadata.source
    state.refreshedAt = metadata.refreshedAt
    state.refreshNotice = metadata.refreshNotice
    const models = parseModelCatalogItems(result)
    const error = parseModelListError(result)
    if (error.code || error.message) {
      return { ...state, models, modelOptions: models.map(model => model.id), status: 'error', modelListUnsupportedEndpoint: parseModelListUnsupportedEndpoint(result),
        errorCode: error.code, errorMessage: error.message }
    }
    return { ...state, status: 'ready', models, modelOptions: models.map(model => model.id) }
  } catch (error) {
    return { ...state, status: 'error', errorMessage: error instanceof Error ? error.message : String(error) }
  }
}

export const useModelCatalogStore = create<ModelCatalogStore>((set, get) => {
  const publish = (entry: WorkspaceCatalogs): void => {
    if (entry === activeWorkspace) set({ catalogs: entry.catalogs })
  }
  const loadCatalog = (providerId: string | null, revalidate: boolean, force: boolean): Promise<ModelCatalogState | null> => {
    const entry = activeWorkspace
    if (!entry) return Promise.resolve(null)
    const key = catalogKey(providerId)
    const pending = entry.inFlight.get(key)
    if (pending) return pending
    const cached = entry.catalogs.get(key)
    if (!revalidate && !force && cached?.status === 'ready') return Promise.resolve(cached)
    if (cached?.status !== 'ready') {
      entry.catalogs = new Map(entry.catalogs).set(key, { ...initialState, status: 'loading', providerId: key })
      publish(entry)
    }
    const load = fetchCatalog(entry.scope, key, force).then((result) => {
      if (workspaces.get(workspaceResourceKey(entry.scope)) !== entry || entry.inFlight.get(key) !== load) return null
      const snapshot = result.status !== 'ready' && cached?.status === 'ready'
        ? { ...cached, errorCode: result.errorCode, errorMessage: result.errorMessage }
        : result
      entry.catalogs = new Map(entry.catalogs).set(key, snapshot)
      const effectiveKey = catalogKey(snapshot.providerId)
      for (const [aliasKey, alias] of entry.catalogs) {
        if (cached && alias === cached && catalogKey(alias.providerId) === effectiveKey && !entry.inFlight.has(aliasKey)) {
          entry.catalogs.set(aliasKey, snapshot)
        }
      }
      if (key === null && effectiveKey && snapshot.status === 'ready' && !entry.catalogs.has(effectiveKey)) {
        entry.catalogs.set(effectiveKey, snapshot)
      }
      publish(entry)
      return snapshot
    }).finally(() => {
      if (entry.inFlight.get(key) === load) entry.inFlight.delete(key)
    })
    entry.inFlight.set(key, load)
    return load
  }
  return {
    catalogs: new Map(),
    revision: 0,
    scopeKey: null,
    scope: null,

    activate(scope) {
      const scopeKey = scope ? workspaceResourceKey(scope) : null
      if (scopeKey === get().scopeKey) return
      if (scope) {
        for (const [key, entry] of workspaces) {
          if (entry.scope.connectionKey === scope.connectionKey && entry.scope.connectionId !== scope.connectionId) workspaces.delete(key)
        }
        let entry = workspaces.get(scopeKey!)
        if (!entry) {
          entry = { scope, catalogs: new Map(), inFlight: new Map() }
          workspaces.set(scopeKey!, entry)
        }
        activeWorkspace = entry
      } else {
        activeWorkspace = null
      }
      set((state) => ({ scopeKey, scope, catalogs: activeWorkspace?.catalogs ?? new Map(), revision: state.revision + 1 }))
    },

    prune(liveKeys) {
      for (const [key, entry] of workspaces) {
        if (entry !== activeWorkspace && !liveKeys.has(key)) workspaces.delete(key)
      }
    },

    getCatalog(providerId = null) {
      return selectModelCatalog(get(), providerId)
    },

    loadIfNeeded(force = false, providerId = null) {
      return loadCatalog(providerId, false, force)
    },

    revalidate(providerId = null) {
      return loadCatalog(providerId, true, false)
    },

    handleConfigChanged(regions) {
      const entry = activeWorkspace
      if (!entry) return
      if (regions.includes('providers')) {
        entry.inFlight.clear()
        const keys = [...entry.catalogs.keys()]
        set((state) => ({ revision: state.revision + 1 }))
        for (const key of keys) void loadCatalog(key, true, false)
      } else if (hasConfigKeyPathChange(regions, 'ProviderId')) {
        entry.inFlight.delete(null)
        entry.catalogs = new Map(entry.catalogs)
        entry.catalogs.delete(null)
        set((state) => ({ catalogs: entry.catalogs, revision: state.revision + 1 }))
      }
    },

    reset() {
      workspaces.clear()
      activeWorkspace = null
      set((state) => ({ scopeKey: null, scope: null, catalogs: new Map(), revision: state.revision + 1 }))
    }
  }
})

export function selectModelCatalog(
  state: Pick<ModelCatalogStore, 'catalogs'>,
  providerId?: string | null
): ModelCatalogState {
  return state.catalogs.get(catalogKey(providerId)) ?? initialState
}
