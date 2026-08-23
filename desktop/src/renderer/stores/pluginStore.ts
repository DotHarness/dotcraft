import { create } from 'zustand'
import type { LocalizedTextMap } from '../../shared/locales'
import {
  normalizeMarketplace,
  normalizePlugin,
  operationResultPatch,
  requireRevision,
  validRevision,
  type PluginOperationResult
} from './pluginSnapshot'

export type {
  PluginOperationOutcome,
  PluginOperationResult,
  PluginRuntimeProjection
} from './pluginSnapshot'
export { operationFailureMessage } from './pluginSnapshot'

export interface PluginInterface {
  displayName?: string | null
  shortDescription?: string | null
  longDescription?: string | null
  developerName?: string | null
  category?: string | null
  capabilities?: string[]
  defaultPrompt?: string | null
  brandColor?: string | null
  composerIconDataUrl?: string | null
  logoDataUrl?: string | null
  websiteUrl?: string | null
  privacyPolicyUrl?: string | null
  termsOfServiceUrl?: string | null
}

export interface PluginFunctionInfo {
  name: string
  namespace?: string | null
  description: string
}

export interface PluginDotnetInfo {
  entryAssembly: string
  entryType: string
  exportedApiAssemblies: string[]
  minHostVersion: string
}

/** Only `active` satisfies a declared dependency. */
export type PluginDependencyAvailability =
  | 'missing'
  | 'versionUnsatisfied'
  | 'disabled'
  | 'unavailable'
  | 'blocked'
  | 'activating'
  | 'active'
  | 'deactivating'
  | 'faulted'
  | 'reclaiming'

export interface PluginDependencyInfo {
  id: string
  requiredVersion: string
  observedVersion?: string | null
  availability: PluginDependencyAvailability
}

export interface PluginRuntimeBlocker {
  code: string
  message: string
  parameters?: Record<string, unknown>
}

/** `reclaiming` is not a fault: the plugin is already stopped, only its memory is still held. */
export type PluginDotnetRuntimeState =
  | 'stopped'
  | 'blocked'
  | 'activating'
  | 'active'
  | 'deactivating'
  | 'faulted'
  | 'reclaiming'

/** Fingerprint-bound trust of the accepted bundle. Any byte change returns it to `modified`. */
export type PluginTrustStatus = 'untrusted' | 'trusted' | 'modified'

export interface PluginDotnetRuntimeInfo {
  state: PluginDotnetRuntimeState
  generationId?: string | null
  blockers: PluginRuntimeBlocker[]
  leakedGenerations: number
  restartRecommended: boolean
  trustStatus: PluginTrustStatus
}

export interface PluginSkillInfo {
  name: string
  description: string
  displayName?: string | null
  shortDescription?: string | null
  enabled: boolean
}

export interface PluginAppNativeApplication {
  displayName: string
  protocol: string
  installUrl?: string | null
}

export interface PluginAppInfo {
  appId: string
  displayName: string
  developerName: string
  description: string
  category?: string | null
  icon?: string | null
  releasePage?: string | null
  nativeApplication?: PluginAppNativeApplication | null
}

export interface PluginDesktopExtensionSurface {
  type: string
  viewId?: string | null
  label?: string | null
  localizedLabel?: LocalizedTextMap | null
  icon?: string | null
  placement?: string | null
  order?: number | null
  title?: string | null
  description?: string | null
  slot?: string | null
  rendererId?: string | null
  actionId?: string | null
  settingsId?: string | null
}

export interface PluginDesktopExtensionInfo {
  id: string
  displayName: string
  description?: string | null
  entry: string
  styles: string[]
  surfaces: PluginDesktopExtensionSurface[]
  requiredAppIds: string[]
  connectOrigins: string[]
  surfaceWriteScopes?: string[]
}

export interface PluginMcpServerInfo {
  name: string
  runtimeName: string
  transport: 'stdio' | 'streamableHttp'
  enabled: boolean
  active: boolean
  shadowedBy?: 'workspace' | 'plugin' | null
}

export interface PluginLspServerInfo {
  name: string
  runtimeName: string
  transport: 'stdio' | 'socket'
  enabled: boolean
  active: boolean
  extensions: string[]
  shadowedBy?: 'workspace' | 'plugin' | null
}

export interface PluginHookInfo {
  key: string
  eventName: string
}

export type MarketplaceSourceType = 'git' | 'local' | 'archive'

export interface MarketplaceEntry {
  name: string
  displayName?: string | null
  sourceType: MarketplaceSourceType
  source: string
  ref?: string | null
  sparsePaths: string[]
  root?: string | null
  lastUpdated?: string | null
  revision?: string | null
  removable: boolean
  pluginIds: string[]
}

export interface MarketplaceFailure {
  name: string
  code: string
  message: string
}

export interface PluginEntry {
  id: string
  displayName: string
  description?: string | null
  version?: string | null
  enabled: boolean
  installed: boolean
  installable: boolean
  removable: boolean
  source: string
  rootPath: string
  marketplaceName?: string | null
  interface?: PluginInterface | null
  dotnet?: PluginDotnetInfo | null
  dependencies?: PluginDependencyInfo[]
  dotnetRuntime?: PluginDotnetRuntimeInfo | null
  functions: PluginFunctionInfo[]
  skills: PluginSkillInfo[]
  apps?: PluginAppInfo[]
  desktopExtensions?: PluginDesktopExtensionInfo[]
  hooks?: PluginHookInfo[]
  mcpServers: PluginMcpServerInfo[]
  lspServers: PluginLspServerInfo[]
  diagnostics?: PluginDiagnosticEntry[]
}

export interface PluginDiagnosticEntry {
  severity: string
  code: string
  message: string
  pluginId?: string | null
  path?: string | null
  parameters?: Record<string, unknown>
}

export interface MarketplaceAddInput {
  source: string
  ref?: string
  sparsePaths?: string[]
}

export interface MarketplaceAddOutcome {
  marketplace: MarketplaceEntry
  alreadyAdded: boolean
}

interface PluginState {
  plugins: PluginEntry[]
  marketplaces: MarketplaceEntry[]
  diagnostics: PluginDiagnosticEntry[]
  loading: boolean
  error: string | null
  selectedPluginId: string | null
  selectedPlugin: PluginEntry | null
  detailLoading: boolean
  snapshotRevision: number
  /** Revision of the last unfiltered list, the only complete-workspace baseline. */
  completeSnapshotRevision: number

  fetchPlugins(): Promise<void>
  selectPlugin(id: string): Promise<void>
  clearSelection(): void
  installPlugin(id: string): Promise<PluginOperationResult>
  installLocalPlugin(path: string): Promise<PluginEntry | undefined>
  removePlugin(id: string): Promise<PluginOperationResult>
  togglePluginEnabled(id: string, enabled: boolean): Promise<PluginOperationResult>
  setPluginTrusted(id: string, trusted: boolean): Promise<PluginOperationResult>
  addMarketplace(input: MarketplaceAddInput): Promise<MarketplaceAddOutcome>
  removeMarketplace(name: string): Promise<void>
  refreshMarketplace(name?: string): Promise<MarketplaceFailure[]>
  handleSnapshotUpdated(snapshotRevision: unknown): void
}

// A list started before a mutation can land after it, and the revision alone cannot tell that
// apart from a legitimately unchanged one, so only the newest request may win.
let pluginListRequestToken = 0

export const usePluginStore = create<PluginState>((set, get) => ({
  plugins: [],
  marketplaces: [],
  diagnostics: [],
  loading: false,
  error: null,
  selectedPluginId: null,
  selectedPlugin: null,
  detailLoading: false,
  snapshotRevision: 0,
  completeSnapshotRevision: 0,

  async fetchPlugins() {
    const requestToken = ++pluginListRequestToken
    set({ loading: true, error: null })
    try {
      const result = (await window.api.appServer.sendRequest('plugin/list', {
        includeDisabled: true
      })) as {
        plugins?: PluginEntry[]
        marketplaces?: MarketplaceEntry[]
        diagnostics?: PluginDiagnosticEntry[]
        snapshotRevision: number
      }
      if (requestToken !== pluginListRequestToken) return
      const snapshotRevision = requireRevision(result.snapshotRevision)
      if (snapshotRevision < get().snapshotRevision) {
        set({ loading: false })
        return
      }
      const plugins = (result.plugins ?? []).map(normalizePlugin)
      const diagnostics = result.diagnostics ?? []
      set((state) => ({
        plugins,
        marketplaces: (result.marketplaces ?? []).map(normalizeMarketplace),
        diagnostics,
        selectedPlugin: state.selectedPluginId
          ? plugins.find((plugin) => plugin.id === state.selectedPluginId) ?? null
          : state.selectedPlugin,
        selectedPluginId: state.selectedPluginId && !plugins.some((plugin) => plugin.id === state.selectedPluginId)
          ? null
          : state.selectedPluginId,
        snapshotRevision,
        completeSnapshotRevision: snapshotRevision,
        loading: false
      }))
    } catch (e: unknown) {
      if (requestToken !== pluginListRequestToken) return
      const msg = e instanceof Error ? e.message : String(e)
      set({ error: msg, loading: false })
    }
  },

  async selectPlugin(id: string) {
    set({ selectedPluginId: id, selectedPlugin: null, detailLoading: true })
    try {
      const result = (await window.api.appServer.sendRequest('plugin/view', { id })) as {
        plugin?: PluginEntry
        snapshotRevision: number
      }
      if (get().selectedPluginId !== id) return
      const snapshotRevision = requireRevision(result.snapshotRevision)
      if (snapshotRevision < get().snapshotRevision) {
        set({ detailLoading: false })
        return
      }
      const plugin = result.plugin ? normalizePlugin(result.plugin) : null
      set({ selectedPlugin: plugin, snapshotRevision, detailLoading: false })
    } catch (e: unknown) {
      if (get().selectedPluginId !== id) return
      const msg = e instanceof Error ? e.message : String(e)
      set({ error: msg, detailLoading: false })
    }
  },

  clearSelection() {
    set({ selectedPluginId: null, selectedPlugin: null, detailLoading: false })
  },

  // Every mutation returns the Host's final projection, even when rejected, so records are
  // replaced with server truth. A missing plugin record means it is gone; only a full list settles that.
  async installPlugin(id: string) {
    try {
      const result = (await window.api.appServer.sendRequest('plugin/install', { id })) as PluginOperationResult
      set((state) => operationResultPatch(state, result))
      if (!result.plugin) await get().fetchPlugins()
      return result
    } catch (e: unknown) {
      console.error('plugin/install failed:', e)
      throw e
    }
  },

  async installLocalPlugin(path: string) {
    try {
      const result = (await window.api.appServer.sendRequest('plugin/installLocal', { path })) as PluginOperationResult
      const updated = result.plugin ? normalizePlugin(result.plugin) : undefined
      set((state) => operationResultPatch(state, result))
      if (!updated) await get().fetchPlugins()
      return updated
    } catch (e: unknown) {
      console.error('plugin/installLocal failed:', e)
      throw e
    }
  },

  async removePlugin(id: string) {
    try {
      const result = (await window.api.appServer.sendRequest('plugin/remove', { id })) as PluginOperationResult
      set((state) => operationResultPatch(state, result))
      if (!result.plugin) {
        await get().fetchPlugins()
        if (get().selectedPluginId === id) {
          set({ selectedPluginId: null, selectedPlugin: null, detailLoading: false })
        }
      }
      return result
    } catch (e: unknown) {
      console.error('plugin/remove failed:', e)
      throw e
    }
  },

  async togglePluginEnabled(id: string, enabled: boolean) {
    try {
      const result = (await window.api.appServer.sendRequest('plugin/setEnabled', {
        id,
        enabled
      })) as PluginOperationResult
      set((state) => operationResultPatch(state, result))
      if (!result.plugin) await get().fetchPlugins()
      return result
    } catch (e: unknown) {
      console.error('plugin/setEnabled failed:', e)
      throw e
    }
  },

  // The Host binds a granted trust intent to the bundle bytes it accepted.
  async setPluginTrusted(id: string, trusted: boolean) {
    try {
      const result = (await window.api.appServer.sendRequest('plugin/setTrusted', {
        id,
        trusted
      })) as PluginOperationResult
      set((state) => operationResultPatch(state, result))
      if (!result.plugin) await get().fetchPlugins()
      return result
    } catch (e: unknown) {
      console.error('plugin/setTrusted failed:', e)
      throw e
    }
  },


  // Marketplace mutations change which plugins are installable, so each one re-reads the
  // catalog to keep the browse surface and its marketplace grouping in step.
  async addMarketplace(input: MarketplaceAddInput) {
    const result = (await window.api.appServer.sendRequest('marketplace/add', {
      source: input.source,
      ...(input.ref ? { ref: input.ref } : {}),
      ...(input.sparsePaths && input.sparsePaths.length > 0 ? { sparsePaths: input.sparsePaths } : {})
    })) as { marketplace?: MarketplaceEntry; alreadyAdded?: boolean }
    await get().fetchPlugins()
    return {
      marketplace: normalizeMarketplace(result.marketplace ?? ({} as MarketplaceEntry)),
      alreadyAdded: result.alreadyAdded === true
    }
  },

  async removeMarketplace(name: string) {
    await window.api.appServer.sendRequest('marketplace/remove', { name })
    await get().fetchPlugins()
  },

  async refreshMarketplace(name?: string) {
    const result = (await window.api.appServer.sendRequest(
      'marketplace/refresh',
      name ? { name } : {}
    )) as { errors?: MarketplaceFailure[] }
    await get().fetchPlugins()
    return result.errors ?? []
  },

  // An invalidation, not a state snapshot: only a complete list can prove a held record is gone.
  handleSnapshotUpdated(snapshotRevision) {
    const revision = validRevision(snapshotRevision)
    if (revision == null || revision <= get().completeSnapshotRevision) return
    if (revision > get().snapshotRevision) set({ snapshotRevision: revision })
    void get().fetchPlugins()
  }
}))
