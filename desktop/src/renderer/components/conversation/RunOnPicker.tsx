import { useCallback, useEffect, useId, useMemo, useRef, useState } from 'react'
import { Check, ChevronDown, Loader2, Monitor, SatelliteDish } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { useConnectionStore } from '../../stores/connectionStore'
import { bootstrapSatellites, useSatellitesStore } from '../../stores/satellitesStore'
import { useThreadStore } from '../../stores/threadStore'
import { showThreadRouteFailureToast, useThreadRouteStore } from '../../stores/threadRouteStore'
import { basename } from '../../utils/path'
import { ActionTooltip } from '../ui/ActionTooltip'
import { ComposerOverlapBand, useComposerOverlapBandHeight } from './useComposerOverlapBand'
import { WorkspaceFooterPill, menuStyle } from './composerFooterPrimitives'
import styles from './RunOnPicker.module.css'

interface RunOnPickerProps {
  /** Absent on the welcome composer, where the choice is held until the first message. */
  threadId?: string
  /** Names the This PC folder before a thread carries a workspace path. */
  workspacePath?: string
  turnRunning?: boolean
  /** Lets the context row close its own menus when this one opens. */
  onOpenChange?: (open: boolean) => void
}

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

/**
 * Whether the Run on chip has anything to offer, so the context row can decide to
 * render for it alone. Takes the satellite subscription on the caller's behalf.
 */
export function useRunOnVisible(): boolean {
  const capable = useConnectionStore((s) => s.capabilities?.remoteToolHost === true)
  const satelliteCount = useSatellitesStore((s) => (s.supported ? s.satellites.length : 0))

  useEffect(() => bootstrapSatellites(), [])

  return capable && satelliteCount > 0
}

export function RunOnPicker({
  threadId,
  workspacePath,
  turnRunning = false,
  onOpenChange
}: RunOnPickerProps): JSX.Element | null {
  const t = useT()
  const hosts = useThreadRouteStore((s) => s.hosts)
  const route = useThreadRouteStore((s) => (threadId ? s.routes[threadId] : undefined))
  const pendingRoute = useThreadRouteStore((s) => (threadId ? null : s.pendingRoute))
  const deferredRoutes = useThreadRouteStore((s) => s.deferredRoutes)
  const connecting = useThreadRouteStore((s) => s.connecting === threadId && threadId != null)
  const activeWorkspacePath = useThreadStore((s) => s.activeThread?.workspacePath ?? '')
  const localWorkspacePath = activeWorkspacePath || (workspacePath ?? '')

  const [open, setOpen] = useState(false)
  const [highlight, setHighlight] = useState(0)
  const wrapRef = useRef<HTMLDivElement>(null)
  const popupRef = useRef<HTMLDivElement>(null)
  const listId = useId()

  const visible = useRunOnVisible()
  const interactive = visible && !connecting
  const overlapBandHeight = useComposerOverlapBandHeight(popupRef, interactive && open)

  const setOpenState = useCallback((next: boolean): void => {
    setOpen((current) => (current === next ? current : next))
    onOpenChange?.(next)
  }, [onOpenChange])

  useEffect(() => {
    if (!visible) return
    void useThreadRouteStore.getState().list(threadId).catch(() => {})
  }, [threadId, visible])

  useEffect(() => {
    if (!visible || !threadId) return
    useThreadRouteStore.getState().maybeReapply(threadId, { turnRunning })
  }, [threadId, turnRunning, visible])

  useEffect(() => {
    if (!visible || !threadId || turnRunning) return
    void useThreadRouteStore.getState().applyDeferredRoute(threadId)
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
        const offline = !host.online
        const busy = host.online && workspace.available === false
        const current = route?.hostId === host.hostId && route.workspaceId === workspace.workspaceId
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
      const store = useThreadRouteStore.getState()
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
        showThreadRouteFailureToast(option.title, error, t)
      }
    },
    [actualId, selectedId, setOpenState, t, threadId, turnRunning]
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
  // The pill names the machine only; the folder stays on the option row's second line.
  const label = connecting
    ? t('composer.runOn.connecting')
    : selected.title
  const tooltip = `${t('composer.runOn.label')} · ${label}`

  return (
    <div ref={wrapRef} style={{ position: 'relative', minWidth: 0 }}>
      <ActionTooltip label={open ? '' : tooltip} placement="top" wrapperStyle={{ minWidth: 0 }}>
        <WorkspaceFooterPill
          data-testid="run-on-trigger"
          aria-label={tooltip}
          aria-haspopup={interactive ? 'listbox' : undefined}
          aria-expanded={interactive ? open : undefined}
          aria-controls={interactive && open ? listId : undefined}
          disabled={!interactive}
          open={open}
          onClick={() => setOpenState(!open)}
        >
          {connecting ? (
            <Loader2 size={15} className="animate-spin-custom" aria-hidden />
          ) : selectedId !== LOCAL_OPTION_ID ? (
            <SatelliteDish size={15} strokeWidth={1.8} aria-hidden data-testid="run-on-routed-glyph" />
          ) : (
            <Monitor size={15} strokeWidth={1.8} aria-hidden />
          )}
          <span className={styles.label}>{label}</span>
          <ChevronDown size={14} strokeWidth={1.8} aria-hidden />
        </WorkspaceFooterPill>
      </ActionTooltip>

      {interactive && open && (
        <div
          ref={popupRef}
          id={listId}
          role="listbox"
          aria-label={t('composer.runOn.label')}
          style={{ ...menuStyle, width: '320px' }}
        >
          <ComposerOverlapBand height={overlapBandHeight} radius={10} />
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
