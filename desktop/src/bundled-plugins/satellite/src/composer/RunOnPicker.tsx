import { useCallback, useEffect, useId, useMemo, useRef, useState } from 'react'
import { Check, ChevronDown, Monitor, SatelliteDish } from 'lucide-react'
import { ActionTooltip, Button, Spinner } from '@dotcraft/plugin'
import type { DesktopPluginRunOnContext } from '@dotcraft/plugin'
import { useT } from '../i18n'
import { useSatellitesStore } from '../state'
import { satelliteHost, showRouteError, useRemoteToolHost } from '../runtime'
import { basename } from '../model'
import styles from './RunOnPicker.module.css'

interface RunOnOption {
  id: string
  hostId: string | null
  workspaceId: string | null
  title: string
  folder: string
  disabled: boolean
  noteKey?: string
}

const LOCAL_OPTION_ID = 'this-pc'

export function RunOnPicker({ context }: { context: DesktopPluginRunOnContext }): JSX.Element | null {
  const t = useT()
  const routing = useRemoteToolHost()
  const { threadId, workspacePath, busy: turnRunning } = context
  const { hosts, deferredRoutes } = routing
  const route = threadId ? routing.routes[threadId] : undefined
  const pendingRoute = threadId ? null : routing.pendingRoute
  const connecting = routing.connecting === threadId && threadId != null
  const localWorkspacePath = workspacePath
  const open = context.menuOpen
  const [highlight, setHighlight] = useState(0)
  const wrapRef = useRef<HTMLDivElement>(null)
  const popupRef = useRef<HTMLDivElement>(null)
  const listId = useId()

  const satelliteCount = useSatellitesStore((state) => state.supported ? state.satellites.length : 0)
  const visible = context.eligible && routing.supported && !routing.remote && satelliteCount > 0
  const interactive = visible && !connecting

  const setOpenState = useCallback((next: boolean): void => {
    context.setMenuOpen(next)
  }, [context.setMenuOpen])

  useEffect(() => {
    if (!visible) return
    void satelliteHost().remoteToolHost.list(threadId ?? undefined).catch(() => {})
  }, [threadId, visible])

  useEffect(() => {
    if (!visible || !threadId) return
    satelliteHost().remoteToolHost.maybeReapply(threadId, { turnRunning })
  }, [threadId, turnRunning, visible])

  useEffect(() => {
    if (!visible || !threadId || turnRunning) return
    void satelliteHost().remoteToolHost.applyDeferredRoute(threadId)
  }, [threadId, turnRunning, visible])

  const options = useMemo<RunOnOption[]>(() => {
    const local: RunOnOption = {
      id: LOCAL_OPTION_ID,
      hostId: null,
      workspaceId: null,
      title: t('composer.runOn.thisPc'),
      folder: basename(localWorkspacePath) || localWorkspacePath,
      disabled: false
    }
    const remote = hosts.flatMap((host) =>
      host.workspaces.map((workspace) => {
        const current = route?.hostId === host.hostId && route.workspaceId === workspace.workspaceId
        const offline = !host.online || (current && route?.status === 'leaseLost')
        const busy = host.online && workspace.available === false
        return {
          id: `${host.hostId}:${workspace.workspaceId}`,
          hostId: host.hostId,
          workspaceId: workspace.workspaceId,
          title: host.displayName,
          folder: workspace.displayName,
          disabled: !current && (offline || busy),
          ...(offline
            ? { noteKey: 'composer.runOn.offline' }
            : busy
              ? { noteKey: `composer.runOn.busy.${workspace.busyOwner === 'self' ? 'self' : 'other'}` }
              : {})
        } satisfies RunOnOption
      })
    )
    return [local, ...remote]
  }, [hosts, localWorkspacePath, route, t])

  const hasDeferredRoute = threadId != null
    && Object.prototype.hasOwnProperty.call(deferredRoutes, threadId)
  const deferredRoute = hasDeferredRoute && threadId ? deferredRoutes[threadId] : undefined
  const actualId = route ? `${route.hostId}:${route.workspaceId}` : LOCAL_OPTION_ID
  const pendingId = hasDeferredRoute
    ? deferredRoute
      ? `${deferredRoute.hostId}:${deferredRoute.workspaceId}`
      : LOCAL_OPTION_ID
    : null
  const welcomeId = pendingRoute
    ? `${pendingRoute.hostId}:${pendingRoute.workspaceId}`
    : LOCAL_OPTION_ID
  const selectedId = threadId ? pendingId ?? actualId : welcomeId
  const selectedIndex = Math.max(0, options.findIndex((option) => option.id === selectedId))

  useEffect(() => {
    setHighlight(selectedIndex)
  }, [selectedIndex, open])

  const apply = useCallback(
    async (option: RunOnOption): Promise<void> => {
      if (option.disabled || option.id === selectedId) {
        setOpenState(false)
        return
      }
      setOpenState(false)
      const store = satelliteHost().remoteToolHost
      if (!threadId) {
        store.setPendingRoute(
          option.hostId && option.workspaceId
            ? { hostId: option.hostId, workspaceId: option.workspaceId }
            : null
        )
        return
      }
      if (turnRunning) {
        if (option.id === actualId) {
          store.clearDeferredRoute(threadId)
        } else {
          store.deferRoute(
            threadId,
            option.hostId && option.workspaceId
              ? { hostId: option.hostId, workspaceId: option.workspaceId }
              : null
          )
        }
        return
      }
      try {
        if (option.hostId && option.workspaceId) {
          await store.connect(threadId, option.hostId, option.workspaceId)
        } else {
          await store.disconnect(threadId)
        }
      } catch (error) {
        showRouteError(option.title, error)
      }
    },
    [actualId, selectedId, setOpenState, threadId, turnRunning]
  )

  useEffect(() => {
    if (!open) return
    const handlePointerDown = (event: MouseEvent): void => {
      if (!wrapRef.current?.contains(event.target as Node)) setOpenState(false)
    }
    const handleKeyDown = (event: KeyboardEvent): void => {
      if (event.key === 'Escape') {
        event.preventDefault()
        setOpenState(false)
        return
      }
      if (!interactive) return
      if (event.key === 'ArrowDown') {
        event.preventDefault()
        setHighlight((current) => Math.min(options.length - 1, current + 1))
        return
      }
      if (event.key === 'ArrowUp') {
        event.preventDefault()
        setHighlight((current) => Math.max(0, current - 1))
        return
      }
      if (event.key === 'Enter') {
        event.preventDefault()
        const next = options[highlight]
        if (next) void apply(next)
      }
    }
    window.addEventListener('mousedown', handlePointerDown, true)
    window.addEventListener('keydown', handleKeyDown, true)
    return () => {
      window.removeEventListener('mousedown', handlePointerDown, true)
      window.removeEventListener('keydown', handleKeyDown, true)
    }
  })

  if (!visible) return null

  const selected = options[selectedIndex]
  const label = connecting
    ? t('composer.runOn.connecting')
    : selected.title
  const offlineNote = !connecting && selected.noteKey === 'composer.runOn.offline'
    ? t(selected.noteKey)
    : null
  const tooltip = `${t('composer.runOn.label')} · ${label}${offlineNote ? ` · ${offlineNote}` : ''}`

  return (
    <div ref={wrapRef} className={styles.anchor}>
      <ActionTooltip label={open ? '' : tooltip}>
        <Button variant="ghost" className={styles.pill}
          data-testid="run-on-trigger"
          aria-label={tooltip}
          aria-haspopup={interactive ? 'listbox' : undefined}
          aria-expanded={interactive ? open : undefined}
          aria-controls={interactive && open ? listId : undefined}
          disabled={!interactive}
          data-open={open}
          onClick={() => setOpenState(!open)}
        >
          {connecting ? (
            <Spinner size={15} />
          ) : selectedId !== LOCAL_OPTION_ID ? (
            <SatelliteDish size={15} strokeWidth={1.8} aria-hidden data-testid="run-on-routed-glyph" />
          ) : (
            <Monitor size={15} strokeWidth={1.8} aria-hidden />
          )}
          <span className={styles.label}>{label}</span>
          {offlineNote && (
            <span className={styles.pillNote} data-testid="run-on-offline-note">· {offlineNote}</span>
          )}
          <ChevronDown size={14} strokeWidth={1.8} aria-hidden />
        </Button>
      </ActionTooltip>

      {interactive && open && (
        <div
          ref={popupRef}
          id={listId}
          role="listbox"
          aria-label={t('composer.runOn.label')}
          className={styles.menu}
        >
          {options.map((option, index) => {
            const isSelected = option.id === selectedId
            const note = option.noteKey ? t(option.noteKey) : null
            return (
              <button
                key={option.id}
                type="button"
                role="option"
                data-testid={`run-on-option-${option.id}`}
                aria-selected={isSelected}
                aria-disabled={option.disabled || undefined}
                className={styles.option}
                data-highlighted={index === highlight}
                data-disabled={option.disabled}
                onMouseEnter={() => setHighlight(index)}
                onClick={() => {
                  void apply(option)
                }}
              >
                <span className={styles.optionIcon} aria-hidden>
                  <Monitor size={14} strokeWidth={1.9} />
                </span>
                <span className={styles.optionText}>
                  <span className={styles.optionTitle}>{option.title}</span>
                  <span className={styles.optionMeta}>
                    {note ? `${option.folder} · ${note}` : option.folder}
                  </span>
                </span>
                <span className={styles.optionCheck} data-selected={isSelected} aria-hidden>
                  <Check size={15} strokeWidth={2} />
                </span>
              </button>
            )
          })}
        </div>
      )}
    </div>
  )
}
