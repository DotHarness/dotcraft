import { useCallback, useEffect, useMemo, useState, type CSSProperties } from 'react'
import { ExternalLink, Link2, RotateCw, Unlink } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { useAppBindingStore, type AppHandoff, type AppInfo } from '../../stores/appBindingStore'
import { useConnectionStore } from '../../stores/connectionStore'
import { addToast } from '../../stores/toastStore'
import { Button } from '../ui/Button'
import { useConfirmDialog } from '../ui/ConfirmDialog'
import { Skeleton } from '../ui/Skeleton'
import { StatusMenuButton } from '../ui/StatusMenuButton'
import type { PluginEntry } from '../../stores/pluginStore'

interface AppBindingPanelProps {
  plugin: PluginEntry
}

export function AppBindingPanel({ plugin }: AppBindingPanelProps): JSX.Element | null {
  const t = useT()
  const confirm = useConfirmDialog()
  const canUseAppBinding = useConnectionStore((s) => s.capabilities?.appBindingVersion === 1)
  const {
    apps,
    appsLoading,
    appsError,
    fetchApps,
    startConnection,
    revokeConnection,
    waitForConnection
  } = useAppBindingStore()
  const [busyKey, setBusyKey] = useState<string | null>(null)

  const refreshPanel = useCallback(async (forceRefresh = false): Promise<void> => {
    await fetchApps(null, forceRefresh, 'pluginDetail')
  }, [fetchApps])

  useEffect(() => {
    if (!canUseAppBinding || !plugin.installed) return
    void refreshPanel(false).catch(() => undefined)
  }, [canUseAppBinding, plugin.installed, refreshPanel])

  const pluginApps = useMemo(
    () => apps.filter((app) => app.pluginId === plugin.id),
    [apps, plugin.id]
  )

  if (!plugin.installed || !canUseAppBinding || (!appsLoading && pluginApps.length === 0 && !appsError)) return null

  async function runAction(key: string, action: () => Promise<void>): Promise<void> {
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

  async function connect(app: AppInfo): Promise<void> {
    await runAction(`${app.appId}:connect`, async () => {
      const result = await startConnection(app.appId)
      await openAppHandoff(result.handoff, t)
      addToast(t('appBinding.connectStarted'), 'info')
      await waitForConnection(app.appId)
      addToast(t('appBinding.connection.connected'), 'success')
    })
  }

  async function installNative(app: AppInfo): Promise<void> {
    await runAction(`${app.appId}:installNative`, async () => {
      const url = app.nativeApp?.installUrl || app.releasePage || app.downloadUrl
      if (!url) throw new Error(t('appBinding.nativeInstallMissing'))
      await window.api.shell.openExternal(url)
    })
  }

  async function disconnect(app: AppInfo): Promise<void> {
    const accepted = await confirm({
      title: t('appBinding.disconnectConfirm.title', { name: app.displayName }),
      message: t('appBinding.disconnectConfirm.message', { name: app.displayName }),
      confirmLabel: t('appBinding.disconnect'),
      cancelLabel: t('common.cancel'),
      danger: true
    })
    if (!accepted) return
    await runAction(`${app.appId}:disconnect`, async () => {
      await revokeConnection(app.appId)
      addToast(t('appBinding.connectionRevoked'), 'success')
    })
  }

  return (
    <section style={section}>
      <h2 style={sectionTitle}>{t('appBinding.pluginTitle')}</h2>
      {appsLoading && (
        <div role="status" aria-busy="true" aria-label={t('appBinding.loading')} style={appList}>
          {Array.from({ length: 2 }, (_, index) => (
            <div key={index} style={appRow} aria-hidden="true">
              <div style={{ minWidth: 0 }}>
                <Skeleton width={index === 0 ? '42%' : '34%'} height={13} />
                <Skeleton width="78%" height={11} style={{ marginTop: 8 }} />
              </div>
              <Skeleton width={92} height={32} radius={8} />
            </div>
          ))}
        </div>
      )}
      {appsError && (
        <div style={errorRow} role="alert">
          <span>{appsError}</span>
          <Button size="sm" onClick={() => { void refreshPanel(true) }}>{t('common.retry')}</Button>
        </div>
      )}
      {!appsLoading && !appsError && (
        <div style={appList}>
          {pluginApps.map((app) => {
            const nativeMissing = app.nativeApp?.status === 'missing'
            const connecting = app.connectionState === 'connecting'
            const connected = app.connectionState === 'connected'
            const reconnect = app.connectionState === 'needsAuth' || app.connectionState === 'error'
            const appBusy = busyKey?.startsWith(`${app.appId}:`) === true
            return (
              <div
                key={app.appId}
                style={appRow}
              >
                <div style={appMain}>
                  <strong style={appTitle}>{app.displayName}</strong>
                  <p style={appDescription}>{app.description}</p>
                </div>
                <div style={actions}>
                  {nativeMissing ? (
                    <Button
                      variant="primary"
                      loading={busyKey === `${app.appId}:installNative`}
                      disabled={appBusy}
                      iconLeft={<ExternalLink size={13} />}
                      onClick={() => { void installNative(app) }}
                    >
                      {t('appBinding.installNative')}
                    </Button>
                  ) : connected ? (
                    <StatusMenuButton
                      label={t('appBinding.status.connected')}
                      tone="success"
                      disabled={appBusy}
                      items={[
                        {
                          label: t('appBinding.reconnect'),
                          icon: <RotateCw size={14} />,
                          onClick: () => { void connect(app) }
                        },
                        { type: 'separator' },
                        {
                          label: t('appBinding.disconnect'),
                          icon: <Unlink size={14} />,
                          danger: true,
                          onClick: () => { void disconnect(app) }
                        }
                      ]}
                    />
                  ) : connecting ? (
                    <Button loading disabled>{t('appBinding.status.connecting')}</Button>
                  ) : (
                    <Button
                      variant="primary"
                      loading={busyKey === `${app.appId}:connect`}
                      disabled={appBusy || !app.enabled || !app.installed}
                      iconLeft={<Link2 size={13} />}
                      onClick={() => { void connect(app) }}
                    >
                      {t(reconnect ? 'appBinding.reconnect' : 'appBinding.connect')}
                    </Button>
                  )}
                </div>
              </div>
            )
          })}
        </div>
      )}
    </section>
  )
}

export async function openAppHandoff(
  handoff: AppHandoff,
  _t: ReturnType<typeof useT>
): Promise<void> {
  if (!handoff.uri) return
  await (window.api.shell.openAppHandoff ?? window.api.shell.openExternal)(handoff.uri)
}

const section: CSSProperties = { marginTop: 28 }
const sectionTitle: CSSProperties = { margin: '0 0 12px', fontSize: 15, fontWeight: 600 }
const appList: CSSProperties = { display: 'flex', flexDirection: 'column' }
const appRow: CSSProperties = {
  display: 'grid',
  gridTemplateColumns: 'minmax(0, 1fr) auto',
  alignItems: 'center',
  gap: 16,
  minHeight: 66,
  padding: '10px 8px',
  borderBottom: '1px solid var(--border-subtle)',
  outline: 'none'
}
const appMain: CSSProperties = { minWidth: 0 }
const appTitle: CSSProperties = { fontSize: 13, color: 'var(--text-primary)' }
const appDescription: CSSProperties = { margin: '4px 0 0', color: 'var(--text-secondary)', fontSize: 12, lineHeight: 1.4 }
const actions: CSSProperties = { display: 'flex', alignItems: 'center', justifyContent: 'flex-end' }
const errorRow: CSSProperties = { display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 12, color: 'var(--error)', fontSize: 13 }
