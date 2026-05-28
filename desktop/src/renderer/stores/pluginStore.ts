import { create } from 'zustand'

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

export interface PluginAppToolInfo {
  name: string
  scope: string
  risk: string
  defaultExposure: string
  description?: string | null
}

export interface PluginAppDynamicToolCatalog {
  enabled: boolean
  description?: string | null
}

export interface PluginAppInfo {
  appId: string
  toolNamespace: string
  displayName: string
  developerName: string
  description: string
  category?: string | null
  icon?: string | null
  releasePage?: string | null
  nativeApplication?: PluginAppNativeApplication | null
  toolCatalog: PluginAppToolInfo[]
  dynamicToolCatalog?: PluginAppDynamicToolCatalog | null
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
  interface?: PluginInterface | null
  functions: PluginFunctionInfo[]
  skills: PluginSkillInfo[]
  apps?: PluginAppInfo[]
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

interface PluginState {
  plugins: PluginEntry[]
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
  removePlugin(id: string): Promise<void>
  togglePluginEnabled(id: string, enabled: boolean): Promise<void>
}

export const usePluginStore = create<PluginState>((set, get) => ({
  plugins: [],
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
      })) as { plugins?: PluginEntry[]; diagnostics?: PluginDiagnosticEntry[] }
      const plugins = (result.plugins ?? []).map(normalizePlugin)
      const diagnostics = result.diagnostics ?? []
      set((state) => ({
        plugins,
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
      const plugin = result.plugin ? normalizePlugin(result.plugin) : null
      set({ selectedPlugin: plugin, detailLoading: false })
    } catch (e: unknown) {
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
  }
}))

function normalizePlugin(plugin: PluginEntry): PluginEntry {
  return {
    ...plugin,
    functions: plugin.functions ?? [],
    skills: plugin.skills ?? [],
    apps: (plugin.apps ?? []).map((app) => ({
      ...app,
      toolCatalog: app.toolCatalog ?? [],
      dynamicToolCatalog: app.dynamicToolCatalog ?? { enabled: false }
    })),
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
