import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { AlertTriangle, Check, ExternalLink, Link2, RefreshCw } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import type { PluginAppInfo, PluginEntry } from '../../stores/pluginStore'
import { useAppBindingStore, type AppInfo } from '../../stores/appBindingStore'
import { addToast } from '../../stores/toastStore'
import { PluginIcon, pluginSubtitle, pluginTitle } from './PluginCatalogItem'
import { openAppHandoff } from '../../utils/threadAppBindings'
import { getPluginContentSummaries } from '../../utils/pluginContentSummaries'
import { Button } from '../ui/Button'
import { DialogCloseButton } from '../ui/DialogCloseButton'
import { IdentityMark } from '../ui/IdentityMark'
import { ModalDialog } from '../ui/ModalDialog'
import { PluginContentIcon } from './PluginContentIcon'
import styles from './PluginInstallDialog.module.css'

type NativeStatus = 'installed' | 'missing' | 'unknown'
type SetupStage = 'pluginInstall' | 'trustRequired' | 'nativeAppRequired' | 'nativeAppPending' | 'appConnect' | 'handoffOpened' | 'complete'

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
  onTrust,
  onSetupComplete,
  onClose
}: {
  plugin: PluginEntry
  installing?: boolean
  onInstall: () => Promise<void> | void
  onTrust?: () => Promise<void> | void
  onSetupComplete?: () => void
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

  const needsTrust = plugin.dotnet != null && plugin.dotnetRuntime?.trustStatus !== 'trusted'

  const setupStage = useMemo<{ stage: SetupStage; apps: AppSetupState[] }>(() => {
    if (!plugin.installed) return { stage: 'pluginInstall', apps: [] }
    // Copying the bundle is not authority: an in-process plugin stays blocked until this step.
    if (needsTrust) return { stage: 'trustRequired', apps: [] }
    if (!hasApps) return { stage: 'complete', apps: [] }

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
        apps: setupApps
      }
    }

    return { stage: 'complete', apps: setupApps }
  }, [hasApps, needsTrust, plugin.installed, setupApps])

  const setupFinished = setupStage.stage === 'complete'
  const completionReported = useRef(false)
  useEffect(() => {
    if (!setupFinished || completionReported.current) return
    completionReported.current = true
    ;(onSetupComplete ?? onClose)()
  }, [onClose, onSetupComplete, setupFinished])

  const dialogTitle = plugin.installed && (hasApps || needsTrust)
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
      await openAppHandoff(result.handoff)
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

  return (
    <ModalDialog
      titleId="plugin-install-title"
      width={600}
      onDismiss={onClose}
      header={(
        <div className={styles.hero}>
          <span className={styles.close}>
            <DialogCloseButton label={t('common.close')} onClose={onClose} />
          </span>
          <div className={styles.logos}>
            <IdentityMark role="hero" size={56} src={dotharLogoUrl} fallback="D" />
            <span className={styles.dotTrail}>•••</span>
            <PluginIcon plugin={plugin} role="hero" size={56} />
          </div>
          <h2 id="plugin-install-title" className={styles.title}>{dialogTitle}</h2>
          <div className={styles.subtitle}>{t('plugins.developedBy', { developer: plugin.interface?.developerName || 'DotHarness' })}</div>
        </div>
      )}
      footer={(
        <div className={styles.actions}>
          {setupStage.stage === 'pluginInstall' ? (
            <Button size="prominent" variant="primary" onClick={() => { void onInstall() }} disabled={installing} loading={installing} className={styles.wide}>
              {installing ? t('plugins.installing') : t('plugins.installDialog.addToDotCraft')}
            </Button>
          ) : (
            <CurrentSetupStage
              stage={setupStage.stage}
              apps={setupStage.apps}
              trustName={title}
              busyKey={busyKey}
              onTrust={onTrust ? () => run('plugin:trust', async () => { await onTrust() }) : undefined}
              onConnect={handleConnect}
              onInstallNative={handleInstallNative}
              onRefresh={() => { void run('setup:refresh', refreshAppSetup) }}
              t={t}
            />
          )}
        </div>
      )}
    >
      <div className={styles.infoCard}>
        <div className={styles.cardTitle}>
          <strong>{title}</strong>
        </div>
        <div className={styles.muted}>{t('plugins.providedBy', { developer: plugin.interface?.developerName || 'DotHarness' })}</div>
        <div className={styles.muted}>{t('plugins.detail.category')}: {plugin.interface?.category || 'Coding'}</div>
        <Divider />
        <SectionTitle>{t('plugins.detail.about')}</SectionTitle>
        <p className={styles.description}>{plugin.interface?.longDescription || pluginSubtitle(plugin)}</p>
        <Divider />
        <SectionTitle>{t('plugins.detail.contents')}</SectionTitle>
        <ContentChips plugin={plugin} />
        {capabilities.length > 0 && (
          <>
            <Divider />
            <SectionTitle>{t('plugins.detail.capabilities')}</SectionTitle>
            <div className={styles.chips}>
              {capabilities.map((capability) => <span key={capability} className={styles.chip}>{capability}</span>)}
            </div>
          </>
        )}
      </div>
    </ModalDialog>
  )
}

function ContentChips({ plugin }: { plugin: PluginEntry }): JSX.Element {
  const t = useT()
  const contents = getPluginContentSummaries(plugin, t)
  if (contents.length === 0) {
    return (
      <div className={styles.chips}>
        <span className={styles.muted}>{t('plugins.detail.noContents')}</span>
      </div>
    )
  }

  return (
    <div className={styles.chips}>
      {contents.map((item) => (
        <span key={item.key} className={styles.chip}>
          <PluginContentIcon type={item.type} size={12} />
          <span>{item.type === 'desktopPlugin' || item.type === 'dotnet' || item.type === 'hooks' ? `${item.title} · ${item.kind}` : item.title}</span>
        </span>
      ))}
    </div>
  )
}

function CurrentSetupStage({
  stage,
  apps,
  trustName,
  busyKey,
  onConnect,
  onInstallNative,
  onRefresh,
  onTrust,
  t
}: {
  stage: SetupStage
  apps: AppSetupState[]
  trustName: string
  busyKey: string | null
  onConnect: (app: PluginAppInfo) => Promise<void>
  onInstallNative: (app: PluginAppInfo) => Promise<void>
  onRefresh: () => void
  onTrust?: () => void
  t: ReturnType<typeof useT>
}): JSX.Element | null {
  if (stage === 'trustRequired') {
    return (
      <SetupPanel
        tone="warning"
        title={t('plugins.installDialog.trustRequired')}
        status={t('plugins.installDialog.trustStep', { name: trustName })}
      >
        <Button
          size="sm"
          variant="primary"
          onClick={onTrust}
          disabled={!onTrust || busyKey === 'plugin:trust'}
          loading={busyKey === 'plugin:trust'}
          className={styles.trailing}
        >
          {t('plugins.installDialog.trustAction')}
        </Button>
      </SetupPanel>
    )
  }

  if (stage === 'complete') return null

  const nativeStage = stage === 'nativeAppRequired' || stage === 'nativeAppPending'
  const title = nativeStage
    ? t('plugins.installDialog.requiredApps')
    : t('plugins.installDialog.connectRequired')
  const status = nativeStage
    ? t('plugins.installDialog.nativeStep', { name: apps.map((setupApp) => setupApp.app.nativeApplication?.displayName || setupApp.app.displayName).join(', ') })
    : t('plugins.installDialog.connectStep', { name: apps.filter((setupApp) => !setupApp.connected).map((setupApp) => setupApp.app.displayName).join(', ') })

  return (
    <SetupPanel title={title} status={status}>
      <div className={styles.appList}>
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
  tone,
  title,
  status,
  details,
  children
}: {
  tone?: 'warning'
  title: string
  status: string
  details?: ReactNode
  children?: ReactNode
}): JSX.Element {
  return (
    <div className={styles.setupPanel} data-tone={tone}>
      <span className={styles.setupIcon}>
        {tone === 'warning'
          ? <AlertTriangle size={13} aria-hidden />
          : <Link2 size={13} aria-hidden />}
      </span>
      <span className={styles.stepBody}>
        <strong className={styles.stepTitle}>{title}</strong>
        <span className={styles.muted}>{status}</span>
        {details}
      </span>
      {children && <div className={styles.stepAction}>{children}</div>}
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
  const nativeStage = stage === 'nativeAppRequired' || stage === 'nativeAppPending'
  const connected = setupApp.connected && !nativeStage
  const waitingForApp = stage === 'handoffOpened' && handoffOpened
  const nativePending = nativeStatus !== 'missing' || nativeOpened
  return (
    <div className={styles.appRow}>
      <AppSetupIcon app={app} />
      <span className={styles.appBody}>
        <strong className={styles.stepTitle}>{app.displayName}</strong>
        {!connected && (
          <span className={styles.muted}>
            {stage === 'nativeAppRequired' || stage === 'nativeAppPending'
              ? nativePending ? t('plugins.installDialog.nativePending') : nativeStatusLabel(nativeStatus, t)
              : waitingForApp ? t('plugins.installDialog.waitingForApp') : connectionStatusLabel(liveApp, t)}
          </span>
        )}
      </span>
      <span className={styles.appActions}>
        {connected ? (
          <Button size="sm" disabled variant="secondary">
            <Check size={13} aria-hidden />
            {t('appBinding.connection.connected')}
          </Button>
        ) : stage === 'nativeAppRequired' || stage === 'nativeAppPending' ? (
          nativePending ? (
            <Button size="sm" onClick={onRefresh} disabled={busyKey != null} variant="secondary">
              <RefreshCw size={13} aria-hidden />
              {t('appBinding.refresh')}
            </Button>
          ) : (
            <Button size="sm" onClick={() => { void onInstallNative(app) }} disabled={busyKey != null} variant="primary">
              <ExternalLink size={13} aria-hidden />
              {t('appBinding.installNative')}
            </Button>
          )
        ) : waitingForApp ? (
          <>
            <Button size="sm" disabled variant="secondary">
              <Link2 size={13} aria-hidden />
              {t('plugins.installDialog.linkOpened')}
            </Button>
            <Button size="sm" onClick={onRefresh} disabled={busyKey != null} variant="secondary">
              <RefreshCw size={13} aria-hidden />
              {t('appBinding.refresh')}
            </Button>
            <Button size="sm" onClick={() => { void onConnect(app) }} disabled={busyKey != null} variant="secondary">
              <Link2 size={13} aria-hidden />
              {t('appBinding.connect')}
            </Button>
          </>
        ) : (
          <Button size="sm" onClick={() => { void onConnect(app) }} disabled={busyKey != null} variant="primary">
            <Link2 size={13} aria-hidden />
            {t('appBinding.connect')}
          </Button>
        )}
      </span>
    </div>
  )
}

function AppSetupIcon({ app }: { app: PluginAppInfo }): JSX.Element {
  return (
    <IdentityMark
      role="list"
      size={36}
      src={app.icon}
      fallback={<Link2 size={15} />}
      framed={!app.icon}
    />
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
  return <div className={styles.divider} />
}

function SectionTitle({ children }: { children: string }): JSX.Element {
  return <div className={styles.sectionTitle}>{children}</div>
}
