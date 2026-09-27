import { useCallback, useEffect, useLayoutEffect, useRef, useState, type CSSProperties, type MouseEvent as ReactMouseEvent } from 'react'
import { Download, Menu, Minus, PanelLeftClose, PanelLeftOpen, Square, X } from 'lucide-react'
import { Spinner } from '../ui/Spinner'

import { TITLE_BAR_OVERLAY_HEIGHT } from '../../../shared/titleBarOverlay'
import { TOP_LEVEL_MENU_IDS, type TopLevelMenuId } from '../../../shared/locales'
import type { AppUpdateState } from '../../../shared/appUpdate'
import { useT } from '../../contexts/LocaleContext'
import { useWindowMaximized } from '../../hooks/useWindowMaximized'
import { useUIStore } from '../../stores/uiStore'
import { IconButton } from '../ui/IconButton'
import { ACTION_SHORTCUTS } from '../ui/shortcutKeys'
import { DotCraftLogo } from '../ui/DotCraftLogo'
import { AppUpdateDialog } from '../update/AppUpdateDialog'
import { AppNavigationControls } from './AppNavigationControls'
import css from './CustomMenuBar.module.css'

const dragRegion: CSSProperties = { WebkitAppRegion: 'drag' }
const noDrag: CSSProperties = { WebkitAppRegion: 'no-drag' }

const MENU_LABEL_KEY: Record<TopLevelMenuId, 'menu.file' | 'menu.edit' | 'menu.view' | 'menu.window' | 'menu.help'> =
  {
    file: 'menu.file',
    edit: 'menu.edit',
    view: 'menu.view',
    window: 'menu.window',
    help: 'menu.help'
  }

export function CustomMenuBar(): JSX.Element {
  const t = useT()
  const isWindows = window.api.platform === 'win32'
  const sidebarCollapsed = useUIStore((s) => s.sidebarCollapsed)
  const toggleSidebar = useUIStore((s) => s.toggleSidebar)
  const [sidebarButtonHovered, setSidebarButtonHovered] = useState(false)
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

  const sidebarLabel = sidebarCollapsed ? t('sidebar.expandAria') : t('sidebar.collapseAria')
  const updateButtonVisible = Boolean(updateState.update) && (
    updateState.status === 'available' ||
    updateState.status === 'downloading' ||
    updateState.status === 'downloaded' ||
    updateState.status === 'error'
  )
  const updateLabel = getUpdateTooltipLabel(updateState, t)

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
        <IconButton
        size={28}
        label={sidebarLabel}
        tooltipLabel={sidebarLabel}
        shortcut={ACTION_SHORTCUTS.toggleSidebar}
        tooltipPlacement="bottom"
        onClick={toggleSidebar}
        onMouseEnter={() => setSidebarButtonHovered(true)}
        onMouseLeave={() => setSidebarButtonHovered(false)}
        style={{ ...noDrag, margin: '0 4px' }}
        icon={sidebarButtonHovered
          ? sidebarCollapsed
            ? <PanelLeftOpen size={16} aria-hidden="true" />
            : <PanelLeftClose size={16} aria-hidden="true" />
          : <DotCraftLogo size={20} />}
        />

        <AppNavigationControls />

        {updateButtonVisible && (
          <IconButton
          size={28}
          label={updateLabel}
          tooltipLabel={updateLabel}
          tooltipPlacement="bottom"
          onClick={() => setUpdateDialogOpen(true)}
          style={updateButtonStyle(updateState.status)}
          icon={(
            <>
              {updateState.status === 'downloading'
                ? <Spinner size={16} />
                : <Download size={16} aria-hidden="true" />}
              {(updateState.status === 'downloaded' || updateState.status === 'error') && (
                <span style={updateBadgeStyle} aria-hidden="true" />
              )}
            </>
          )}
          />
        )}
      </div>

      <div
        ref={menusRef}
        className={css.menus}
        data-collapsed={isWindows && collapsedMenus || undefined}
        aria-hidden={isWindows && collapsedMenus || undefined}
        inert={isWindows && collapsedMenus}
      >
        {TOP_LEVEL_MENU_IDS.map((menuId) => (
          <button
            key={menuId}
            type="button"
            style={menuButtonStyle}
            onMouseDown={(e) => {
              e.preventDefault()
              const r = e.currentTarget.getBoundingClientRect()
              void window.api.menu.popupTopLevel(menuId, r.left, r.bottom)
            }}
          >
            {t(MENU_LABEL_KEY[menuId])}
          </button>
        ))}
      </div>

      {isWindows && collapsedMenus && (
        <button
          type="button"
          className={css.narrowMenu}
          onMouseDown={(event) => {
            event.preventDefault()
            const rect = event.currentTarget.getBoundingClientRect()
            void window.api.menu.popupAll(rect.left, rect.bottom)
          }}
        >
          <Menu size={15} aria-hidden="true" />
          {t('menu.all')}
        </button>
      )}

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

function getUpdateTooltipLabel(
  state: AppUpdateState,
  t: (key: string, vars?: Record<string, string | number>) => string
): string {
  if (state.status === 'downloading') return t('update.downloadingTooltip')
  if (state.status === 'downloaded') return t('update.downloadedTooltip')
  if (state.status === 'error') return t('update.failedTooltip')
  return t('update.availableTooltip')
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
            : 'var(--bg-tertiary)'
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

function updateButtonStyle(status: AppUpdateState['status']): CSSProperties {
  const active = status === 'downloaded' || status === 'error'
  return {
    ...noDrag,
    position: 'relative',
    color: active ? 'var(--accent)' : undefined
  }
}

const updateBadgeStyle: CSSProperties = {
  position: 'absolute',
  top: 4,
  right: 4,
  width: 7,
  height: 7,
  borderRadius: 999,
  background: 'var(--accent)',
  boxShadow: '0 0 0 2px color-mix(in srgb, var(--text-primary) 8%, transparent)'
}

const menuButtonStyle: CSSProperties = {
  ...noDrag,
  marginRight: 2,
  padding: '2px 6px',
  border: 'none',
  borderRadius: 4,
  background: 'transparent',
  color: 'inherit',
  fontSize: 'var(--type-ui-size)',
  lineHeight: 'var(--type-ui-line-height)',
  fontWeight: 'var(--type-ui-weight)',
  cursor: 'default'
}
