import { create } from 'zustand'

export type ModelCatalogStatus = 'idle' | 'loading' | 'ready' | 'error'
export type ReasoningEffortWire = 'low' | 'medium' | 'high' | 'extraHigh' | 'ultra'
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
  supportsDisable: boolean
  supportedEfforts: ModelReasoningEffortOption[]
  defaultEffort: ReasoningEffortWire
  supportedOutputs: ReasoningOutputWire[]
  defaultOutput: ReasoningOutputWire
}

/**
 * Server-authored context-window metadata for a model (see
 * specs/features/model-options.md §5). Clients use `supportsMax` to decide
 * whether to offer the MAX context switch; they must not hardcode model rules.
 */
export interface ModelContextWindowMeta {
  /** Raw catalog window after model-catalog resolution (may be a fallback default). */
  catalogWindow: number
  /** Default configured compaction window after normal cap rules. */
  configuredWindow: number
  /** True only when the catalog window is explicit and greater than the configured window. */
  supportsMax: boolean
  /** Window used by MAX mode when supported; otherwise equal to `configuredWindow`. */
  maxWindow: number
}

export interface ModelCatalogItem {
  id: string
  ownedBy?: string
  createdAt?: string
  reasoning?: ModelReasoningCapability | null
  speed?: ModelSpeedCapability | null
  contextWindow?: ModelContextWindowMeta | null
}

/** AppServer `model/list` error when the upstream endpoint does not support listing models. */
const MODEL_LIST_ERROR_ENDPOINT_NOT_SUPPORTED = 'EndpointNotSupported'

interface ModelCatalogState {
  status: ModelCatalogStatus
  models: ModelCatalogItem[]
  modelOptions: string[]
  /**
   * Effective provider id returned by AppServer. When model/list is requested without providerId,
   * this resolves to the workspace-selected provider.
   */
  providerId: string | null
  /** Provider id used for the last request; null means "workspace default". */
  requestedProviderId: string | null
  /** True when the server reports that the upstream API does not support model listing. */
  modelListUnsupportedEndpoint: boolean
  errorCode: string | null
  errorMessage: string | null
}

interface ModelCatalogActions {
  loadIfNeeded(force?: boolean, providerId?: string | null): Promise<void>
  reset(): void
}

type ModelCatalogStore = ModelCatalogState & ModelCatalogActions

let inFlightLoad: Promise<void> | null = null

const initialState: ModelCatalogState = {
  status: 'idle',
  models: [],
  modelOptions: [],
  providerId: null,
  requestedProviderId: null,
  modelListUnsupportedEndpoint: false,
  errorCode: null,
  errorMessage: null
}

const effortValues = new Set<ReasoningEffortWire>(['low', 'medium', 'high', 'extraHigh', 'ultra'])
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
    supportedEfforts,
    defaultEffort,
    supportedOutputs: supportedOutputs.length > 0 ? supportedOutputs : ['full'],
    defaultOutput: typeof typed.defaultOutput === 'string' && outputValues.has(typed.defaultOutput as ReasoningOutputWire)
      ? typed.defaultOutput as ReasoningOutputWire
      : 'full'
  }
}

function parseContextWindowMeta(value: unknown): ModelContextWindowMeta | null {
  if (!value || typeof value !== 'object') return null
  const typed = value as Record<string, unknown>
  const num = (camel: unknown, pascal: unknown): number => {
    const raw = typeof camel === 'number' ? camel : typeof pascal === 'number' ? pascal : NaN
    return Number.isFinite(raw) ? raw : 0
  }
  const supportsRaw = typed.supportsMax ?? typed.SupportsMax
  return {
    catalogWindow: num(typed.catalogWindow, typed.CatalogWindow),
    configuredWindow: num(typed.configuredWindow, typed.ConfiguredWindow),
    supportsMax: supportsRaw === true,
    maxWindow: num(typed.maxWindow, typed.MaxWindow)
  }
}

export function parseModelCatalogItems(payload: unknown): ModelCatalogItem[] {
  const typed = payload as {
    success?: boolean
    models?: Array<{ id?: string; Id?: string; ownedBy?: string; OwnedBy?: string; createdAt?: string; CreatedAt?: string; reasoning?: unknown; Reasoning?: unknown; speed?: unknown; Speed?: unknown; contextWindow?: unknown; ContextWindow?: unknown }>
  }
  if (!typed.success || !Array.isArray(typed.models)) return []
  const byId = new Map<string, ModelCatalogItem>()
  for (const model of typed.models) {
    const id = String(model.id ?? model.Id ?? '').trim()
    if (!id || byId.has(id)) continue
    byId.set(id, {
      id,
      ownedBy: typeof (model.ownedBy ?? model.OwnedBy) === 'string' ? String(model.ownedBy ?? model.OwnedBy) : undefined,
      createdAt: typeof (model.createdAt ?? model.CreatedAt) === 'string' ? String(model.createdAt ?? model.CreatedAt) : undefined,
      reasoning: parseReasoningCapability(model.reasoning ?? model.Reasoning),
      speed: parseSpeedCapability(model.speed ?? model.Speed),
      contextWindow: parseContextWindowMeta(model.contextWindow ?? model.ContextWindow)
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

export const useModelCatalogStore = create<ModelCatalogStore>((set, get) => ({
  ...initialState,

  async loadIfNeeded(force = false, providerId = null) {
    const normalizedProviderId = typeof providerId === 'string' && providerId.trim() !== '' ? providerId.trim() : null
    for (;;) {
      const current = get()
      const providerChanged = current.requestedProviderId !== normalizedProviderId
      if (!force && !providerChanged && current.status === 'ready') {
        return
      }
      if (!force && !providerChanged && current.status === 'loading' && inFlightLoad) {
        await inFlightLoad
        return
      }
      if (inFlightLoad) {
        await inFlightLoad
        force = true
        continue
      }
      break
    }

    set({ status: 'loading', requestedProviderId: normalizedProviderId })
    const load = (async () => {
      try {
        const result = await window.api.appServer.listModels(normalizedProviderId)
        if (result == null) {
          set({
            models: [],
            modelOptions: [],
            status: 'idle',
            providerId: normalizedProviderId,
            requestedProviderId: normalizedProviderId,
            modelListUnsupportedEndpoint: false,
            errorCode: null,
            errorMessage: null
          })
          return
        }
        const effectiveProviderId = parseEffectiveProviderId(result, normalizedProviderId)
        const error = parseModelListError(result)
        if (error.code || error.message) {
          set({
            models: [],
            modelOptions: [],
            status: 'error',
            providerId: effectiveProviderId,
            requestedProviderId: normalizedProviderId,
            modelListUnsupportedEndpoint: parseModelListUnsupportedEndpoint(result),
            errorCode: error.code,
            errorMessage: error.message
          })
          return
        }

        const models = parseModelCatalogItems(result)
        set({
          models,
          modelOptions: models.map((model) => model.id),
          status: 'ready',
          providerId: effectiveProviderId,
          requestedProviderId: normalizedProviderId,
          modelListUnsupportedEndpoint: false,
          errorCode: null,
          errorMessage: null
        })
      } catch (err) {
        set({
          modelOptions: [],
          models: [],
          status: 'error',
          providerId: normalizedProviderId,
          requestedProviderId: normalizedProviderId,
          modelListUnsupportedEndpoint: false,
          errorCode: null,
          errorMessage: err instanceof Error ? err.message : String(err)
        })
      }
    })()
    inFlightLoad = load

    try {
      await load
    } finally {
      if (inFlightLoad === load) inFlightLoad = null
    }
  },

  reset() {
    inFlightLoad = null
    set({ ...initialState })
  }
}))
