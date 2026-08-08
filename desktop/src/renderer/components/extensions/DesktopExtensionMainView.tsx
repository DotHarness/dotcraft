import * as React from 'react'
import { useEffect, useMemo, useState } from 'react'
import type { DesktopMainViewExtension } from '../../utils/desktopExtensionRegistry'
import type { PluginAppInfo } from '../../stores/pluginStore'
import type { ActiveMainView } from '../../stores/uiStore'
import { useUIStore } from '../../stores/uiStore'
import { useThreadStore } from '../../stores/threadStore'
import { useWorkspaceProjectsStore } from '../../stores/workspaceProjectsStore'
import { openWorkspaceThread } from '../../utils/openWorkspaceThread'
import { remapDesktopExtensionToLocalRoot } from '../../utils/desktopExtensionLocalRoot'
import { removeToast, showToast, type ToastType } from '../../stores/toastStore'
import { TeamsView } from '../teams/TeamsView'
import { AgentBuilderView } from '../agents/AgentBuilderView'
import { OratorioView } from '../oratorio/OratorioView'
import { OratorioSettingsSurface } from '../oratorio/OratorioSettingsSurface'

export type ExtensionComponent = React.ComponentType<DesktopExtensionComponentProps>

interface DesktopExtensionComponentProps {
  host: DesktopExtensionHost
  viewId: string
}

export interface DesktopExtensionHost {
  react: typeof React
  plugin: {
    id: string
    displayName: string
    rootPath: string
  }
  extension: {
    id: string
    displayName: string
  }
  appBindings: {
    getConnectionStatus(appId: string): Promise<AppConnectionStatus>
    startConnection(appId: string): Promise<AppConnectionStartResult>
    openApp(appId: string, url: string): Promise<void>
  }
  network: {
    getJson(url: string, timeoutMs?: number): Promise<unknown>
    postJson(url: string, body: unknown, timeoutMs?: number): Promise<unknown>
  }
  appSurfaces: {
    getJson(appId: string, surfaceId: string, relativePath: string, timeoutMs?: number): Promise<unknown>
    postJson(appId: string, surfaceId: string, relativePath: string, body: unknown, timeoutMs?: number): Promise<unknown>
  }
  /**
   * Scoped DotCraft AppServer JSON-RPC bridge. Each call is allow-listed in the
   * main process against the extension's `appServerScopes` (declared in
   * desktop-extensions.json), e.g. `["agent/profiles/*", "thread/start"]`.
   */
  appServer: {
    request(method: string, params?: unknown, timeoutMs?: number): Promise<unknown>
  }
  navigation: {
    setActiveMainView(view: ActiveMainView): void
    openThread(threadId: string, workspacePath?: string): Promise<void>
  }
  ui: {
    /**
     * Shows a host toast in DotCraft's native notification stack. Supports an
     * optional inline action (e.g. Undo) and an `onExpire` callback that fires
     * if the toast auto-dismisses or is closed without the action being taken
     * (use it to commit a deferred change). Returns a function that dismisses it.
     */
    showToast(options: ExtensionToastOptions): () => void
  }
  components: {
    TeamsView: React.ComponentType
    AgentBuilderView: React.ComponentType
    OratorioView: React.ComponentType<DesktopExtensionComponentProps>
    OratorioSettingsPanel: React.ComponentType<DesktopExtensionComponentProps>
  }
}

interface ExtensionToastOptions {
  message: string
  type?: ToastType
  durationMs?: number
  action?: { label: string; icon?: string; onClick: () => void }
  onExpire?: () => void
}

interface AppHandoff {
  mode: string
  uri?: string | null
}

interface AppConnectionStartResult {
  connectionRequestId: string
  appId: string
  state: string
  expiresAt: string
  handoff: AppHandoff
}

interface AppConnectionStatus {
  appId: string
  state: string
  connectedAt?: string | null
  expiresAt?: string | null
  accountLabel?: string | null
  diagnostic?: string | null
  publicMetadata?: {
    displayName?: string | null
    message?: string | null
    surfaceEndpoints?: Record<string, string>
  } | null
}

export interface DesktopExtensionActivation {
  mainViews?: Record<string, ExtensionComponent>
  settingsPanels?: Record<string, ExtensionComponent>
  surfaces?: {
    mainViews?: Record<string, ExtensionComponent>
    settingsPanels?: Record<string, ExtensionComponent>
  }
}

type DesktopExtensionModule =
  | ExtensionComponent
  | {
    default?: ExtensionComponent
    activate?: (host: DesktopExtensionHost) => DesktopExtensionActivation | Promise<DesktopExtensionActivation>
    mainViews?: Record<string, ExtensionComponent>
    settingsPanels?: Record<string, ExtensionComponent>
    surfaces?: {
      mainViews?: Record<string, ExtensionComponent>
      settingsPanels?: Record<string, ExtensionComponent>
    }
  }

const activationCache = new Map<string, Promise<DesktopExtensionActivation>>()
const injectedStyles = new Set<string>()

interface DesktopExtensionMainViewProps {
  entry: DesktopMainViewExtension
}

export function DesktopExtensionMainView({ entry }: DesktopExtensionMainViewProps): JSX.Element {
  const [component, setComponent] = useState<ExtensionComponent | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [grantId, setGrantId] = useState<string | null>(null)
  const setActiveMainView = useUIStore((s) => s.setActiveMainView)
  const setActiveThreadId = useThreadStore((s) => s.setActiveThreadId)

  const host = useMemo<DesktopExtensionHost | null>(() => {
    if (!grantId) return null
    return createDesktopExtensionHost(entry, grantId, setActiveMainView, setActiveThreadId)
  }, [entry, grantId, setActiveMainView, setActiveThreadId])

  useEffect(() => {
    let cancelled = false
    let activeGrantId: string | null = null
    setLoading(true)
    setError(null)
    setComponent(null)
    setGrantId(null)

    authorizeAndLoadActivation(entry, setActiveMainView, setActiveThreadId)
      .then(({ activation, grantId }) => {
        activeGrantId = grantId
        if (cancelled) {
          void window.api.desktopExtensions.revokeExtension({ grantId })
          return
        }
        const next = activation.surfaces?.mainViews?.[entry.viewId] ?? activation.mainViews?.[entry.viewId] ?? null
        if (!next) {
          activeGrantId = null
          void window.api.desktopExtensions.revokeExtension({ grantId })
          setError(`Desktop extension '${entry.extension.displayName}' did not provide main view '${entry.viewId}'.`)
          setLoading(false)
          return
        }
        setGrantId(grantId)
        setComponent(() => next)
        setLoading(false)
      })
      .catch((err: unknown) => {
        if (cancelled) return
        setError(err instanceof Error ? err.message : String(err))
        setLoading(false)
      })

    return () => {
      cancelled = true
      if (activeGrantId) {
        void window.api.desktopExtensions.revokeExtension({ grantId: activeGrantId })
      }
    }
  }, [entry, setActiveMainView, setActiveThreadId])

  if (loading) {
    return <ExtensionStatus title={entry.label} message="Loading desktop extension..." />
  }

  if (error || !component || !host) {
    return <ExtensionStatus title={entry.label} message={error ?? 'Desktop extension is unavailable.'} />
  }

  const Component = component
  return <Component host={host} viewId={entry.viewId} />
}

export function createDesktopExtensionHost(
  entry: DesktopMainViewExtension,
  grantId: string,
  setActiveMainView: (view: ActiveMainView) => void,
  setActiveThreadId: (threadId: string | null) => void
): DesktopExtensionHost {
  return {
    react: React,
    plugin: {
      id: entry.plugin.id,
      displayName: entry.plugin.displayName,
      rootPath: entry.plugin.rootPath
    },
    extension: {
      id: entry.extension.id,
      displayName: entry.extension.displayName
    },
    appBindings: {
      getConnectionStatus(appId) {
        return window.api.desktopExtensions.getAppConnectionStatus({ grantId, appId }) as Promise<AppConnectionStatus>
      },
      startConnection(appId) {
        return window.api.desktopExtensions.startAppConnection({ grantId, appId }) as Promise<AppConnectionStartResult>
      },
      openApp(appId, url) {
        if (!isAppUrlAllowed(entry.plugin.apps ?? [], appId, url)) {
          return Promise.reject(new Error(`Desktop extension '${entry.extension.id}' is not allowed to open this app URL.`))
        }
        return window.api.desktopExtensions.openApp({ grantId, appId, url })
      }
    },
    network: {
      getJson(url, timeoutMs) {
        return window.api.desktopExtensions.fetchJson({
          grantId,
          url,
          timeoutMs
        })
      },
      postJson(url, body, timeoutMs) {
        // Keep the legacy scoped network write transport for existing extensions.
        if ((entry.extension.surfaceWriteScopes ?? []).length === 0) {
          return Promise.reject(new Error(`Desktop extension '${entry.extension.id}' did not declare surfaceWriteScopes and cannot write.`))
        }
        return window.api.desktopExtensions.postJson({
          grantId,
          url,
          body,
          timeoutMs
        })
      }
    },
    appSurfaces: {
      getJson(appId, surfaceId, relativePath, timeoutMs) {
        return window.api.desktopExtensions.appSurfaceGetJson({
          grantId,
          appId,
          surfaceId,
          relativePath,
          timeoutMs
        })
      },
      postJson(appId, surfaceId, relativePath, body, timeoutMs) {
        return window.api.desktopExtensions.appSurfacePostJson({
          grantId,
          appId,
          surfaceId,
          relativePath,
          body,
          timeoutMs
        })
      }
    },
    appServer: {
      request(method, params, timeoutMs) {
        return window.api.desktopExtensions.appServerRequest({ grantId, method, params, timeoutMs })
      }
    },
    navigation: {
      setActiveMainView,
      async openThread(threadId, workspacePath) {
        const foregroundWorkspace = useWorkspaceProjectsStore.getState().foregroundWorkspacePath
        await openWorkspaceThread({
          threadId,
          workspacePath,
          foregroundWorkspacePath: foregroundWorkspace,
          switchWorkspace: (path) => window.api.workspace.switch(path),
          setPending: (payload) => useUIStore.getState().setPendingProjectThreadOpen(payload),
          clearPending: (projectKey, pendingThreadId) => useUIStore.getState().clearPendingProjectThreadOpen(projectKey, pendingThreadId),
          activateThread: (targetThreadId) => {
            setActiveThreadId(targetThreadId)
            setActiveMainView('conversation')
          }
        })
      }
    },
    ui: {
      showToast(options) {
        const id = showToast({
          message: options.message,
          type: options.type,
          durationMs: options.durationMs,
          action: options.action,
          onExpire: options.onExpire
        })
        return () => removeToast(id)
      }
    },
    components: {
      TeamsView,
      AgentBuilderView,
      OratorioView,
      OratorioSettingsPanel: OratorioSettingsSurface
    }
  }
}

export async function authorizeAndLoadActivation(
  entry: DesktopMainViewExtension,
  setActiveMainView: (view: ActiveMainView) => void,
  setActiveThreadId: (threadId: string | null) => void
): Promise<{ activation: DesktopExtensionActivation; grantId: string }> {
  const { grantId, rootPath } = await window.api.desktopExtensions.authorizeExtension({
    pluginId: entry.plugin.id,
    rootPath: entry.plugin.rootPath,
    extensionId: entry.extension.id
  })
  try {
    const localEntry = remapDesktopExtensionToLocalRoot(entry, rootPath)
    const host = createDesktopExtensionHost(localEntry, grantId, setActiveMainView, setActiveThreadId)
    const activation = await loadActivation(localEntry, host, grantId)
    return { activation, grantId }
  } catch (error) {
    void window.api.desktopExtensions.revokeExtension({ grantId })
    throw error
  }
}

async function loadActivation(
  entry: DesktopMainViewExtension,
  host: DesktopExtensionHost,
  grantId: string
): Promise<DesktopExtensionActivation> {
  const cacheKey = `${entry.plugin.id}:${entry.extension.id}:${entry.extension.entry}:${grantId}`
  let cached = activationCache.get(cacheKey)
  if (!cached) {
    cached = loadActivationUncached(entry, host)
    activationCache.set(cacheKey, cached)
  }
  return cached
}

async function loadActivationUncached(
  entry: DesktopMainViewExtension,
  host: DesktopExtensionHost
): Promise<DesktopExtensionActivation> {
  if (!entry.plugin.rootPath) {
    throw new Error(`Plugin '${entry.plugin.id}' does not have an installed root path.`)
  }
  await injectStyles(entry)
  const { url } = await window.api.desktopExtensions.toPluginUrl(entry.plugin.id, entry.extension.entry)
  const mod = await import(/* @vite-ignore */ url) as DesktopExtensionModule
  if (typeof mod === 'function') {
    return { mainViews: { [entry.viewId]: mod } }
  }
  if ('activate' in mod && typeof mod.activate === 'function') {
    return mod.activate(host)
  }
  if ('default' in mod && typeof mod.default === 'function') {
    return { mainViews: { [entry.viewId]: mod.default as ExtensionComponent } }
  }
  return {
    mainViews: mod.mainViews ?? {},
    surfaces: mod.surfaces
  }
}

async function injectStyles(entry: DesktopMainViewExtension): Promise<void> {
  for (const stylePath of entry.extension.styles) {
    const { url } = await window.api.desktopExtensions.toPluginUrl(entry.plugin.id, stylePath)
    if (injectedStyles.has(url)) continue
    injectedStyles.add(url)
    const link = document.createElement('link')
    link.rel = 'stylesheet'
    link.href = url
    link.dataset.dotcraftDesktopExtension = `${entry.plugin.id}:${entry.extension.id}`
    document.head.appendChild(link)
  }
}

function ExtensionStatus({ title, message }: { title: string; message: string }): JSX.Element {
  return (
    <div style={{
      height: '100%',
      display: 'grid',
      placeItems: 'center',
      padding: 24,
      color: 'var(--text-secondary)'
    }}>
      <div style={{
        maxWidth: 520,
        display: 'grid',
        gap: 8,
        textAlign: 'center'
      }}>
        <h2 style={{
          margin: 0,
          color: 'var(--text-primary)',
          fontSize: 'var(--type-title-size)',
          lineHeight: 'var(--type-title-line-height)',
          fontWeight: 'var(--type-ui-emphasis-weight)'
        }}>{title}</h2>
        <p style={{
          margin: 0,
          fontSize: 'var(--type-body-size)',
          lineHeight: 'var(--type-body-line-height)'
        }}>{message}</p>
      </div>
    </div>
  )
}

function isAppUrlAllowed(apps: PluginAppInfo[], appId: string, url: string): boolean {
  const app = apps.find((candidate) => candidate.appId === appId)
  const protocol = app?.nativeApplication?.protocol?.trim().replace(/:$/, '').toLowerCase()
  if (!protocol) return false

  try {
    const parsed = new URL(url)
    return parsed.protocol.toLowerCase() === `${protocol}:`
  } catch {
    return false
  }
}
