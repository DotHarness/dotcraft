import { useCallback, useEffect, useState, type CSSProperties, type MouseEvent as ReactMouseEvent } from 'react'
import { Copy, Download, LoaderCircle, Minus, PanelLeftClose, PanelLeftOpen, Square, X } from 'lucide-react'

import { TITLE_BAR_OVERLAY_HEIGHT } from '../../../shared/titleBarOverlay'
import { TOP_LEVEL_MENU_IDS, type TopLevelMenuId } from '../../../shared/locales'
import type { AppUpdateState } from '../../../shared/appUpdate'
import { useT } from '../../contexts/LocaleContext'
import { useWindowMaximized } from '../../hooks/useWindowMaximized'
import { useUIStore } from '../../stores/uiStore'
import { ActionTooltip } from '../ui/ActionTooltip'
import { ACTION_SHORTCUTS } from '../ui/shortcutKeys'
import { DotCraftLogo } from '../ui/DotCraftLogo'
import { AppUpdateDialog } from '../update/AppUpdateDialog'
import { AppNavigationControls } from './AppNavigationControls'

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

/**
 * Global Windows / Linux top bar. macOS keeps native traffic lights and does
 * not render this component.
 */
export function CustomMenuBar(): JSX.Element {
  const t = useT()
  const sidebarCollapsed = useUIStore((s) => s.sidebarCollapsed)
  const toggleSidebar = useUIStore((s) => s.toggleSidebar)
  const [sidebarButtonHovered, setSidebarButtonHovered] = useState(false)
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
    return () => {
      disposed = true
      unsubscribe()
    }
  }, [])

  const handleDownloadUpdate = useCallback((): void => {
    void window.api.updates.downloadAndInstall()
      .then(setUpdateState)
      .catch((error: unknown) => {
        setUpdateState((current) => ({
          ...current,
          status: 'error',
          error: error instanceof Error ? error.message : String(error)
        }))
      })
  }, [])

  return (
    <div
      style={{
        ...dragRegion,
        height: TITLE_BAR_OVERLAY_HEIGHT,
        flexShrink: 0,
        display: 'flex',
        alignItems: 'center',
        background: 'transparent',
        color: 'var(--text-secondary)',
        userSelect: 'none'
      }}
      onDoubleClick={handleTitleBarDoubleClick}
    >
      <ActionTooltip label={sidebarLabel} shortcut={ACTION_SHORTCUTS.toggleSidebar} placement="bottom">
        <button
          type="button"
          onClick={toggleSidebar}
          onMouseEnter={() => setSidebarButtonHovered(true)}
          onMouseLeave={() => setSidebarButtonHovered(false)}
          aria-label={sidebarLabel}
          style={{
            ...topBarIconButtonStyle,
            ...noDrag,
            width: 36,
            height: TITLE_BAR_OVERLAY_HEIGHT,
            borderRadius: 0,
            color: sidebarButtonHovered ? 'var(--text-primary)' : 'var(--text-secondary)'
          }}
        >
          {sidebarButtonHovered
            ? sidebarCollapsed
              ? <PanelLeftOpen size={17} strokeWidth={2} aria-hidden="true" />
              : <PanelLeftClose size={17} strokeWidth={2} aria-hidden="true" />
            : <DotCraftLogo size={20} />}
        </button>
      </ActionTooltip>

      <AppNavigationControls />

      {updateButtonVisible && (
        <ActionTooltip label={updateLabel} placement="bottom">
          <button
            type="button"
            onClick={() => setUpdateDialogOpen(true)}
            aria-label={updateLabel}
            style={updateButtonStyle(updateState.status)}
          >
            {updateState.status === 'downloading'
              ? <LoaderCircle size={16} strokeWidth={2} aria-hidden="true" style={spinStyle} />
              : <Download size={16} strokeWidth={2} aria-hidden="true" />}
            {(updateState.status === 'available' || updateState.status === 'error') && (
              <span style={updateBadgeStyle} aria-hidden="true" />
            )}
          </button>
        </ActionTooltip>
      )}

      <div style={{ ...noDrag, display: 'flex', alignItems: 'center', marginLeft: 6 }}>
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

      <div style={{ flex: 1, alignSelf: 'stretch' }} />

      <div style={{ ...noDrag, display: 'flex', alignItems: 'stretch', height: '100%' }}>
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
            ? <Copy size={13} strokeWidth={1.8} aria-hidden="true" />
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
      {updateDialogOpen && (
        <AppUpdateDialog
          state={updateState}
          onClose={() => setUpdateDialogOpen(false)}
          onDownload={handleDownloadUpdate}
        />
      )}
    </div>
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
  const active = status === 'available' || status === 'error'
  return {
    ...topBarIconButtonStyle,
    position: 'relative',
    width: 32,
    height: TITLE_BAR_OVERLAY_HEIGHT,
    borderRadius: 0,
    color: active ? 'var(--accent)' : 'var(--text-secondary)',
    backgroundColor: 'transparent'
  }
}

const updateBadgeStyle: CSSProperties = {
  position: 'absolute',
  top: 8,
  right: 7,
  width: 7,
  height: 7,
  borderRadius: 999,
  background: 'var(--accent)',
  boxShadow: '0 0 0 2px color-mix(in srgb, var(--text-primary) 8%, transparent)'
}

const spinStyle: CSSProperties = {
  animation: 'spin 1s linear infinite'
}

const menuButtonStyle: CSSProperties = {
  ...noDrag,
  marginRight: 2,
  padding: '2px 8px',
  border: 'none',
  borderRadius: 4,
  background: 'transparent',
  color: 'inherit',
  fontSize: 'var(--type-ui-size)',
  lineHeight: 'var(--type-ui-line-height)',
  fontWeight: 'var(--type-ui-weight)',
  cursor: 'default'
}
