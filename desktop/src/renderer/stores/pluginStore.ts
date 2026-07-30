import { create } from 'zustand'
import type { LocalizedTextMap } from '../../shared/locales'

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
  functions: PluginFunctionInfo[]
  skills: PluginSkillInfo[]
  apps?: PluginAppInfo[]
  desktopExtensions?: PluginDesktopExtensionInfo[]
  hooks?: PluginHookInfo[]
  mcpServers: PluginMcpServerInfo[]
  lspServers: PluginLspServerInfo[]
  diagnostics?: Array<{ severity: string; code: string; message: string; pluginId?: string; path?: string }>
}

export interface PluginDiagnosticEntry {
  severity: string
  code: string
  message: string
  pluginId?: string | null
  path?: string | null
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

  fetchPlugins(): Promise<void>
  selectPlugin(id: string): Promise<void>
  clearSelection(): void
  installPlugin(id: string): Promise<void>
  installLocalPlugin(path: string): Promise<PluginEntry | undefined>
  removePlugin(id: string): Promise<void>
  togglePluginEnabled(id: string, enabled: boolean): Promise<void>
  addMarketplace(input: MarketplaceAddInput): Promise<MarketplaceAddOutcome>
  removeMarketplace(name: string): Promise<void>
  refreshMarketplace(name?: string): Promise<MarketplaceFailure[]>
}

export const usePluginStore = create<PluginState>((set, get) => ({
  plugins: [],
  marketplaces: [],
  diagnostics: [],
  loading: false,
  error: null,
  selectedPluginId: null,
  selectedPlugin: null,
  detailLoading: false,

  async fetchPlugins() {
    set({ loading: true, error: null })
    try {
      const result = (await window.api.appServer.sendRequest('plugin/list', {
        includeDisabled: true
      })) as {
        plugins?: PluginEntry[]
        marketplaces?: MarketplaceEntry[]
        diagnostics?: PluginDiagnosticEntry[]
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
        loading: false
      }))
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : String(e)
      set({ error: msg, loading: false })
    }
  },

  async selectPlugin(id: string) {
    set({ selectedPluginId: id, selectedPlugin: null, detailLoading: true })
    try {
      const result = (await window.api.appServer.sendRequest('plugin/view', { id })) as { plugin?: PluginEntry }
      if (get().selectedPluginId !== id) return
      const plugin = result.plugin ? normalizePlugin(result.plugin) : null
      set({ selectedPlugin: plugin, detailLoading: false })
    } catch (e: unknown) {
      if (get().selectedPluginId !== id) return
      const msg = e instanceof Error ? e.message : String(e)
      set({ error: msg, detailLoading: false })
    }
  },

  clearSelection() {
    set({ selectedPluginId: null, selectedPlugin: null, detailLoading: false })
  },

  async installPlugin(id: string) {
    try {
      const result = (await window.api.appServer.sendRequest('plugin/install', { id })) as { plugin?: PluginEntry }
      const updated = result.plugin ? normalizePlugin(result.plugin) : undefined
      if (updated) {
        set((state) => ({
          plugins: upsertPlugin(state.plugins, updated),
          selectedPlugin: state.selectedPlugin?.id === updated.id ? updated : state.selectedPlugin
        }))
      } else {
        await get().fetchPlugins()
      }
    } catch (e: unknown) {
      console.error('plugin/install failed:', e)
      throw e
    }
  },

  async installLocalPlugin(path: string) {
    try {
      const result = (await window.api.appServer.sendRequest('plugin/installLocal', { path })) as { plugin?: PluginEntry }
      const updated = result.plugin ? normalizePlugin(result.plugin) : undefined
      if (updated) {
        set((state) => ({
          plugins: upsertPlugin(state.plugins, updated),
          selectedPlugin: state.selectedPlugin?.id === updated.id ? updated : state.selectedPlugin
        }))
      } else {
        await get().fetchPlugins()
      }
      return updated
    } catch (e: unknown) {
      console.error('plugin/installLocal failed:', e)
      throw e
    }
  },

  async removePlugin(id: string) {
    try {
      const result = (await window.api.appServer.sendRequest('plugin/remove', { id })) as { plugin?: PluginEntry }
      const updated = result.plugin ? normalizePlugin(result.plugin) : undefined
      if (updated) {
        set((state) => ({
          plugins: upsertPlugin(state.plugins, updated),
          selectedPlugin: state.selectedPlugin?.id === updated.id ? updated : state.selectedPlugin
        }))
      } else {
        await get().fetchPlugins()
        if (get().selectedPluginId === id) {
          set({ selectedPluginId: null, selectedPlugin: null, detailLoading: false })
        }
      }
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
      })) as { plugin?: PluginEntry }
      const updated = result.plugin ? normalizePlugin(result.plugin) : undefined
      if (updated) {
        set((state) => ({
          plugins: upsertPlugin(state.plugins, updated),
          selectedPlugin: state.selectedPlugin?.id === updated.id ? updated : state.selectedPlugin
        }))
      } else {
        await get().fetchPlugins()
      }
    } catch (e: unknown) {
      console.error('plugin/setEnabled failed:', e)
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
  }
}))

function normalizeMarketplace(marketplace: MarketplaceEntry): MarketplaceEntry {
  return {
    ...marketplace,
    sparsePaths: marketplace.sparsePaths ?? [],
    pluginIds: marketplace.pluginIds ?? [],
    removable: marketplace.removable !== false
  }
}

function normalizePlugin(plugin: PluginEntry): PluginEntry {
  return {
    ...plugin,
    functions: plugin.functions ?? [],
    skills: plugin.skills ?? [],
    apps: (plugin.apps ?? []).map((app) => ({
      ...app,
      nativeApplication: app.nativeApplication ?? null
    })),
    desktopExtensions: (plugin.desktopExtensions ?? []).map((extension) => ({
      ...extension,
      styles: extension.styles ?? [],
      surfaces: extension.surfaces ?? [],
      requiredAppIds: extension.requiredAppIds ?? [],
      connectOrigins: extension.connectOrigins ?? [],
      surfaceWriteScopes: extension.surfaceWriteScopes ?? []
    })),
    hooks: plugin.hooks ?? [],
    mcpServers: plugin.mcpServers ?? [],
    lspServers: (plugin.lspServers ?? []).map((server) => ({
      ...server,
      extensions: server.extensions ?? []
    }))
  }
}

function upsertPlugin(plugins: PluginEntry[], updated: PluginEntry): PluginEntry[] {
  const found = plugins.some((plugin) => plugin.id === updated.id)
  if (!found) return [...plugins, updated]
  return plugins.map((plugin) => (plugin.id === updated.id ? updated : plugin))
}
