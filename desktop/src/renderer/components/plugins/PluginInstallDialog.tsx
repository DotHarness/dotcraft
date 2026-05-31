import { useCallback, useEffect, useMemo, useState, type CSSProperties, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { Box, Check, Code2, ExternalLink, Link2, RefreshCw, Server, Settings, Wrench, X } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import type { PluginAppInfo, PluginEntry } from '../../stores/pluginStore'
import { useAppBindingStore, type AppInfo } from '../../stores/appBindingStore'
import { addToast } from '../../stores/toastStore'
import { PluginIcon, pluginSubtitle, pluginTitle } from './PluginCatalogItem'
import { openAppHandoff } from './AppBindingPanel'
import { getPluginDesktopExtensionContents } from '../../utils/pluginDesktopExtensions'

type NativeStatus = 'installed' | 'missing' | 'unknown'
type SetupStage = 'pluginInstall' | 'nativeAppRequired' | 'nativeAppPending' | 'appConnect' | 'handoffOpened' | 'complete'

interface AppSetupState {
  app: PluginAppInfo
  liveApp?: AppInfo
  nativeStatus: NativeStatus | string
  connected: boolean
  nativeOpened: boolean
  handoffOpened: boolean
}

const dotharLogoUrl = new URL('../../assets/brand/dothar.svg', import.meta.url).href

export function PluginInstallDialog({
  plugin,
  installing,
  onInstall,
  onClose
}: {
  plugin: PluginEntry
  installing?: boolean
  onInstall: () => Promise<void> | void
  onClose: () => void
}): JSX.Element {
  const t = useT()
  const title = pluginTitle(plugin)
  const capabilities = plugin.interface?.capabilities ?? []
  const pluginApps = useMemo(() => plugin.apps ?? [], [plugin.apps])
  const hasApps = pluginApps.length > 0
  const {
    apps,
    fetchApps,
    startConnection,
    waitForConnection
  } = useAppBindingStore()
  const [nativeStatuses, setNativeStatuses] = useState<Record<string, NativeStatus>>({})
  const [nativeOpenedAppIds, setNativeOpenedAppIds] = useState<Record<string, boolean>>({})
  const [handoffOpenedAppIds, setHandoffOpenedAppIds] = useState<Record<string, boolean>>({})
  const [busyKey, setBusyKey] = useState<string | null>(null)

  const appsById = useMemo(() => new Map(apps.map((app) => [app.appId, app])), [apps])
  const setupApps = useMemo<AppSetupState[]>(() => pluginApps.map((app) => {
    const liveApp = appsById.get(app.appId)
    const nativeStatus = (liveApp?.nativeApp?.status as NativeStatus | undefined)
      ?? nativeStatuses[app.appId]
      ?? 'unknown'
    return {
      app,
      liveApp,
      nativeStatus,
      connected: liveApp?.connectionState === 'connected',
      nativeOpened: nativeOpenedAppIds[app.appId] === true,
      handoffOpened: handoffOpenedAppIds[app.appId] === true || liveApp?.connectionState === 'connecting'
    }
  }), [appsById, handoffOpenedAppIds, nativeOpenedAppIds, nativeStatuses, pluginApps])

  const setupStage = useMemo<{ stage: SetupStage; apps: AppSetupState[] }>(() => {
    if (!hasApps || !plugin.installed) return { stage: 'pluginInstall', apps: [] }

    const appsNeedingNative = setupApps.filter((setupApp) => requiresNativeInstallCheck(setupApp.app) && setupApp.nativeStatus !== 'installed')
    if (appsNeedingNative.length > 0) {
      const hasUnopenedMissingApp = appsNeedingNative.some((setupApp) => setupApp.nativeStatus === 'missing' && !setupApp.nativeOpened)
      return {
        stage: hasUnopenedMissingApp ? 'nativeAppRequired' : 'nativeAppPending',
        apps: appsNeedingNative
      }
    }

    const appsNeedingConnection = setupApps.filter((setupApp) => !setupApp.connected)
    if (appsNeedingConnection.length > 0) {
      return {
        stage: appsNeedingConnection.some((setupApp) => setupApp.handoffOpened) ? 'handoffOpened' : 'appConnect',
        apps: appsNeedingConnection
      }
    }

    return { stage: 'complete', apps: setupApps }
  }, [hasApps, plugin.installed, setupApps])

  const dialogTitle = hasApps && plugin.installed
    ? t('plugins.installDialog.setupTitle', { name: title })
    : t('plugins.installDialog.title', { name: title })

  const refreshNativeStatuses = useCallback(async () => {
    const next: Record<string, NativeStatus> = {}
    await Promise.all(pluginApps.map(async (app) => {
      const protocol = app.nativeApplication?.protocol
      if (!protocol) {
        next[app.appId] = 'unknown'
        return
      }
      try {
        const handler = await window.api.shell.getProtocolHandlerName(protocol)
        next[app.appId] = handler ? 'installed' : 'missing'
      } catch {
        next[app.appId] = 'unknown'
      }
    }))
    setNativeStatuses(next)
  }, [pluginApps])

  useEffect(() => {
    function handleKeyDown(event: KeyboardEvent): void {
      if (event.key === 'Escape') onClose()
    }
    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [onClose])

  useEffect(() => {
    setNativeStatuses({})
    setNativeOpenedAppIds({})
    setHandoffOpenedAppIds({})
  }, [plugin.id])

  useEffect(() => {
    if (!plugin.installed || !hasApps) return
    void refreshNativeStatuses()
  }, [hasApps, plugin.installed, refreshNativeStatuses])

  useEffect(() => {
    if (!plugin.installed || !hasApps) return
    void fetchApps(null, true, 'pluginDetail')
  }, [fetchApps, hasApps, plugin.installed])

  async function run(key: string, action: () => Promise<void>): Promise<void> {
    if (busyKey) return
    setBusyKey(key)
    try {
      await action()
    } catch (err) {
      addToast(err instanceof Error ? err.message : String(err), 'error')
    } finally {
      setBusyKey(null)
    }
  }

  async function refreshAppSetup(): Promise<void> {
    await Promise.all([
      refreshNativeStatuses(),
      fetchApps(null, true, 'pluginDetail')
    ])
  }

  async function handleInstallNative(app: PluginAppInfo): Promise<void> {
    await run(`${app.appId}:native`, async () => {
      const url = app.nativeApplication?.installUrl || app.releasePage
      if (!url) throw new Error(t('appBinding.nativeInstallMissing'))
      await window.api.shell.openExternal(url)
      setNativeOpenedAppIds((current) => ({ ...current, [app.appId]: true }))
    })
  }

  async function handleConnect(app: PluginAppInfo): Promise<void> {
    if (busyKey) return
    setBusyKey(`${app.appId}:connect`)
    try {
      const result = await startConnection(app.appId)
      await openAppHandoff(result.handoff, t)
      setHandoffOpenedAppIds((current) => ({ ...current, [app.appId]: true }))
      addToast(t('appBinding.connectStarted'), 'info')
      void waitForConnection(app.appId)
        .then(() => addToast(t('appBinding.connection.connected'), 'success'))
        .catch((err) => addToast(err instanceof Error ? err.message : String(err), 'error'))
    } catch (err) {
      addToast(err instanceof Error ? err.message : String(err), 'error')
    } finally {
      setBusyKey(null)
    }
  }

  const dialog = (
    <div role="dialog" aria-modal="true" aria-labelledby="plugin-install-title" style={backdrop} onMouseDown={(event) => {
      if (event.target === event.currentTarget) onClose()
    }}>
      <div style={dialogCard} onMouseDown={(event) => event.stopPropagation()}>
        <button type="button" aria-label={t('common.close')} onClick={onClose} style={closeButton}>
          <X size={16} aria-hidden />
        </button>
        <div style={logoRow}>
          <span style={brandLogo}><img src={dotharLogoUrl} alt="" style={logoImg} /></span>
          <span style={dotTrail}>•••</span>
          <PluginIcon plugin={plugin} size={56} />
        </div>
        <h2 id="plugin-install-title" style={titleStyle}>{dialogTitle}</h2>
        <div style={subtitleStyle}>{t('plugins.developedBy', { developer: plugin.interface?.developerName || 'DotHarness' })}</div>
        <div style={infoCard}>
          <div style={cardTitleLine}>
            <strong>{title}</strong>
            <span style={badge}>{plugin.source || 'builtin'}</span>
          </div>
          <div style={muted}>{t('plugins.providedBy', { developer: plugin.interface?.developerName || 'DotHarness' })}</div>
          <div style={muted}>{t('plugins.detail.category')}: {plugin.interface?.category || 'Coding'}</div>
          <Divider />
          <SectionTitle>{t('plugins.detail.about')}</SectionTitle>
          <p style={description}>{plugin.interface?.longDescription || pluginSubtitle(plugin)}</p>
          <Divider />
          <SectionTitle>{t('plugins.detail.contents')}</SectionTitle>
          <ContentChips plugin={plugin} />
          <Divider />
          <SectionTitle>{t('plugins.detail.capabilities')}</SectionTitle>
          <div style={chips}>
            {capabilities.map((capability) => <span key={capability} style={chip}>{capability}</span>)}
          </div>
        </div>

        {setupStage.stage === 'pluginInstall' ? (
          <button type="button" onClick={() => { void onInstall() }} disabled={installing} style={installPrimaryButton}>
            {installing ? t('plugins.installing') : t('plugins.installDialog.addToDotCraft')}
          </button>
        ) : (
          <CurrentSetupStage
            stage={setupStage.stage}
            apps={setupStage.apps}
            busyKey={busyKey}
            onClose={onClose}
            onConnect={handleConnect}
            onInstallNative={handleInstallNative}
            onRefresh={() => { void run('setup:refresh', refreshAppSetup) }}
            t={t}
          />
        )}
      </div>
    </div>
  )

  return createPortal(dialog, document.body) as JSX.Element
}

function ContentChips({ plugin }: { plugin: PluginEntry }): JSX.Element {
  const t = useT()
  return (
    <div style={chips}>
      {getPluginDesktopExtensionContents(plugin, t).map((extension) => (
        <span key={extension.key} style={chip}>
          <Settings size={12} aria-hidden />
          <span>{extension.title} · {extension.kind}</span>
        </span>
      ))}
      {(plugin.apps ?? []).map((app) => (
        <span key={`app:${app.appId}`} style={chip}><Link2 size={12} aria-hidden />{app.displayName}</span>
      ))}
      {plugin.skills.map((skill) => (
        <span key={`skill:${skill.name}`} style={chip}><Box size={12} aria-hidden />{skill.displayName || skill.name}</span>
      ))}
      {plugin.functions.map((fn) => (
        <span key={`tool:${fn.name}`} style={chip}><Wrench size={12} aria-hidden />{fn.name}</span>
      ))}
      {(plugin.mcpServers ?? []).map((server) => (
        <span key={`mcp:${server.runtimeName}`} style={chip}><Server size={12} aria-hidden />{server.runtimeName}</span>
      ))}
      {(plugin.lspServers ?? []).map((server) => (
        <span key={`lsp:${server.runtimeName}`} style={chip}><Code2 size={12} aria-hidden />{server.runtimeName}</span>
      ))}
    </div>
  )
}

function CurrentSetupStage({
  stage,
  apps,
  busyKey,
  onClose,
  onConnect,
  onInstallNative,
  onRefresh,
  t
}: {
  stage: SetupStage
  apps: AppSetupState[]
  busyKey: string | null
  onClose: () => void
  onConnect: (app: PluginAppInfo) => Promise<void>
  onInstallNative: (app: PluginAppInfo) => Promise<void>
  onRefresh: () => void
  t: ReturnType<typeof useT>
}): JSX.Element {
  if (stage === 'complete') {
    return (
      <SetupPanel
        complete
        title={t('plugins.installDialog.completeStep')}
        status={t('plugins.installDialog.allAppsConnected')}
      >
        <button type="button" onClick={onClose} style={primaryButton}>
          <Check size={13} aria-hidden />
          {t('common.close')}
        </button>
      </SetupPanel>
    )
  }

  const nativeStage = stage === 'nativeAppRequired' || stage === 'nativeAppPending'
  const title = nativeStage
    ? t('plugins.installDialog.requiredApps')
    : t('plugins.installDialog.connectRequired')
  const status = nativeStage
    ? t('plugins.installDialog.nativeStep', { name: apps.map((setupApp) => setupApp.app.nativeApplication?.displayName || setupApp.app.displayName).join(', ') })
    : t('plugins.installDialog.connectStep', { name: apps.map((setupApp) => setupApp.app.displayName).join(', ') })

  return (
    <SetupPanel title={title} status={status}>
      <div style={appSetupList}>
        {apps.map((setupApp) => (
          <AppSetupRow
            key={setupApp.app.appId}
            setupApp={setupApp}
            busyKey={busyKey}
            stage={stage}
            onConnect={onConnect}
            onInstallNative={onInstallNative}
            onRefresh={onRefresh}
            t={t}
          />
        ))}
      </div>
    </SetupPanel>
  )
}

function SetupPanel({
  complete,
  title,
  status,
  children
}: {
  complete?: boolean
  title: string
  status: string
  children?: ReactNode
}): JSX.Element {
  return (
    <div style={setupPanel}>
      <span style={setupIcon(complete === true)}>{complete ? <Check size={13} aria-hidden /> : <Link2 size={13} aria-hidden />}</span>
      <span style={stepBody}>
        <strong style={stepTitle}>{title}</strong>
        <span style={muted}>{status}</span>
      </span>
      {children && <div style={stepAction}>{children}</div>}
    </div>
  )
}

function AppSetupRow({
  setupApp,
  busyKey,
  stage,
  onConnect,
  onInstallNative,
  onRefresh,
  t
}: {
  setupApp: AppSetupState
  busyKey: string | null
  stage: SetupStage
  onConnect: (app: PluginAppInfo) => Promise<void>
  onInstallNative: (app: PluginAppInfo) => Promise<void>
  onRefresh: () => void
  t: ReturnType<typeof useT>
}): JSX.Element {
  const { app, liveApp, nativeStatus, nativeOpened, handoffOpened } = setupApp
  const waitingForApp = stage === 'handoffOpened' && handoffOpened
  const nativePending = nativeStatus !== 'missing' || nativeOpened
  return (
    <div style={appSetupRow}>
      <AppSetupIcon app={app} />
      <span style={appSetupBody}>
        <strong style={stepTitle}>{app.displayName}</strong>
        <span style={muted}>
          {stage === 'nativeAppRequired' || stage === 'nativeAppPending'
            ? nativePending ? t('plugins.installDialog.nativePending') : nativeStatusLabel(nativeStatus, t)
            : waitingForApp ? t('plugins.installDialog.waitingForApp') : connectionStatusLabel(liveApp, t)}
        </span>
      </span>
      <span style={appSetupActions}>
        {stage === 'nativeAppRequired' || stage === 'nativeAppPending' ? (
          nativePending ? (
            <button type="button" onClick={onRefresh} disabled={busyKey != null} style={secondaryButton}>
              <RefreshCw size={13} aria-hidden />
              {t('appBinding.refresh')}
            </button>
          ) : (
            <button type="button" onClick={() => { void onInstallNative(app) }} disabled={busyKey != null} style={primaryButton}>
              <ExternalLink size={13} aria-hidden />
              {t('appBinding.installNative')}
            </button>
          )
        ) : waitingForApp ? (
          <>
            <button type="button" disabled style={secondaryButton}>
              <Link2 size={13} aria-hidden />
              {t('plugins.installDialog.linkOpened')}
            </button>
            <button type="button" onClick={onRefresh} disabled={busyKey != null} style={secondaryButton}>
              <RefreshCw size={13} aria-hidden />
              {t('appBinding.refresh')}
            </button>
            <button type="button" onClick={() => { void onConnect(app) }} disabled={busyKey != null} style={secondaryButton}>
              <Link2 size={13} aria-hidden />
              {t('appBinding.connect')}
            </button>
          </>
        ) : (
          <button type="button" onClick={() => { void onConnect(app) }} disabled={busyKey != null} style={primaryButton}>
            <Link2 size={13} aria-hidden />
            {t('appBinding.connect')}
          </button>
        )}
      </span>
    </div>
  )
}

function AppSetupIcon({ app }: { app: PluginAppInfo }): JSX.Element {
  if (app.icon) return <img src={app.icon} alt="" style={appSetupIconImg} />
  return (
    <span style={appSetupIconFallback} aria-hidden>
      <Link2 size={15} />
    </span>
  )
}

function requiresNativeInstallCheck(app: PluginAppInfo): boolean {
  return Boolean(app.nativeApplication?.protocol?.trim())
}

function nativeStatusLabel(status: NativeStatus | string, t: ReturnType<typeof useT>): string {
  if (status === 'installed') return t('appBinding.native.installed')
  if (status === 'missing') return t('appBinding.native.missing')
  return t('appBinding.native.unknown')
}

function connectionStatusLabel(app: AppInfo | undefined, t: ReturnType<typeof useT>): string {
  if (!app) return t('appBinding.connection.notConnected')
  if (app.connectionState === 'connected') return t('appBinding.connection.connected')
  if (app.connectionState === 'connecting') return t('appBinding.connection.connecting')
  if (app.connectionState === 'needsAuth') return t('appBinding.connection.needsAuth')
  if (app.connectionState === 'error') return t('appBinding.connection.error')
  return t('appBinding.connection.notConnected')
}

function Divider(): JSX.Element {
  return <div style={divider} />
}

function SectionTitle({ children }: { children: string }): JSX.Element {
  return <div style={sectionTitle}>{children}</div>
}

const backdrop: CSSProperties = {
  position: 'fixed',
  inset: 0,
  zIndex: 10000,
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  backgroundColor: 'var(--overlay-scrim)'
}
const dialogCard: CSSProperties = {
  position: 'relative',
  width: 600,
  maxWidth: 'calc(100vw - 48px)',
  maxHeight: 'calc(100vh - 48px)',
  overflow: 'auto',
  borderRadius: 18,
  backgroundColor: 'var(--bg-secondary)',
  boxShadow: 'var(--shadow-level-3)',
  padding: '32px 24px 24px'
}
const closeButton: CSSProperties = {
  position: 'absolute',
  top: 18,
  right: 18,
  width: 30,
  height: 30,
  border: 'none',
  borderRadius: 8,
  background: 'transparent',
  color: 'var(--text-secondary)',
  cursor: 'pointer'
}
const logoRow: CSSProperties = { display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 18 }
const brandLogo: CSSProperties = { width: 56, height: 56, borderRadius: 12, overflow: 'hidden', display: 'inline-flex' }
const logoImg: CSSProperties = { width: '100%', height: '100%' }
const dotTrail: CSSProperties = { color: 'var(--text-dimmed)', letterSpacing: 2 }
const titleStyle: CSSProperties = { margin: '18px 0 4px', textAlign: 'center', fontSize: 22, fontWeight: 700 }
const subtitleStyle: CSSProperties = { textAlign: 'center', color: 'var(--text-secondary)', fontSize: 13, marginBottom: 24 }
const infoCard: CSSProperties = { border: '1px solid var(--border-default)', borderRadius: 12, padding: 16, marginBottom: 18 }
const cardTitleLine: CSSProperties = { display: 'flex', alignItems: 'center', gap: 8, fontSize: 14 }
const badge: CSSProperties = { padding: '2px 7px', borderRadius: 999, backgroundColor: 'var(--bg-tertiary)', color: 'var(--text-secondary)', fontSize: 11 }
const muted: CSSProperties = { marginTop: 8, color: 'var(--text-secondary)', fontSize: 12 }
const divider: CSSProperties = { height: 1, backgroundColor: 'var(--border-subtle)', margin: '16px 0' }
const sectionTitle: CSSProperties = { fontSize: 13, fontWeight: 700, marginBottom: 8 }
const description: CSSProperties = { margin: 0, color: 'var(--text-secondary)', fontSize: 13, lineHeight: 1.5 }
const chips: CSSProperties = { display: 'flex', flexWrap: 'wrap', gap: 8 }
const chip: CSSProperties = { display: 'inline-flex', alignItems: 'center', gap: 5, minHeight: 26, padding: '4px 9px', borderRadius: 8, border: '1px solid var(--border-default)', fontSize: 12 }
const setupPanel: CSSProperties = { display: 'grid', gridTemplateColumns: '24px minmax(0, 1fr)', alignItems: 'start', columnGap: 12, rowGap: 10, border: '1px solid var(--border-subtle)', borderRadius: 10, padding: 12 }
const setupIcon = (complete: boolean): CSSProperties => ({
  width: 24,
  height: 24,
  borderRadius: 999,
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  background: complete ? 'var(--success-bg)' : 'var(--bg-tertiary)',
  color: complete ? 'var(--success)' : 'var(--text-secondary)',
  fontSize: 12,
  fontWeight: 700,
  flex: '0 0 auto'
})
const stepBody: CSSProperties = { display: 'flex', flexDirection: 'column', flex: 1, minWidth: 0 }
const stepTitle: CSSProperties = { color: 'var(--text-primary)', fontSize: 13 }
const stepAction: CSSProperties = { gridColumn: '2', display: 'flex', width: '100%', minWidth: 0 }
const appSetupList: CSSProperties = { display: 'flex', flexDirection: 'column', gap: 10, width: '100%' }
const appSetupRow: CSSProperties = { display: 'flex', alignItems: 'center', flexWrap: 'wrap', gap: 12, width: '100%' }
const appSetupBody: CSSProperties = { display: 'flex', flexDirection: 'column', flex: '1 1 180px', minWidth: 0 }
const appSetupActions: CSSProperties = { display: 'inline-flex', flexWrap: 'wrap', justifyContent: 'flex-end', gap: 8, flex: '0 1 auto' }
const appSetupIconImg: CSSProperties = { width: 36, height: 36, borderRadius: 8, objectFit: 'contain', flex: '0 0 auto' }
const appSetupIconFallback: CSSProperties = { width: 36, height: 36, borderRadius: 8, background: 'var(--bg-tertiary)', color: 'var(--text-secondary)', display: 'inline-flex', alignItems: 'center', justifyContent: 'center', flex: '0 0 auto' }
const primaryButton: CSSProperties = { minHeight: 38, border: 'none', borderRadius: 999, backgroundColor: 'var(--text-primary)', color: 'var(--bg-primary)', fontSize: 13, fontWeight: 700, cursor: 'pointer', display: 'inline-flex', alignItems: 'center', justifyContent: 'center', gap: 6, padding: '0 16px' }
const installPrimaryButton: CSSProperties = { ...primaryButton, width: '100%' }
const secondaryButton: CSSProperties = { minHeight: 34, border: 'none', borderRadius: 8, backgroundColor: 'var(--bg-tertiary)', color: 'var(--text-primary)', fontSize: 12, fontWeight: 650, cursor: 'pointer', display: 'inline-flex', alignItems: 'center', justifyContent: 'center', gap: 6, padding: '0 12px' }
