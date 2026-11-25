import { useCallback, useEffect, useLayoutEffect, useRef, useState, type CSSProperties, type MouseEvent as ReactMouseEvent } from 'react'
import { Minus, Square, X } from 'lucide-react'

import { TITLE_BAR_OVERLAY_HEIGHT } from '../../../shared/titleBarOverlay'
import type { AppUpdateState } from '../../../shared/appUpdate'
import { useWindowMaximized } from '../../hooks/useWindowMaximized'
import { AppUpdateDialog } from '../update/AppUpdateDialog'
import { isTitleBarUpdateVisible, TitleBarUpdateButton } from '../update/TitleBarUpdateButton'
import { AppNavigationControls } from './AppNavigationControls'
import { ApplicationMenuBar } from './ApplicationMenuBar'
import { SidebarToggleButton } from './SidebarToggleButton'
import css from './CustomMenuBar.module.css'

const dragRegion: CSSProperties = { WebkitAppRegion: 'drag' }
const noDrag: CSSProperties = { WebkitAppRegion: 'no-drag' }

export function CustomMenuBar(): JSX.Element {
  const isWindows = window.api.platform === 'win32'
  const [collapsedMenus, setCollapsedMenus] = useState(false)
  const barRef = useRef<HTMLDivElement>(null)
  const prefixRef = useRef<HTMLDivElement>(null)
  const menusRef = useRef<HTMLDivElement>(null)
  const controlsRef = useRef<HTMLDivElement>(null)
  const [updateDialogOpen, setUpdateDialogOpen] = useState(false)
  const [updateState, setUpdateState] = useState<AppUpdateState>({
    status: 'idle',
    currentVersion: ''
  })
  const maximized = useWindowMaximized()
  const handleTitleBarDoubleClick = useCallback((event: ReactMouseEvent<HTMLDivElement>) => {
    if (isInteractiveTitleBarTarget(event.target)) return
    event.preventDefault()
    void window.api.window.toggleMaximize()
  }, [])

  useEffect(() => {
    let disposed = false
    void window.api.updates.getState()
      .then((state) => {
        if (!disposed) setUpdateState(state)
      })
      .catch(() => {})
    const unsubscribe = window.api.updates.onStateChanged((state) => {
      setUpdateState(state)
    })
    const unsubscribeOpenDialog = window.api.updates.onOpenDialog(() => {
      setUpdateDialogOpen(true)
    })
    return () => {
      disposed = true
      unsubscribe()
      unsubscribeOpenDialog()
    }
  }, [])

  useLayoutEffect(() => {
    if (!isWindows) return
    const bar = barRef.current
    const prefix = prefixRef.current
    const menus = menusRef.current
    const controls = controlsRef.current
    if (!bar || !prefix || !menus || !controls) return
    const measure = (): void => {
      setCollapsedMenus(prefix.offsetWidth + menus.offsetWidth + controls.offsetWidth + 12 > bar.clientWidth)
    }
    measure()
    if (typeof ResizeObserver === 'undefined') return
    const observer = new ResizeObserver(measure)
    for (const element of [bar, prefix, menus, controls]) observer.observe(element)
    return () => observer.disconnect()
  }, [isWindows])

  return (
    <div
      ref={barRef}
      className="dotcraft-custom-menu-bar"
      style={{
        ...dragRegion,
        height: TITLE_BAR_OVERLAY_HEIGHT,
        flexShrink: 0,
        display: 'flex',
        alignItems: 'center',
        color: 'var(--text-secondary)',
        userSelect: 'none'
      }}
      onDoubleClick={handleTitleBarDoubleClick}
    >
      <div ref={prefixRef} className={css.prefix}>
        <SidebarToggleButton showBrand style={{ ...noDrag, margin: '0 4px' }} />

        <AppNavigationControls />

        {isTitleBarUpdateVisible(updateState) && (
          <TitleBarUpdateButton state={updateState} onOpen={() => setUpdateDialogOpen(true)} />
        )}
      </div>

      <ApplicationMenuBar menusRef={menusRef} collapsed={isWindows && collapsedMenus} />

      <div style={{ flex: 1, alignSelf: 'stretch' }} />

      {isWindows ? (
        <div ref={controlsRef} data-window-controls className={css.nativeControls} />
      ) : (
        <div ref={controlsRef} data-window-controls style={{ ...noDrag, display: 'flex', alignItems: 'stretch', height: '100%' }}>
          <WindowControlButton
            label="Minimize"
            onClick={() => {
              void window.api.window.minimize()
            }}
          >
            <Minus size={15} strokeWidth={1.8} aria-hidden="true" />
          </WindowControlButton>
          <WindowControlButton
            label={maximized ? 'Restore' : 'Maximize'}
            onClick={() => {
              void window.api.window.toggleMaximize()
            }}
          >
            {maximized
              ? <RestoreWindowIcon />
              : <Square size={12} strokeWidth={1.8} aria-hidden="true" />}
          </WindowControlButton>
          <WindowControlButton
            label="Close"
            danger
            onClick={() => {
              void window.api.window.close()
            }}
          >
            <X size={15} strokeWidth={1.8} aria-hidden="true" />
          </WindowControlButton>
        </div>
      )}
      {updateDialogOpen && (
        <AppUpdateDialog
          state={updateState}
          onClose={() => setUpdateDialogOpen(false)}
          onDownload={() => void window.api.updates.download()}
          onInstall={() => void window.api.updates.install()}
        />
      )}
    </div>
  )
}

function RestoreWindowIcon(): JSX.Element {
  return (
    <svg width="14" height="14" viewBox="0 0 16 16" fill="none" aria-hidden="true">
      <path d="M5.5 3.5V2.5H13.5V10.5H12.5M2.5 5.5H10.5V13.5H2.5V5.5Z" stroke="currentColor" strokeWidth="1.3" strokeLinejoin="round" />
    </svg>
  )
}

function isInteractiveTitleBarTarget(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) return false
  return target.closest('button,a,input,textarea,select,[role="button"]') !== null
}

function WindowControlButton({
  label,
  danger = false,
  onClick,
  children
}: {
  label: string
  danger?: boolean
  onClick: () => void
  children: JSX.Element
}): JSX.Element {
  const [hovered, setHovered] = useState(false)
  return (
    <button
      type="button"
      aria-label={label}
      title={label}
      onClick={onClick}
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
      style={{
        ...topBarIconButtonStyle,
        width: 46,
        height: '100%',
        borderRadius: 0,
        color: danger && hovered ? '#ffffff' : hovered ? 'var(--text-primary)' : 'var(--text-secondary)',
        backgroundColor: hovered
          ? danger
            ? 'var(--error)'
            : 'var(--bg-hover)'
          : 'transparent'
      }}
    >
      {children}
    </button>
  )
}

const topBarIconButtonStyle: CSSProperties = {
  ...noDrag,
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  padding: 0,
  border: 'none',
  background: 'transparent',
  cursor: 'default',
  transition: 'background-color 100ms ease, color 100ms ease'
}
