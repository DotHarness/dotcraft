import { useCallback, useEffect, useMemo, useState, type CSSProperties } from 'react'
import { Link2, RefreshCw, ShieldAlert, ShieldCheck, Unlink } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { useAppBindingStore, type AppHandoff, type AppInfo } from '../../stores/appBindingStore'
import { useConnectionStore } from '../../stores/connectionStore'
import { useThreadStore } from '../../stores/threadStore'
import { addToast } from '../../stores/toastStore'
import { ActionTooltip } from '../ui/ActionTooltip'
import { useConfirmDialog } from '../ui/ConfirmDialog'
import { Skeleton } from '../ui/Skeleton'
import { CatalogHoverButton } from '../catalog/CatalogSurface'
import type { PluginEntry } from '../../stores/pluginStore'

interface AppBindingPanelProps {
  plugin: PluginEntry
}

export function AppBindingPanel({ plugin }: AppBindingPanelProps): JSX.Element | null {
  const t = useT()
  const confirm = useConfirmDialog()
  const canUseAppBinding = useConnectionStore((s) => s.capabilities?.appBindingVersion === 2)
  const activeThreadId = useThreadStore((s) => s.activeThreadId)
  const {
    apps,
    appsLoading,
    appsError,
    fetchApps,
    startConnection,
    revokeConnection,
    createBindingRequest,
    refreshThreadBindings,
    revokeThreadBinding,
    confirmCapabilities,
    waitForConnection,
    waitForThreadBinding
  } = useAppBindingStore()
  const [busyKey, setBusyKey] = useState<string | null>(null)
  const [handoffByAppId, setHandoffByAppId] = useState<Record<string, AppHandoff>>({})

  const refreshPanel = useCallback(async (forceRefresh = false): Promise<void> => {
    if (activeThreadId) {
      try {
        await refreshThreadBindings(activeThreadId)
      } finally {
        await fetchApps(activeThreadId, forceRefresh, 'pluginDetail')
      }
      return
    }

    await fetchApps(null, forceRefresh, 'pluginDetail')
  }, [activeThreadId, fetchApps, refreshThreadBindings])

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

  async function handleConnect(app: AppInfo): Promise<void> {
    await runAction(`${app.appId}:connect`, async () => {
      const result = await startConnection(app.appId)
      setHandoffByAppId((current) => ({ ...current, [app.appId]: result.handoff }))
      await openAppHandoff(result.handoff, t)
      addToast(t('appBinding.connectStarted'), 'info')
      await waitForConnection(app.appId)
      addToast(t('appBinding.connection.connected'), 'success')
    })
  }

  async function handleInstallNative(app: AppInfo): Promise<void> {
    await runAction(`${app.appId}:installNative`, async () => {
      const url = app.nativeApp?.installUrl || app.releasePage || app.downloadUrl
      if (!url) throw new Error(t('appBinding.nativeInstallMissing'))
      await window.api.shell.openExternal(url)
    })
  }

  async function handleOpenApp(app: AppInfo, bindingId?: string): Promise<void> {
    await runAction(`${app.appId}:openApp`, async () => {
      const result = await startConnection(app.appId)
      setHandoffByAppId((current) => ({ ...current, [app.appId]: result.handoff }))
      await openAppHandoff(result.handoff, t)
      addToast(t('appBinding.connectStarted'), 'info')
      await waitForConnection(app.appId)
      if (activeThreadId && bindingId) {
        await refreshThreadBindings(activeThreadId, bindingId)
      }
      await fetchApps(activeThreadId, true, 'pluginDetail')
    })
  }

  async function handleBind(app: AppInfo): Promise<void> {
    if (!activeThreadId) return
    await runAction(`${app.appId}:bind`, async () => {
      const result = await createBindingRequest({
        threadId: activeThreadId,
        appId: app.appId,
        source: 'pluginDetail'
      })
      setHandoffByAppId((current) => ({ ...current, [app.appId]: result.handoff }))
      await openAppHandoff(result.handoff, t)
      addToast(t('appBinding.bindingStarted'), 'info')
      await waitForThreadBinding({
        threadId: activeThreadId,
        appId: app.appId,
        bindingRequestId: result.bindingRequestId
      })
      addToast(t('appBinding.binding.activeToast'), 'success')
    })
  }

  return (
    <section style={section}>
      <div style={sectionHeader}>
        <h2 style={sectionTitle}>{t('appBinding.pluginTitle')}</h2>
        <ActionTooltip label={t('appBinding.refresh')}>
          <CatalogHoverButton
            type="button"
            baseStyle={iconButton}
            aria-label={t('appBinding.refresh')}
            onClick={() => { void runAction('setup:refresh', () => refreshPanel(true)) }}
          >
            <RefreshCw size={14} aria-hidden />
          </CatalogHoverButton>
        </ActionTooltip>
      </div>
      {appsLoading && (
        <div role="status" aria-busy="true" aria-label={t('appBinding.loading')} style={appList}>
          {Array.from({ length: 3 }, (_, index) => (
            <div key={index} style={appRow} aria-hidden="true">
              <div style={appMain}>
                <Skeleton width={index % 2 === 0 ? '42%' : '34%'} height={13} />
                <Skeleton width="92%" height={11} style={{ marginTop: 8 }} />
                <Skeleton width="68%" height={11} style={{ marginTop: 6 }} />
                <div style={{ display: 'flex', gap: 6, marginTop: 10 }}>
                  <Skeleton width={64} height={20} radius={999} />
                  <Skeleton width={52} height={20} radius={999} />
                  <Skeleton width={72} height={20} radius={999} />
                </div>
              </div>
              <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
                <Skeleton width={96} height={32} radius={8} />
              </div>
            </div>
          ))}
        </div>
      )}
      {appsError && <p style={errorText}>{appsError}</p>}
      <div style={appList}>
        {pluginApps.map((app) => {
          const binding = app.bindingSummary
          const connected = app.connectionState === 'connected'
          const bindingActive = binding?.state === 'active'
          const bindingOffline = binding?.state === 'offline'
          const handoff = handoffByAppId[app.appId]
          const nativeMissing = app.nativeApp?.status === 'missing'
          return (
            <div key={app.appId} style={appRow}>
              <div style={appMain}>
                <div style={appTitleRow}>
                  <strong style={appTitle}>{app.displayName}</strong>
                  <span style={statePill(app.connectionState === 'connected')}>
                    {connectionStateLabel(app.connectionState, t)}
                  </span>
                  {binding && (
                    <span style={statePill(binding.state === 'active')}>
                      {bindingStateLabel(binding.state, t)}
                    </span>
                  )}
                </div>
                <p style={appDescription}>{app.description}</p>
                {handoff && <HandoffHint handoff={handoff} t={t} />}
                {bindingOffline && (
                  <div style={{ marginTop: 8 }}>
                    <div style={mutedText}>{t('appBinding.approvedCapabilities')}</div>
                    {(binding?.approvedTools ?? []).length > 0
                      ? (binding?.approvedTools ?? []).map((tool, index) => (
                          <div key={`${String(tool.namespace)}:${String(tool.name)}:${index}`} style={mutedText}>
                            {String(tool.namespace)}.{String(tool.name)}
                          </div>
                        ))
                      : <div style={mutedText}>{t('appBinding.noApprovedCapabilities')}</div>}
                  </div>
                )}
                {activeThreadId && binding && binding.state === 'needsConfirmation' && binding.candidateCapabilityRevision != null && (
                  <div style={capabilityBlock} role="group" aria-label={t('appBinding.capabilityExpansion')}>
                    <div style={capabilityHead}>
                      <ShieldAlert size={14} aria-hidden style={{ color: 'var(--warning)', flexShrink: 0 }} />
                      <span style={capabilityTitle}>{t('appBinding.capabilityExpansion')}</span>
                    </div>
                    {(binding.pendingChanges ?? []).length > 0 && (
                      <div style={capabilityChanges}>
                        {(binding.pendingChanges ?? []).map((change) => (
                          <div key={`${change.kind}:${change.tool}`} style={capabilityChange}>
                            <span aria-hidden style={{ color: change.kind === 'removed' ? 'var(--error)' : 'var(--success)', flexShrink: 0 }}>
                              {change.kind === 'removed' ? '−' : '+'}
                            </span>
                            <span>
                              <span style={{ color: 'var(--text-primary)', fontWeight: 500 }}>{change.tool}</span>
                              {change.detail ? ` · ${change.detail}` : ''}
                            </span>
                          </div>
                        ))}
                      </div>
                    )}
                    <div style={capabilityActions}>
                      <button type="button" style={smallPrimaryButton} onClick={() => { void confirmCapabilities(activeThreadId, binding.bindingId, binding.candidateCapabilityRevision!, 'accept') }}>
                        {t('appBinding.acceptCapabilities')}
                      </button>
                      <button type="button" style={smallGhostButton} onClick={() => { void confirmCapabilities(activeThreadId, binding.bindingId, binding.candidateCapabilityRevision!, 'reject') }}>
                        {t('appBinding.rejectCapabilities')}
                      </button>
                    </div>
                  </div>
                )}
              </div>
              <div style={actions}>
                {bindingOffline ? (
                  <button
                    type="button"
                    style={primaryButton}
                    disabled={busyKey === `${app.appId}:openApp`}
                    onClick={() => { void handleOpenApp(app, binding?.bindingId) }}
                  >
                    <Link2 size={13} aria-hidden />
                    {t('appBinding.openApp')}
                  </button>
                ) : connected ? (
                  <button
                    type="button"
                    style={secondaryButton}
                    disabled={busyKey === `${app.appId}:revokeConnection`}
                    onClick={() => {
                      void runAction(`${app.appId}:revokeConnection`, async () => {
                        await revokeConnection(app.appId)
                        addToast(t('appBinding.connectionRevoked'), 'success')
                      })
                    }}
                  >
                    <Unlink size={13} aria-hidden />
                    {t('appBinding.disconnect')}
                  </button>
                ) : nativeMissing ? (
                  <button
                    type="button"
                    style={primaryButton}
                    disabled={busyKey === `${app.appId}:installNative`}
                    onClick={() => { void handleInstallNative(app) }}
                  >
                    <Link2 size={13} aria-hidden />
                    {t('appBinding.installNative')}
                  </button>
                ) : (
                  <button
                    type="button"
                    style={primaryButton}
                    disabled={busyKey === `${app.appId}:connect` || !app.enabled || !app.installed}
                    onClick={() => { void handleConnect(app) }}
                  >
                    <Link2 size={13} aria-hidden />
                    {app.connectionState === 'needsAuth' ? t('appBinding.reconnect') : t('appBinding.connect')}
                  </button>
                )}
                {bindingOffline && connected && (
                  <button
                    type="button"
                    style={secondaryButton}
                    disabled={busyKey === `${app.appId}:revokeConnection`}
                    onClick={() => {
                      void runAction(`${app.appId}:revokeConnection`, async () => {
                        await revokeConnection(app.appId)
                        addToast(t('appBinding.connectionRevoked'), 'success')
                      })
                    }}
                  >
                    <Unlink size={13} aria-hidden />
                    {t('appBinding.disconnect')}
                  </button>
                )}
                {connected && activeThreadId && !binding && !bindingActive && (
                  <button
                    type="button"
                    style={primaryButton}
                    disabled={busyKey === `${app.appId}:bind`}
                    onClick={() => { void handleBind(app) }}
                  >
                    <ShieldCheck size={13} aria-hidden />
                    {t('appBinding.bindThread')}
                  </button>
                )}
                {activeThreadId && binding && (
                  <>
                    <button
                      type="button"
                      style={secondaryButton}
                      disabled={busyKey === `${app.appId}:refreshBinding`}
                      onClick={() => {
                        void runAction(`${app.appId}:refreshBinding`, async () => {
                          await refreshThreadBindings(activeThreadId, binding.bindingId)
                          addToast(t('appBinding.bindingRefreshed'), 'success')
                        })
                      }}
                    >
                      <RefreshCw size={13} aria-hidden />
                      {t('appBinding.refresh')}
                    </button>
                    <button
                      type="button"
                      style={secondaryButton}
                      disabled={busyKey === `${app.appId}:revokeBinding`}
                      onClick={() => {
                        void runAction(`${app.appId}:revokeBinding`, async () => {
                          await revokeThreadBinding(activeThreadId, binding.bindingId)
                          addToast(t('appBinding.bindingRevoked'), 'success')
                        })
                      }}
                    >
                      <Unlink size={13} aria-hidden />
                      {t('appBinding.revoke')}
                    </button>
                  </>
                )}
              </div>
            </div>
          )
        })}
      </div>
    </section>
  )
}

function HandoffHint({
  handoff,
  t
}: {
  handoff: AppHandoff
  t: ReturnType<typeof useT>
}): JSX.Element | null {
  const value = handoff.uri
  if (!value) return null

  return (
    <ActionTooltip
      label={value}
      wrapperStyle={{ display: 'block', minWidth: 0, overflow: 'hidden', flexShrink: 1 }}
    >
      <div style={{ ...handoffBox, display: 'block' }}>
        <div style={handoffHint}>{t('appBinding.handoffOpening')}</div>
      </div>
    </ActionTooltip>
  )
}

export async function openAppHandoff(
  handoff: AppHandoff,
  t: ReturnType<typeof useT>
): Promise<void> {
  if (!handoff.uri) return
  try {
    await (window.api.shell.openAppHandoff ?? window.api.shell.openExternal)(handoff.uri)
  } catch {
    addToast(t('appBinding.handoffReady'), 'info')
  }
}

function connectionStateLabel(state: string, t: ReturnType<typeof useT>): string {
  if (state === 'connected') return t('appBinding.connection.connected')
  if (state === 'connecting') return t('appBinding.connection.connecting')
  if (state === 'needsAuth') return t('appBinding.connection.needsAuth')
  if (state === 'error') return t('appBinding.connection.error')
  return t('appBinding.connection.notConnected')
}

function bindingStateLabel(state: string, t: ReturnType<typeof useT>): string {
  if (state === 'active') return t('appBinding.binding.active')
  if (state === 'offline') return t('appBinding.binding.offline')
  if (state === 'needsConfirmation') return t('appBinding.capabilityExpansion')
  if (state === 'revoked') return t('appBinding.binding.revoked')
  if (state === 'failed') return t('appBinding.binding.error')
  return t('appBinding.binding.pending')
}

const section: CSSProperties = { marginTop: 28 }
const sectionHeader: CSSProperties = { display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 10, marginBottom: 12 }
const sectionTitle: CSSProperties = { margin: 0, fontSize: 15, fontWeight: 600 }
const appList: CSSProperties = { display: 'flex', flexDirection: 'column', gap: 10 }
const appRow: CSSProperties = { display: 'grid', gridTemplateColumns: 'minmax(0, 1fr) auto', gap: 12, border: '1px solid var(--border-default)', borderRadius: 8, padding: 12, background: 'var(--bg-secondary)' }
const appMain: CSSProperties = { minWidth: 0 }
const appTitleRow: CSSProperties = { display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }
const appTitle: CSSProperties = { fontSize: 13, color: 'var(--text-primary)' }
const appDescription: CSSProperties = { margin: '6px 0 0', color: 'var(--text-secondary)', fontSize: 12, lineHeight: 1.45 }
const mutedText: CSSProperties = { marginTop: 4, color: 'var(--text-secondary)', fontSize: 11, lineHeight: 1.4 }
const handoffBox: CSSProperties = { marginTop: 8, display: 'flex', flexDirection: 'column', gap: 3, minWidth: 0 }
const handoffHint: CSSProperties = { marginTop: 8, fontFamily: 'var(--font-mono)', fontSize: 11, color: 'var(--text-tertiary)', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }
const actions: CSSProperties = { display: 'flex', alignItems: 'flex-start', justifyContent: 'flex-end', flexWrap: 'wrap', gap: 8, maxWidth: 280 }
const baseButton: CSSProperties = { border: 'none', borderRadius: 8, height: 32, padding: '0 12px', boxSizing: 'border-box', lineHeight: 1, cursor: 'pointer', display: 'inline-flex', alignItems: 'center', justifyContent: 'center', gap: 6, fontSize: 13, fontWeight: 600 }
const primaryButton: CSSProperties = { ...baseButton, background: 'var(--text-primary)', color: 'var(--bg-primary)', border: '1px solid var(--text-primary)' }
const secondaryButton: CSSProperties = { ...baseButton, background: 'var(--bg-tertiary)', color: 'var(--text-primary)' }
const capabilityBlock: CSSProperties = {
  width: '100%',
  marginTop: 8,
  padding: '10px 12px',
  borderRadius: 8,
  border: '1px solid color-mix(in srgb, var(--warning) 40%, var(--border-default))',
  background: 'var(--bg-tertiary)',
  display: 'flex',
  flexDirection: 'column',
  gap: 8
}
const capabilityHead: CSSProperties = { display: 'flex', alignItems: 'center', gap: 7 }
const capabilityTitle: CSSProperties = { fontSize: 12, fontWeight: 600, color: 'var(--text-primary)' }
const capabilityChanges: CSSProperties = { display: 'flex', flexDirection: 'column', gap: 3 }
const capabilityChange: CSSProperties = { display: 'flex', gap: 6, fontSize: 11.5, color: 'var(--text-secondary)', fontFamily: 'var(--font-mono)' }
const capabilityActions: CSSProperties = { display: 'flex', gap: 8, flexWrap: 'wrap', marginTop: 2 }
const smallButton: CSSProperties = { ...baseButton, height: 'auto', borderRadius: 6, padding: '5px 10px', fontSize: 11.5 }
const smallPrimaryButton: CSSProperties = { ...smallButton, background: 'var(--text-primary)', color: 'var(--bg-primary)', border: '1px solid var(--text-primary)' }
const smallGhostButton: CSSProperties = { ...smallButton, background: 'transparent', color: 'var(--text-secondary)', border: '1px solid var(--border-default)', fontWeight: 500 }
const iconButton: CSSProperties = { width: 32, height: 32, border: 'none', borderRadius: 8, background: 'transparent', color: 'var(--text-secondary)', cursor: 'pointer', display: 'inline-flex', alignItems: 'center', justifyContent: 'center' }
const errorText: CSSProperties = { margin: 0, color: 'var(--error)', fontSize: 13 }

function statePill(good: boolean): CSSProperties {
  return {
    borderRadius: 999,
    padding: '3px 7px',
    fontSize: 11,
    background: good ? 'rgba(22, 163, 74, 0.12)' : 'var(--bg-tertiary)',
    color: good ? 'var(--success, #15803d)' : 'var(--text-secondary)'
  }
}
