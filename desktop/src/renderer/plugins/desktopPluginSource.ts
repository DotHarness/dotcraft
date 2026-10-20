import { create } from 'zustand'
import { normalizeLocale, translate } from '../../shared/locales'
import type { DesktopPluginSource } from '../../shared/desktopPluginSource'
import { requestConfirmDialog, type ConfirmDialogRequest } from '../components/ui/ConfirmDialog'
import { useConnectionStore } from '../stores/connectionStore'
import { usePluginStore } from '../stores/pluginStore'
import type { DesktopPluginRuntime } from './desktopPluginLifecycle'

export const useDesktopPluginSource = create<{
  source: DesktopPluginSource | null
  errors: Record<string, string>
  busy: boolean
}>(() => ({ source: null, errors: {}, busy: false }))

let retry: (() => void) | null = null
let authorize: (() => Promise<void>) | null = null
let revoke: (() => Promise<void>) | null = null
let prepareWorkspaceChange: ((localTarget: boolean) => void) | null = null

export function reportDesktopPluginError(id: string, error: unknown): void {
  useDesktopPluginSource.setState(state => ({ errors: { ...state.errors, [id]: String(error) } }))
}

export function clearDesktopPluginError(id: string): void {
  useDesktopPluginSource.setState(state => {
    const errors = { ...state.errors }
    delete errors[id]
    return { errors }
  })
}

export function prepareDesktopPluginWorkspaceChange(localTarget: boolean): void {
  prepareWorkspaceChange?.(localTarget)
}

export function retryDesktopPlugins(): void { retry?.() }
export async function authorizeDesktopPlugins(): Promise<void> { await authorize?.() }
export async function revokeDesktopPlugins(): Promise<void> { await revoke?.() }

export function observeDesktopPluginSource(runtime: DesktopPluginRuntime): () => void {
  let issued = 0
  let stopped = false
  let retainingLocal = false
  let dialog: ConfirmDialogRequest | null = null
  const asked = new Set<string>()
  const t = (key: string, vars?: Record<string, string>) => translate(normalizeLocale(document.documentElement.lang), key, vars)

  prepareWorkspaceChange = (localTarget) => {
    retainingLocal = localTarget && (useDesktopPluginSource.getState().source?.remote === false || retainingLocal)
    if (retainingLocal) runtime.prepareForWorkspaceChange()
    else runtime.reconcile([])
  }

  const sync = async (forcePrompt = false): Promise<void> => {
    if (dialog || useConnectionStore.getState().status !== 'connected') return
    const token = ++issued
    const workspaceEpoch = usePluginStore.getState().workspaceEpoch
    const current = () => !stopped && token === issued && workspaceEpoch === usePluginStore.getState().workspaceEpoch
    useDesktopPluginSource.setState({ busy: true })
    try {
      const source = await window.api.desktopPlugins.context()
      if (!current()) return
      if (useDesktopPluginSource.getState().source?.sourceKey !== source.sourceKey) {
        if (!retainingLocal || source.remote) runtime.reconcile([])
        useDesktopPluginSource.setState({ errors: {} })
      }
      useDesktopPluginSource.setState({ source })
      clearDesktopPluginError('source')
      if (source.remote || !source.trusted) retainingLocal = false
      if (source.remote && !source.supported) {
        runtime.reconcile([])
        return
      }
      const plugins = usePluginStore.getState().plugins
      if (!source.trusted) {
        runtime.reconcile([])
        const hasDesktop = plugins.some(plugin => plugin.installed && plugin.enabled && plugin.desktop)
        if (!forcePrompt && (!hasDesktop || asked.has(source.sourceKey))) return
        asked.add(source.sourceKey)
        dialog = requestConfirmDialog({
          title: t('desktopPlugins.remote.authorize'),
          message: t('desktopPlugins.remote.confirm', { workspace: source.workspacePath }),
          confirmLabel: t('desktopPlugins.remote.allow'),
          cancelLabel: t('common.cancel')
        })
        const pendingDialog = dialog
        const allowed = await pendingDialog.result
        if (dialog === pendingDialog) dialog = null
        if (!current() || !allowed) return
        const trusted = await window.api.desktopPlugins.setTrusted({ sourceKey: source.sourceKey, trusted: true })
        if (!current()) return
        useDesktopPluginSource.setState({ source: trusted })
      }
      if (current()) {
        const catalog = usePluginStore.getState()
        if (!catalog.hasCompleteCatalog) return
        runtime.reconcile(catalog.plugins, source.sourceKey)
        retainingLocal = false
      }
    } catch (error) {
      if (current()) {
        if (retainingLocal) {
          retainingLocal = false
          runtime.reconcile([])
        }
        reportDesktopPluginError('source', error)
      }
    } finally {
      if (current()) useDesktopPluginSource.setState({ busy: false })
    }
  }
  retry = () => {
    retainingLocal = false
    runtime.reconcile([])
    useDesktopPluginSource.setState({ errors: {} })
    void sync()
  }
  authorize = () => sync(true)
  revoke = async () => {
    const source = useDesktopPluginSource.getState().source
    if (!source) return
    const token = ++issued
    dialog?.dismiss()
    dialog = null
    runtime.reconcile([])
    useDesktopPluginSource.setState({ busy: true })
    asked.add(source.sourceKey)
    try {
      const next = await window.api.desktopPlugins.setTrusted({ sourceKey: source.sourceKey, trusted: false })
      if (!stopped && token === issued && useDesktopPluginSource.getState().source?.sourceKey === next.sourceKey) {
        useDesktopPluginSource.setState({ source: next, errors: {} })
      }
    } catch (error) {
      if (!stopped && token === issued) reportDesktopPluginError('source', error)
    } finally {
      if (!stopped && token === issued) useDesktopPluginSource.setState({ busy: false })
    }
  }
  const stopPlugins = usePluginStore.subscribe((next, previous) => {
    if (next.catalogStatus === 'error' && !next.hasCompleteCatalog) {
      retainingLocal = false
      runtime.reconcile([])
    }
    if (next.plugins !== previous.plugins) {
      if (!next.plugins.length && (!retainingLocal || next.hasCompleteCatalog)) runtime.reconcile([])
      void sync()
    }
  })
  const stopConnection = useConnectionStore.subscribe((next, previous) => {
    if (next.connectionEpoch === previous.connectionEpoch) return
    ++issued
    dialog?.dismiss()
    dialog = null
    asked.clear()
    prepareDesktopPluginWorkspaceChange(next.status === 'connected' || next.status === 'connecting')
    useDesktopPluginSource.setState({ source: null, errors: {}, busy: false })
    usePluginStore.getState().resetForWorkspaceChange()
    if (next.status === 'connected') void sync()
  })
  void sync()
  return () => {
    stopped = true
    ++issued
    dialog?.dismiss()
    stopPlugins()
    stopConnection()
    retry = authorize = revoke = prepareWorkspaceChange = null
    useDesktopPluginSource.setState({ source: null, errors: {}, busy: false })
  }
}
