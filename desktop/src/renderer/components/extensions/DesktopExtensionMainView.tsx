import * as React from 'react'
import { useEffect, useMemo, useState } from 'react'
import type { DesktopMainViewExtension } from '../../utils/desktopExtensionRegistry'
import type { ActiveMainView } from '../../stores/uiStore'
import { useUIStore } from '../../stores/uiStore'
import { useThreadStore } from '../../stores/threadStore'
import { TeamsView } from '../teams/TeamsView'

type ExtensionComponent = React.ComponentType<DesktopExtensionComponentProps>

interface DesktopExtensionComponentProps {
  host: DesktopExtensionHost
  viewId: string
}

interface DesktopExtensionHost {
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
  appServer: {
    sendRequest(method: string, params?: unknown, timeoutMs?: number): Promise<unknown>
  }
  navigation: {
    setActiveMainView(view: ActiveMainView): void
    openThread(threadId: string): void
  }
  components: {
    TeamsView: React.ComponentType
  }
}

interface DesktopExtensionActivation {
  mainViews?: Record<string, ExtensionComponent>
  surfaces?: {
    mainViews?: Record<string, ExtensionComponent>
  }
}

type DesktopExtensionModule =
  | ExtensionComponent
  | {
    default?: ExtensionComponent
    activate?: (host: DesktopExtensionHost) => DesktopExtensionActivation | Promise<DesktopExtensionActivation>
    mainViews?: Record<string, ExtensionComponent>
    surfaces?: {
      mainViews?: Record<string, ExtensionComponent>
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
  const setActiveMainView = useUIStore((s) => s.setActiveMainView)
  const setActiveThreadId = useThreadStore((s) => s.setActiveThreadId)

  const host = useMemo<DesktopExtensionHost>(() => ({
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
    appServer: {
      sendRequest(method, params, timeoutMs) {
        if (!isAppServerMethodAllowed(entry.extension.permissions, method)) {
          return Promise.reject(new Error(`Desktop extension '${entry.extension.id}' is not allowed to call '${method}'.`))
        }
        return window.api.appServer.sendRequest(method, params, timeoutMs)
      }
    },
    navigation: {
      setActiveMainView,
      openThread(threadId) {
        setActiveThreadId(threadId)
        setActiveMainView('conversation')
      }
    },
    components: {
      TeamsView
    }
  }), [entry.extension.displayName, entry.extension.id, entry.plugin.displayName, entry.plugin.id, entry.plugin.rootPath, setActiveMainView, setActiveThreadId])

  useEffect(() => {
    let cancelled = false
    setLoading(true)
    setError(null)
    setComponent(null)

    loadActivation(entry, host)
      .then((activation) => {
        if (cancelled) return
        const next = activation.surfaces?.mainViews?.[entry.viewId] ?? activation.mainViews?.[entry.viewId] ?? null
        if (!next) {
          setError(`Desktop extension '${entry.extension.displayName}' did not provide main view '${entry.viewId}'.`)
          setLoading(false)
          return
        }
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
    }
  }, [entry, host])

  if (loading) {
    return <ExtensionStatus title={entry.label} message="Loading desktop extension..." />
  }

  if (error || !component) {
    return <ExtensionStatus title={entry.label} message={error ?? 'Desktop extension is unavailable.'} />
  }

  const Component = component
  return <Component host={host} viewId={entry.viewId} />
}

async function loadActivation(
  entry: DesktopMainViewExtension,
  host: DesktopExtensionHost
): Promise<DesktopExtensionActivation> {
  const cacheKey = `${entry.plugin.id}:${entry.extension.id}:${entry.extension.entry}`
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
  await window.api.desktopExtensions.authorizePluginRoot(entry.plugin.id, entry.plugin.rootPath)
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

function isAppServerMethodAllowed(permissions: string[], method: string): boolean {
  return permissions.some((permission) => {
    if (!permission.startsWith('appServer:')) return false
    const pattern = permission.slice('appServer:'.length)
    if (pattern === method || pattern === '*') return true
    if (pattern.endsWith('/*')) {
      const prefix = pattern.slice(0, -1)
      return method.startsWith(prefix)
    }
    return false
  })
}
