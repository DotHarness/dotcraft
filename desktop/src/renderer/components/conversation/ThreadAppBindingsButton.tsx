import { useCallback, useEffect, useMemo, useState, type CSSProperties } from 'react'
import { Link2 } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import {
  AppBindingActivationError,
  useAppBindingStore,
  type AppInfo,
  type ThreadAppBinding,
  type ThreadAppBindingSummary
} from '../../stores/appBindingStore'
import { useConnectionStore } from '../../stores/connectionStore'
import { addToast } from '../../stores/toastStore'
import { Button } from '../ui/Button'
import { ChannelIconBadge } from '../ui/channelMeta'
import { PillSwitch } from '../ui/PillSwitch'
import { IdentityMark } from '../ui/IdentityMark'
import { openAppHandoff } from '../plugins/AppBindingPanel'
import { AppBindingPickerRow, AppBindingsPicker, isAppReadyForBindingPicker } from './AppBindingsPicker'

interface ThreadAppBindingsButtonProps {
  threadId: string
}

const EMPTY_THREAD_APP_BINDINGS: ThreadAppBinding[] = []
const EMPTY_THREAD_APPS: AppInfo[] = []
type ThreadBindingLike = ThreadAppBinding | ThreadAppBindingSummary

interface ThreadAppRowModel {
  key: string
  app: AppInfo
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
  const canUseAppBinding = useConnectionStore((s) => s.capabilities?.appBindingVersion === 1)
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
  const confirmCapabilities = useAppBindingStore((s) => s.confirmCapabilities)
  const createBindingRequest = useAppBindingStore((s) => s.createBindingRequest)
  const waitForThreadBinding = useAppBindingStore((s) => s.waitForThreadBinding)
  const [open, setOpen] = useState(false)
  const [busyKey, setBusyKey] = useState<string | null>(null)
  const [pendingSocialHandoffs, setPendingSocialHandoffs] = useState<Record<string, PendingSocialHandoff>>({})

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
      .filter(isAppReadyForBindingPicker)
      .sort((a, b) => a.displayName.localeCompare(b.displayName)),
    [apps]
  )

  const rows = useMemo<ThreadAppRowModel[]>(() => {
    const bindingsById = new Map(threadBindings.map((binding) => [binding.bindingId, binding]))
    return threadApps.map((app) => {
      const candidate = app.bindingSummary
        ? bindingsById.get(app.bindingSummary.bindingId) ?? app.bindingSummary
        : threadBindings.find((candidate) => candidate.appId === app.appId)
      const binding = candidate?.state === 'revoked' || candidate?.state === 'cancelled'
        ? undefined
        : candidate
      return {
        key: `app:${app.appId}`,
        app,
        binding,
        pendingHandoff: resolvePendingSocialHandoff(pendingSocialHandoffs, binding)
      }
    })
  }, [pendingSocialHandoffs, threadApps, threadBindings])

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

  async function bindApp(app: AppInfo): Promise<void> {
    const socialChannelName = getSocialChannelName(app)
    if (!isAppReadyForBindingPicker(app)) {
      throw new Error(t('appBinding.welcomeAppNotConnected', { name: app.displayName || app.appId }))
    }

    const result = await createBindingRequest({
      threadId,
      appId: app.appId,
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
    try {
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
    } catch (err) {
      try {
        await cancelBindingRequest(threadId, result.bindingRequestId, 'activation_failed', result.bindingId)
      } catch {
        // Keep the original activation error visible; a retry starts a fresh binding request.
      }
      if (err instanceof AppBindingActivationError) {
        throw new Error(t('appBinding.bindingFailed', {
          name: app.displayName || app.appId,
          state: err.state,
          reason: err.failureReason || '—'
        }))
      }
      throw err
    }
  }

  return (
    <AppBindingsPicker
      open={open}
      onOpenChange={setOpen}
      loading={pickerLoading || busyKey === 'retry'}
      error={error || appsError}
      empty={rows.length === 0}
      emptyLabel={t('appBinding.threadEmpty')}
      onRetry={() => { void runAction('retry', () => refreshThreadAppPicker(true)) }}
    >
      {rows.map((row) => (
        <ThreadAppRow
          key={row.key}
          app={row.app}
          binding={row.binding}
          pendingHandoff={row.pendingHandoff}
          busy={busyKey != null}
          onToggle={(checked) => {
            if (checked) {
              void runAction(`bind:${row.app.appId}`, () => bindApp(row.app))
              return
            }
            if (!row.binding) return
            void runAction(`revoke:${row.binding.bindingId}`, async () => {
              const binding = row.binding!
              const isPending = binding.state === 'connecting'
              if (isPending) {
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
              await refreshThreadAppPicker(false)
              addToast(t(isPending ? 'appBinding.bindingRequestCancelled' : 'appBinding.bindingRevoked'), 'success')
            })
          }}
          onConfirm={(decision) => {
            if (!row.binding) return
            const revision = row.binding.candidateCapabilityRevision
            if (revision == null) return
            void runAction(`capabilities:${row.binding.bindingId}`, async () => {
              await confirmCapabilities(threadId, row.binding!.bindingId, revision, decision)
              await refreshThreadAppPicker(false)
            })
          }}
        />
      ))}
    </AppBindingsPicker>
  )
}

function ThreadAppRow({
  app,
  binding,
  pendingHandoff,
  busy,
  onToggle,
  onConfirm,
}: {
  app: AppInfo
  binding?: ThreadBindingLike
  pendingHandoff?: PendingSocialHandoff
  busy: boolean
  onToggle: (checked: boolean) => void
  onConfirm: (decision: 'accept' | 'reject') => void
}): JSX.Element {
  const t = useT()
  const [reviewOpen, setReviewOpen] = useState(false)
  const displayName = app.displayName
  const socialTargetLabel = binding?.socialTarget ? formatSocialTarget(binding.socialTarget) : null
  const pendingInstruction = pendingHandoff
    ? pendingHandoff.instructions?.trim() || `/bind ${pendingHandoff.bindCode}`
    : null
  const socialChannelName = getSocialChannelName(app)
  const icon = app.icon || binding?.icon
  const isReview = binding?.state === 'needsConfirmation'
  const checked = binding != null
  const pendingChanges = binding && 'pendingChanges' in binding ? binding.pendingChanges ?? [] : []
  const subtitle = socialTargetLabel || undefined
  const action = (
    <>
      {isReview && (
        <Button size="sm" variant="secondary" disabled={busy} onClick={() => setReviewOpen((value) => !value)}>
          {t('appBinding.thread.review')}
        </Button>
      )}
      <PillSwitch
        checked={checked}
        onChange={onToggle}
        size="sm"
        disabled={busy}
        aria-label={t('appBinding.thread.useApp', { name: displayName })}
      />
    </>
  )
  const details = (pendingInstruction || (isReview && reviewOpen)) ? (
    <>
      {pendingInstruction && <div style={bindingInstruction}>{pendingInstruction}</div>}
      {isReview && reviewOpen && (
        <div style={capabilityReview}>
          {pendingChanges.map((change, index) => (
            <div key={`${change.kind}:${change.tool}:${index}`} style={bindingSubtitle}>{change.detail || change.tool}</div>
          ))}
          <div style={reviewActions}>
            <Button size="sm" variant="secondary" disabled={busy} onClick={() => onConfirm('reject')}>{t('appBinding.rejectCapabilities')}</Button>
            <Button size="sm" variant="primary" disabled={busy} onClick={() => onConfirm('accept')}>{t('appBinding.acceptCapabilities')}</Button>
          </div>
        </div>
      )}
    </>
  ) : undefined
  return (
    <AppBindingPickerRow
      icon={<AppIcon icon={icon} channelName={socialChannelName} label={displayName} />}
      title={displayName}
      subtitle={subtitle}
      action={action}
      details={details}
    />
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
    return <IdentityMark role="list" size={30} src={icon} fallback={<Link2 size={15} />} />
  }
  if (channelName) {
    return <ChannelIconBadge channelName={channelName} tooltip={label || channelName} size={30} />
  }
  return (
    <IdentityMark role="list" size={30} fallback={<Link2 size={15} />} framed />
  )
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
  if (binding?.state !== 'connecting') return undefined
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
  if (binding.state !== 'connecting') return
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

const bindingSubtitle: CSSProperties = { marginTop: 3, color: 'var(--text-secondary)', fontSize: 11, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }
const bindingInstruction: CSSProperties = { marginTop: 4, color: 'var(--text-primary)', fontSize: 11, lineHeight: 1.35, overflowWrap: 'anywhere' }
const capabilityReview: CSSProperties = { gridColumn: '2 / -1', paddingTop: 4 }
const reviewActions: CSSProperties = { display: 'flex', justifyContent: 'flex-end', gap: 6, marginTop: 8 }
