import { useCallback, useEffect, useMemo, useRef, useState, type CSSProperties } from 'react'
import { ExternalLink, Link2, RefreshCw, ShieldCheck, Unlink } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import {
  useAppBindingStore,
  type AppInfo,
  type ThreadAppBinding,
  type ThreadAppBindingSummary
} from '../../stores/appBindingStore'
import { useConnectionStore } from '../../stores/connectionStore'
import { addToast } from '../../stores/toastStore'
import { ActionTooltip } from '../ui/ActionTooltip'
import { ChannelIconBadge } from '../ui/channelMeta'
import { openAppHandoff } from '../plugins/AppBindingPanel'

interface ThreadAppBindingsButtonProps {
  threadId: string
}

const EMPTY_THREAD_APP_BINDINGS: ThreadAppBinding[] = []
const EMPTY_THREAD_APPS: AppInfo[] = []
type ThreadBindingLike = ThreadAppBinding | ThreadAppBindingSummary

interface ThreadAppRowModel {
  key: string
  app?: AppInfo
  binding?: ThreadBindingLike
  pendingHandoff?: PendingSocialHandoff
}

interface PendingSocialHandoff {
  appId: string
  bindingRequestId: string
  bindCode: string
  instructions?: string | null
}

export function ThreadAppBindingsButton({ threadId }: ThreadAppBindingsButtonProps): JSX.Element | null {
  const t = useT()
  const canUseAppBinding = useConnectionStore((s) => s.capabilities?.appBinding === true)
  const bindings = useAppBindingStore((s) => s.bindingsByThread[threadId] ?? EMPTY_THREAD_APP_BINDINGS)
  const loading = useAppBindingStore((s) => s.bindingsLoadingByThread[threadId] === true)
  const error = useAppBindingStore((s) => s.bindingsErrorByThread[threadId] ?? null)
  const apps = useAppBindingStore((s) =>
    s.appsThreadId === threadId && s.appsSurface === 'threadBinding' ? s.apps : EMPTY_THREAD_APPS
  )
  const appsLoading = useAppBindingStore((s) =>
    s.appsThreadId === threadId && s.appsSurface === 'threadBinding' && s.appsLoading
  )
  const appsError = useAppBindingStore((s) =>
    s.appsThreadId === threadId && s.appsSurface === 'threadBinding' ? s.appsError : null
  )
  const fetchApps = useAppBindingStore((s) => s.fetchApps)
  const fetchThreadBindings = useAppBindingStore((s) => s.fetchThreadBindings)
  const refreshThreadBindings = useAppBindingStore((s) => s.refreshThreadBindings)
  const cancelBindingRequest = useAppBindingStore((s) => s.cancelBindingRequest)
  const revokeThreadBinding = useAppBindingStore((s) => s.revokeThreadBinding)
  const startConnection = useAppBindingStore((s) => s.startConnection)
  const waitForConnection = useAppBindingStore((s) => s.waitForConnection)
  const createBindingRequest = useAppBindingStore((s) => s.createBindingRequest)
  const waitForThreadBinding = useAppBindingStore((s) => s.waitForThreadBinding)
  const [open, setOpen] = useState(false)
  const [busyKey, setBusyKey] = useState<string | null>(null)
  const [pendingSocialHandoffs, setPendingSocialHandoffs] = useState<Record<string, PendingSocialHandoff>>({})
  const rootRef = useRef<HTMLDivElement>(null)

  const refreshThreadAppPicker = useCallback(async (forceRefresh = false): Promise<void> => {
    try {
      await refreshThreadBindings(threadId)
    } catch {
      await fetchThreadBindings(threadId)
    }
    await fetchApps(threadId, forceRefresh, 'threadBinding')
  }, [fetchApps, fetchThreadBindings, refreshThreadBindings, threadId])

  useEffect(() => {
    if (!canUseAppBinding) return
    void refreshThreadBindings(threadId).catch(() => {
      void fetchThreadBindings(threadId)
    })
  }, [canUseAppBinding, fetchThreadBindings, refreshThreadBindings, threadId])

  useEffect(() => {
    if (!open) return
    const handlePointerDown = (event: PointerEvent): void => {
      if (rootRef.current?.contains(event.target as Node)) return
      setOpen(false)
    }
    const handleKeyDown = (event: KeyboardEvent): void => {
      if (event.key === 'Escape') setOpen(false)
    }
    window.addEventListener('pointerdown', handlePointerDown)
    window.addEventListener('keydown', handleKeyDown)
    return () => {
      window.removeEventListener('pointerdown', handlePointerDown)
      window.removeEventListener('keydown', handleKeyDown)
    }
  }, [open])

  useEffect(() => {
    if (!open || !canUseAppBinding) return
    void refreshThreadAppPicker(false)
  }, [canUseAppBinding, open, refreshThreadAppPicker])

  useEffect(() => {
    setPendingSocialHandoffs((current) => prunePendingSocialHandoffs(current, bindings, apps))
  }, [apps, bindings])

  const threadBindings = useMemo(
    () => bindings.filter((binding) => binding.state !== 'revoked'
      && binding.state !== 'cancelled'),
    [bindings]
  )

  const threadApps = useMemo(
    () => apps
      .filter((app) => app.installed && app.enabled)
      .sort((a, b) => a.displayName.localeCompare(b.displayName)),
    [apps]
  )

  const rows = useMemo<ThreadAppRowModel[]>(() => {
    const bindingsById = new Map(threadBindings.map((binding) => [binding.bindingId, binding]))
    const unmatchedBindings = new Map(threadBindings.map((binding) => [binding.bindingId, binding]))
    const nextRows: ThreadAppRowModel[] = []

    for (const app of threadApps) {
      const binding = app.bindingSummary
        ? bindingsById.get(app.bindingSummary.bindingId) ?? app.bindingSummary
        : threadBindings.find((candidate) => candidate.appId === app.appId)
      if (binding) unmatchedBindings.delete(binding.bindingId)
      const pendingHandoff = resolvePendingSocialHandoff(pendingSocialHandoffs, binding)
      if (isUnavailableSocialAppRow(app, binding, pendingHandoff)) continue
      nextRows.push({
        key: `app:${app.appId}`,
        app,
        binding,
        pendingHandoff
      })
    }

    for (const binding of unmatchedBindings.values()) {
      nextRows.push({
        key: `binding:${binding.bindingId}`,
        binding,
        pendingHandoff: resolvePendingSocialHandoff(pendingSocialHandoffs, binding)
      })
    }

    return nextRows
  }, [pendingSocialHandoffs, threadApps, threadBindings])

  const activeBindingCount = useMemo(
    () => rows.filter((row) => row.binding != null && row.binding.state !== 'revoked' && row.binding.state !== 'cancelled').length,
    [rows]
  )
  const pickerLoading = loading || appsLoading

  if (!canUseAppBinding) return null

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

  async function connectApp(app: AppInfo): Promise<void> {
    const result = await startConnection(app.appId)
    await openAppHandoff(result.handoff, t)
    addToast(t('appBinding.connectStarted'), 'info')
    await waitForConnection(app.appId)
    await fetchApps(threadId, true, 'threadBinding')
    addToast(t('appBinding.connection.connected'), 'success')
  }

  async function installNativeApp(app: AppInfo): Promise<void> {
    const url = app.nativeApp?.installUrl || app.releasePage || app.downloadUrl
    if (!url) throw new Error(t('appBinding.nativeInstallMissing'))
    await window.api.shell.openExternal(url)
  }

  async function bindApp(app: AppInfo): Promise<void> {
    const socialChannelName = getSocialChannelName(app)
    if ((socialChannelName || app.requiresExternalConnection !== false) && app.connectionState !== 'connected') {
      throw new Error(t('appBinding.welcomeAppNotConnected', { name: app.displayName || app.appId }))
    }

    const result = await createBindingRequest({
      threadId,
      appId: app.appId,
      requestedScopes: defaultRequestedScopes(app),
      requestedTools: requestedToolsForBinding(app),
      source: 'threadMenu',
      ...(socialChannelName
        ? {
            bindingKind: 'socialChannel',
            socialIntent: {
              channelName: socialChannelName,
              targetSelection: 'confirmInChannel',
              displayHint: app.displayName || socialChannelName
            }
          }
        : {})
    })
    if (result.handoff?.uri) await openAppHandoff(result.handoff, t)
    if (result.handoff?.bindCode) {
      await fetchThreadBindings(threadId)
      await fetchApps(threadId, true, 'threadBinding')
      const state = useAppBindingStore.getState()
      const currentBindings = state.bindingsByThread[threadId] ?? EMPTY_THREAD_APP_BINDINGS
      const currentApps = state.appsThreadId === threadId && state.appsSurface === 'threadBinding'
        ? state.apps
        : EMPTY_THREAD_APPS
      if (hasPendingSocialBindingRequest(currentBindings, currentApps, result.bindingRequestId)) {
        setPendingSocialHandoffs((current) => ({
          ...current,
          [result.bindingRequestId]: {
            appId: app.appId,
            bindingRequestId: result.bindingRequestId,
            bindCode: result.handoff!.bindCode!,
            instructions: result.handoff!.instructions
          }
        }))
      }
      return
    }
    if (result.state !== 'active') addToast(t('appBinding.bindingStarted'), 'info')
    await waitForThreadBinding({
      threadId,
      appId: app.appId,
      bindingRequestId: result.bindingRequestId
    })
    await fetchThreadBindings(threadId)
    await fetchApps(threadId, true, 'threadBinding')
    addToast(t('appBinding.binding.activeToast'), 'success')
  }

  async function openAppForBinding(binding: ThreadBindingLike): Promise<void> {
    const result = await startConnection(binding.appId)
    await openAppHandoff(result.handoff, t)
    addToast(t('appBinding.connectStarted'), 'info')
    await waitForConnection(binding.appId)
    await refreshThreadBindings(threadId, binding.bindingId)
    await fetchApps(threadId, true, 'threadBinding')
    addToast(t('appBinding.bindingRefreshed'), 'success')
  }

  return (
    <div ref={rootRef} style={root}>
      <ActionTooltip label={t('appBinding.title')} placement="bottom">
        <button
          type="button"
          aria-label={t('appBinding.title')}
          style={buttonStyle}
          onClick={() => setOpen((value) => !value)}
        >
          <Link2 size={15} aria-hidden />
          {activeBindingCount > 0 && <span style={countBadge}>{activeBindingCount}</span>}
        </button>
      </ActionTooltip>
      {open && (
        <div style={popover} role="dialog" aria-label={t('appBinding.title')}>
          <div style={popoverHeader}>
            <div style={popoverHeaderTitle}>
              <strong style={popoverTitle}>{t('appBinding.title')}</strong>
            </div>
            <button
              type="button"
              style={iconButton}
              aria-label={t('appBinding.refresh')}
              disabled={busyKey != null}
              onClick={() => { void runAction('refresh:all', () => refreshThreadAppPicker(true)) }}
            >
              <RefreshCw size={13} aria-hidden />
            </button>
          </div>
          {(error || appsError) && <div style={errorText}>{error || appsError}</div>}
          {!pickerLoading && rows.length === 0 && <div style={mutedText}>{t('appBinding.threadEmpty')}</div>}
          <div style={bindingList}>
            {rows.map((row) => (
              <ThreadAppRow
                key={row.key}
                app={row.app}
                binding={row.binding}
                pendingHandoff={row.pendingHandoff}
                busy={busyKey != null}
                onConnect={() => {
                  if (!row.app) return
                  void runAction(`connect:${row.app.appId}`, () => connectApp(row.app!))
                }}
                onInstall={() => {
                  if (!row.app) return
                  void runAction(`install:${row.app.appId}`, () => installNativeApp(row.app!))
                }}
                onBind={() => {
                  if (!row.app) return
                  void runAction(`bind:${row.app.appId}`, () => bindApp(row.app!))
                }}
                onRefresh={() => {
                  if (!row.binding) return
                  void runAction(`refresh:${row.binding.bindingId}`, async () => {
                    await refreshThreadBindings(threadId, row.binding!.bindingId)
                    await fetchApps(threadId, true, 'threadBinding')
                    addToast(t('appBinding.bindingRefreshed'), 'success')
                  })
                }}
                onOpenApp={() => {
                  if (!row.binding) return
                  void runAction(`open:${row.binding.bindingId}`, async () => {
                    await openAppForBinding(row.binding!)
                  })
                }}
                onRevoke={() => {
                  if (!row.binding) return
                  void runAction(`revoke:${row.binding.bindingId}`, async () => {
                    const binding = row.binding!
                    const isPending = binding.state === 'pending'
                    if (binding.state === 'pending') {
                      const bindingRequestId = 'bindingRequestId' in binding
                        ? binding.bindingRequestId || binding.bindingId
                        : binding.bindingId
                      await cancelBindingRequest(threadId, bindingRequestId)
                      setPendingSocialHandoffs((current) => {
                        const next = { ...current }
                        delete next[bindingRequestId]
                        return next
                      })
                    } else {
                      await revokeThreadBinding(threadId, binding.bindingId)
                    }
                    await fetchApps(threadId, true, 'threadBinding')
                    addToast(t(isPending ? 'appBinding.bindingRequestCancelled' : 'appBinding.bindingRevoked'), 'success')
                  })
                }}
              />
            ))}
          </div>
        </div>
      )}
    </div>
  )
}

function ThreadAppRow({
  app,
  binding,
  pendingHandoff,
  busy,
  onConnect,
  onInstall,
  onBind,
  onRefresh,
  onOpenApp,
  onRevoke
}: {
  app?: AppInfo
  binding?: ThreadBindingLike
  pendingHandoff?: PendingSocialHandoff
  busy: boolean
  onConnect: () => void
  onInstall: () => void
  onBind: () => void
  onRefresh: () => void
  onOpenApp: () => void
  onRevoke: () => void
}): JSX.Element {
  const t = useT()
  const displayName = app?.displayName || binding?.displayName || binding?.appId || ''
  const socialTargetLabel = binding?.socialTarget ? formatSocialTarget(binding.socialTarget) : null
  const pendingInstruction = pendingHandoff
    ? pendingHandoff.instructions?.trim() || `/bind ${pendingHandoff.bindCode}`
    : null
  const pendingLabel = binding?.state === 'pending' && !pendingInstruction ? t('appBinding.handoffOpening') : null
  const socialChannelName = app
    ? getSocialChannelName(app)
    : binding?.appId
      ? getSocialChannelNameFromAppId(binding.appId)
      : null
  const icon = app?.icon || binding?.icon
  const connectionState = binding?.connectionState || app?.connectionState || 'notConnected'
  const requiresExternalConnection = binding?.requiresExternalConnection ?? app?.requiresExternalConnection ?? true
  const nativeMissing = app?.nativeApp?.status === 'missing'
  const canOpenExternalApp = binding?.state === 'offline' && requiresExternalConnection !== false
  const activeSocialBindingDisconnected = binding?.state === 'active'
    && socialChannelName != null
    && connectionState !== 'connected'
  const isPendingBinding = binding?.state === 'pending'
  const canBind = app != null
    && binding == null
    && pendingHandoff == null
    && (socialChannelName
      ? app.connectionState === 'connected'
      : requiresExternalConnection === false || app.connectionState === 'connected')
  const canConnect = app != null
    && binding == null
    && requiresExternalConnection !== false
    && app.connectionState !== 'connected'
    && !nativeMissing
  const statusLabel = binding
    ? activeSocialBindingDisconnected
      ? t('appBinding.channel.notConnected')
      : bindingStateLabel(binding.state, t)
    : pendingHandoff
      ? bindingStateLabel('pending', t)
      : socialChannelName && connectionState === 'notConnected'
        ? t('appBinding.channel.notConnected')
        : connectionStateLabel(connectionState, t)
  const statusGood = binding
    ? binding.state === 'active' && !activeSocialBindingDisconnected
    : pendingHandoff
      ? false
      : connectionState === 'connected'
  const showStatusPill = !canOpenExternalApp
  return (
    <div style={bindingRow}>
      <AppIcon icon={icon} channelName={socialChannelName} label={displayName} />
      <div style={bindingMain}>
        <div style={bindingTitleRow}>
          <strong style={bindingTitle}>{displayName}</strong>
          {showStatusPill && <span style={statePill(statusGood)}>{statusLabel}</span>}
        </div>
        {socialTargetLabel && <div style={bindingSubtitle}>{socialTargetLabel}</div>}
        {pendingInstruction && <div style={bindingInstruction}>{pendingInstruction}</div>}
        {pendingLabel && <div style={bindingSubtitle}>{pendingLabel}</div>}
      </div>
      <div style={bindingActions}>
        {app && !binding && nativeMissing && (
          <button type="button" style={secondaryButton} disabled={busy} onClick={onInstall}>
            <ExternalLink size={13} aria-hidden />
            {t('appBinding.installNative')}
          </button>
        )}
        {canConnect && (
          <button type="button" style={primaryButton} disabled={busy} onClick={onConnect}>
            <Link2 size={13} aria-hidden />
            {app?.connectionState === 'needsAuth' ? t('appBinding.reconnect') : t('appBinding.connect')}
          </button>
        )}
        {canBind && (
          <button type="button" style={primaryButton} disabled={busy} onClick={onBind}>
            <ShieldCheck size={13} aria-hidden />
            {t('appBinding.bindThread')}
          </button>
        )}
        {canOpenExternalApp && (
          <button type="button" style={primaryButton} disabled={busy} onClick={onOpenApp}>
            <Link2 size={13} aria-hidden />
            {t('appBinding.openApp')}
          </button>
        )}
        {binding && (
          <>
            {!isPendingBinding && (
              <button type="button" style={iconButton} disabled={busy} aria-label={t('appBinding.refresh')} onClick={onRefresh}>
                <RefreshCw size={13} aria-hidden />
              </button>
            )}
            <button type="button" style={iconButton} disabled={busy} aria-label={binding.state === 'pending' ? t('common.cancel') : t('appBinding.revoke')} onClick={onRevoke}>
              <Unlink size={13} aria-hidden />
            </button>
          </>
        )}
      </div>
    </div>
  )
}

function AppIcon({
  icon,
  channelName,
  label
}: {
  icon?: string | null
  channelName?: string | null
  label?: string
}): JSX.Element {
  if (icon) {
    return <img src={icon} alt="" style={bindingIconImg} />
  }
  if (channelName) {
    return <ChannelIconBadge channelName={channelName} tooltip={label || channelName} size={30} />
  }
  return (
    <span style={bindingIconFallback} aria-hidden>
      <Link2 size={15} />
    </span>
  )
}

function bindingStateLabel(state: string, t: ReturnType<typeof useT>): string {
  if (state === 'active') return t('appBinding.binding.active')
  if (state === 'offline') return t('appBinding.binding.offline')
  if (state === 'expired') return t('appBinding.binding.expired')
  if (state === 'revoked') return t('appBinding.binding.revoked')
  if (state === 'error') return t('appBinding.binding.error')
  return t('appBinding.binding.pending')
}

function connectionStateLabel(state: string, t: ReturnType<typeof useT>): string {
  if (state === 'connected') return t('appBinding.connection.connected')
  if (state === 'connecting') return t('appBinding.connection.connecting')
  if (state === 'needsAuth') return t('appBinding.connection.needsAuth')
  if (state === 'error') return t('appBinding.connection.error')
  return t('appBinding.connection.notConnected')
}

function defaultRequestedScopes(app: AppInfo): string[] {
  return app.scopes.map((scope) => scope.id)
}

function requestedToolsForBinding(app: AppInfo): string[] | undefined {
  return app.dynamicToolCatalog?.enabled === true
    ? undefined
    : app.toolCatalog.map((tool) => tool.name)
}

function getSocialChannelName(app: AppInfo): string | null {
  const prefix = 'com.dotharness.channel.'
  if (app.appId.startsWith(prefix)) return app.appId.slice(prefix.length)
  return null
}

function resolvePendingSocialHandoff(
  pendingHandoffs: Record<string, PendingSocialHandoff>,
  binding?: ThreadBindingLike
): PendingSocialHandoff | undefined {
  if (binding?.state !== 'pending') return undefined
  const bindingRequestId = 'bindingRequestId' in binding ? binding.bindingRequestId : undefined
  if (bindingRequestId && pendingHandoffs[bindingRequestId]) {
    return pendingHandoffs[bindingRequestId]
  }
  return undefined
}

function prunePendingSocialHandoffs(
  handoffs: Record<string, PendingSocialHandoff>,
  bindings: readonly ThreadAppBinding[],
  apps: readonly AppInfo[]
): Record<string, PendingSocialHandoff> {
  const keys = Object.keys(handoffs)
  if (keys.length === 0) return handoffs

  const pendingRequestIds = collectPendingSocialBindingRequestIds(bindings, apps)
  let changed = false
  const next: Record<string, PendingSocialHandoff> = {}
  for (const key of keys) {
    if (pendingRequestIds.has(key)) {
      next[key] = handoffs[key]
    } else {
      changed = true
    }
  }
  return changed ? next : handoffs
}

function hasPendingSocialBindingRequest(
  bindings: readonly ThreadAppBinding[],
  apps: readonly AppInfo[],
  bindingRequestId: string
): boolean {
  return collectPendingSocialBindingRequestIds(bindings, apps).has(bindingRequestId)
}

function collectPendingSocialBindingRequestIds(
  bindings: readonly ThreadAppBinding[],
  apps: readonly AppInfo[]
): Set<string> {
  const ids = new Set<string>()
  for (const binding of bindings) {
    addPendingSocialBindingRequestId(ids, binding)
  }
  for (const app of apps) {
    if (app.bindingSummary) addPendingSocialBindingRequestId(ids, app.bindingSummary)
  }
  return ids
}

function addPendingSocialBindingRequestId(ids: Set<string>, binding: ThreadBindingLike): void {
  if (binding.state !== 'pending') return
  if (!getSocialChannelNameFromAppId(binding.appId)) return
  const bindingRequestId = 'bindingRequestId' in binding ? binding.bindingRequestId : undefined
  if (bindingRequestId) ids.add(bindingRequestId)
}

function getSocialChannelNameFromAppId(appId: string): string | null {
  const prefix = 'com.dotharness.channel.'
  if (appId.startsWith(prefix)) return appId.slice(prefix.length)
  return null
}

function formatSocialTarget(target: NonNullable<ThreadBindingLike['socialTarget']>): string {
  const name = target.displayName?.trim()
  if (name) return name
  return `${target.channelName}:${target.conversationKind}:${target.conversationId}`
}

function isUnavailableSocialAppRow(
  app: AppInfo,
  binding?: ThreadBindingLike,
  pendingHandoff?: PendingSocialHandoff
): boolean {
  return getSocialChannelName(app) != null
    && app.connectionState !== 'connected'
    && binding == null
    && pendingHandoff == null
}

function attachedToolCount(binding: ThreadBindingLike): number {
  return 'attachedToolCount' in binding ? binding.attachedToolCount : 0
}

const root: CSSProperties = { position: 'relative', flexShrink: 0 }
const countBadge: CSSProperties = { minWidth: 15, height: 15, borderRadius: 999, background: 'var(--accent)', color: 'var(--on-accent)', fontSize: 10, display: 'inline-flex', alignItems: 'center', justifyContent: 'center', marginLeft: -4 }
const popover: CSSProperties = { position: 'absolute', top: 34, right: 0, zIndex: 30, width: 360, maxWidth: 'calc(100vw - 32px)', border: '1px solid var(--border-default)', borderRadius: 8, background: 'var(--bg-secondary)', boxShadow: 'var(--shadow-level-3)', padding: 10 }
const popoverHeader: CSSProperties = { display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 8, marginBottom: 8 }
const popoverHeaderTitle: CSSProperties = { minWidth: 0, display: 'flex', alignItems: 'center', gap: 8 }
const popoverTitle: CSSProperties = { fontSize: 13, color: 'var(--text-primary)' }
const bindingList: CSSProperties = { display: 'flex', flexDirection: 'column', gap: 8 }
const bindingRow: CSSProperties = { display: 'grid', gridTemplateColumns: '30px minmax(0, 1fr) auto', alignItems: 'center', gap: 9, border: '1px solid var(--border-default)', borderRadius: 8, padding: 9 }
const bindingMain: CSSProperties = { minWidth: 0 }
const bindingTitleRow: CSSProperties = { display: 'flex', alignItems: 'center', gap: 7, minWidth: 0, flexWrap: 'wrap' }
const bindingTitle: CSSProperties = { fontSize: 12, color: 'var(--text-primary)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }
const bindingSubtitle: CSSProperties = { marginTop: 3, color: 'var(--text-secondary)', fontSize: 11, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }
const bindingInstruction: CSSProperties = { marginTop: 4, color: 'var(--text-primary)', fontSize: 11, lineHeight: 1.35, overflowWrap: 'anywhere' }
const bindingIconImg: CSSProperties = { width: 30, height: 30, borderRadius: 7, objectFit: 'cover', background: 'var(--bg-tertiary)', border: '1px solid var(--border-default)' }
const bindingIconFallback: CSSProperties = { width: 30, height: 30, borderRadius: 7, display: 'inline-flex', alignItems: 'center', justifyContent: 'center', background: 'var(--bg-tertiary)', border: '1px solid var(--border-default)', color: 'var(--text-secondary)' }
const bindingActions: CSSProperties = { display: 'flex', alignItems: 'center', justifyContent: 'flex-end', gap: 6, flexWrap: 'wrap' }
const baseActionButton: CSSProperties = { height: 28, borderRadius: 7, cursor: 'pointer', display: 'inline-flex', alignItems: 'center', justifyContent: 'center', gap: 5, padding: '0 9px', fontSize: 11, fontWeight: 600, whiteSpace: 'nowrap' }
const primaryButton: CSSProperties = { ...baseActionButton, border: '1px solid var(--text-primary)', background: 'var(--text-primary)', color: 'var(--bg-primary)' }
const secondaryButton: CSSProperties = { ...baseActionButton, border: '1px solid var(--border-default)', background: 'var(--bg-tertiary)', color: 'var(--text-primary)' }
const iconButton: CSSProperties = { width: 28, height: 28, border: 'none', borderRadius: 7, background: 'var(--bg-tertiary)', color: 'var(--text-secondary)', cursor: 'pointer', display: 'inline-flex', alignItems: 'center', justifyContent: 'center', padding: 0 }
const mutedText: CSSProperties = { color: 'var(--text-secondary)', fontSize: 12, padding: 8 }
const errorText: CSSProperties = { color: 'var(--error)', fontSize: 12, padding: 8 }

// Transparent fill (not --bg-secondary) so this "Apps" button matches the other
// thread-header buttons — Open (openButtonStyle) and Commit (headerButtonStyle) —
// which are all outlined with a transparent background. A filled background here
// made the Apps button read as a different control in the header row.
const buttonStyle: CSSProperties = {
  height: 28,
  minWidth: 28,
  padding: '0 8px',
  border: '1px solid var(--border-default)',
  borderRadius: 6,
  background: 'transparent',
  color: 'var(--text-secondary)',
  cursor: 'pointer',
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  gap: 5
}

function statePill(good: boolean): CSSProperties {
  return {
    borderRadius: 999,
    padding: '2px 6px',
    fontSize: 10,
    background: good ? 'rgba(22, 163, 74, 0.12)' : 'var(--bg-tertiary)',
    color: good ? 'var(--success, #15803d)' : 'var(--text-secondary)'
  }
}
