import { type CSSProperties, type ReactNode, useCallback, useEffect, useRef, useState } from 'react'
import {
  useUIStore,
  SIDEBAR_COLLAPSED_WIDTH,
  SIDEBAR_MIN_WIDTH,
  DETAIL_MIN_WIDTH,
  DETAIL_DEFAULT_WIDTH_RATIO,
  type UIState
} from '../../stores/uiStore'
import { useResponsiveLayout } from '../../hooks/useResponsiveLayout'
import { useThreadStore } from '../../stores/threadStore'
import { DragHandle } from './DragHandle'
import { ResizeEdgeGlow } from './ResizeEdgeGlow'

interface ThreePanelProps {
  sidebar: ReactNode
  conversation: ReactNode
  detail: ReactNode
}

const CONVERSATION_MIN_WIDTH = 400
const RESIZE_HANDLE_HIT_WIDTH = 8
const DETAIL_PANEL_TRANSITION_MS = 200
const MAC_SIDEBAR_TRAFFIC_LIGHT_SAFE_AREA_PX = 24
const DRAG_REGION: CSSProperties = { WebkitAppRegion: 'drag' }
type ResizeEdge = 'sidebar' | 'detail' | null

function isDetailPanelEffectivelyVisible(
  activeMainView: UIState['activeMainView'],
  detailPanelVisible: boolean,
  activeThreadId: string | null
): boolean {
  const isWelcomeState = activeMainView === 'conversation' && !activeThreadId
  return !(
    activeMainView === 'settings' ||
    activeMainView === 'channels' ||
    activeMainView === 'skills' ||
    activeMainView === 'automations' ||
    isWelcomeState
  ) && detailPanelVisible
}

function resolveMaxDetailPanelWidth(mainSurfaceWidth: number): number {
  return Math.max(DETAIL_MIN_WIDTH, mainSurfaceWidth - CONVERSATION_MIN_WIDTH)
}

export function resolveDetailPanelWidth(
  fallbackWidth: number,
  widthRatio: number,
  mainSurfaceWidth: number | null,
  detailPanelVisible: boolean
): number {
  if (!detailPanelVisible) return 0

  const safeFallbackWidth = Math.max(DETAIL_MIN_WIDTH, fallbackWidth)
  if (mainSurfaceWidth == null || mainSurfaceWidth <= 0) return safeFallbackWidth

  const safeRatio = Number.isFinite(widthRatio) && widthRatio > 0
    ? widthRatio
    : DETAIL_DEFAULT_WIDTH_RATIO
  const proportionalWidth = Math.max(DETAIL_MIN_WIDTH, Math.round(mainSurfaceWidth * safeRatio))
  const maxDetailWidth = resolveMaxDetailPanelWidth(mainSurfaceWidth)

  return Math.min(proportionalWidth, maxDetailWidth)
}

/**
 * Three-panel horizontal layout: Sidebar | Conversation | Detail
 *
 * Spec §8.1 dimensions:
 * - Sidebar: 240px default, 200px min, 48px collapsed
 * - Conversation: flex:1, 400px min, always visible
 * - Detail: 600px preferred default, 300px min, bounded by available width, collapsible
 *
 * Spec §8.3 drag handles: transparent hit area, real divider highlight on hover
 * Spec §8.2 responsive breakpoints applied via useResponsiveLayout
 * Spec §15.5 transitions: 200ms ease-out for panel collapse
 */
export function ThreePanel({ sidebar, conversation, detail }: ThreePanelProps): JSX.Element {
  useResponsiveLayout()
  const isMac = (window as Window & { api?: { platform?: string } }).api?.platform === 'darwin'

  const {
    sidebarCollapsed,
    sidebarWidth,
    detailPanelVisible,
    detailPanelWidth,
    detailPanelWidthRatio,
    activeMainView
  } = useUIStore()
  const activeThreadId = useThreadStore((s) => s.activeThreadId)
  const mainSurfaceRef = useRef<HTMLDivElement>(null)
  const mainSurfaceWidthRef = useRef<number | null>(null)
  const [observedMainSurfaceWidth, setObservedMainSurfaceWidth] = useState<number | null>(null)
  const [sidebarDividerActive, setSidebarDividerActive] = useState(false)
  const [detailDividerActive, setDetailDividerActive] = useState(false)
  const [resizingEdge, setResizingEdge] = useState<ResizeEdge>(null)
  const sidebarDividerHighlighted = sidebarDividerActive || resizingEdge === 'sidebar'
  const detailDividerHighlighted = detailDividerActive || resizingEdge === 'detail'

  const effectiveDetailPanelVisible = isDetailPanelEffectivelyVisible(
    activeMainView,
    detailPanelVisible,
    activeThreadId
  )

  const effectiveSidebarWidth = sidebarCollapsed ? SIDEBAR_COLLAPSED_WIDTH : sidebarWidth

  const fallbackMainSurfaceWidth = window.innerWidth - effectiveSidebarWidth
  const mainSurfaceWidth = observedMainSurfaceWidth ?? fallbackMainSurfaceWidth
  mainSurfaceWidthRef.current = mainSurfaceWidth
  const effectiveDetailPanelWidth = resolveDetailPanelWidth(
    detailPanelWidth,
    detailPanelWidthRatio,
    mainSurfaceWidth,
    effectiveDetailPanelVisible
  )

  useEffect(() => {
    const element = mainSurfaceRef.current
    if (!element) return

    const updateWidth = (width: number): void => {
      setObservedMainSurfaceWidth(width > 0 ? Math.round(width) : null)
    }

    updateWidth(element.getBoundingClientRect().width)

    if (typeof ResizeObserver === 'undefined') return

    const observer = new ResizeObserver((entries) => {
      const entry = entries[0]
      if (!entry) return
      updateWidth(entry.contentRect.width)
    })
    observer.observe(element)
    return () => observer.disconnect()
  }, [])

  const handleSidebarDrag = useCallback((delta: number) => {
    const state = useUIStore.getState()
    if (state.sidebarCollapsed) return

    const currentActiveThreadId = useThreadStore.getState().activeThreadId
    const currentMainSurfaceWidth = window.innerWidth - state.sidebarWidth
    const detailVisible = isDetailPanelEffectivelyVisible(
      state.activeMainView,
      state.detailPanelVisible,
      currentActiveThreadId
    )
    const detailWidth = detailVisible
      ? resolveDetailPanelWidth(
        state.detailPanelWidth,
        state.detailPanelWidthRatio,
        currentMainSurfaceWidth,
        true
      )
      : 0
    const maxSidebarWidth = Math.max(
      SIDEBAR_MIN_WIDTH,
      window.innerWidth - CONVERSATION_MIN_WIDTH - detailWidth
    )
    const nextWidth = Math.min(maxSidebarWidth, state.sidebarWidth + delta)
    state.setSidebarWidth(nextWidth)
  }, [])

  const handleSidebarDragStateChange = useCallback((dragging: boolean) => {
    setResizingEdge((current) => (dragging ? 'sidebar' : current === 'sidebar' ? null : current))
  }, [])

  const handleDetailDrag = useCallback((delta: number) => {
    const state = useUIStore.getState()
    const sidebar = state.sidebarCollapsed ? SIDEBAR_COLLAPSED_WIDTH : state.sidebarWidth
    const currentMainSurfaceWidth = mainSurfaceWidthRef.current ?? window.innerWidth - sidebar
    const maxDetailWidth = resolveMaxDetailPanelWidth(currentMainSurfaceWidth)
    const currentDetailWidth = resolveDetailPanelWidth(
      state.detailPanelWidth,
      state.detailPanelWidthRatio,
      currentMainSurfaceWidth,
      true
    )
    const nextWidth = Math.min(maxDetailWidth, Math.max(DETAIL_MIN_WIDTH, currentDetailWidth - delta))
    state.setDetailPanelWidth(nextWidth, currentMainSurfaceWidth)
  }, [])

  const handleDetailDragStateChange = useCallback((dragging: boolean) => {
    setResizingEdge((current) => (dragging ? 'detail' : current === 'detail' ? null : current))
  }, [])

  return (
    <div
      style={{
        position: 'relative',
        display: 'flex',
        flexDirection: 'row',
        height: '100%',
        width: '100%',
        overflow: 'hidden',
        background: 'transparent'
      }}
    >
      {/* Sidebar */}
      <div
        style={{
          width: `${effectiveSidebarWidth}px`,
          minWidth: `${effectiveSidebarWidth}px`,
          flexShrink: 0,
          overflow: 'visible',
          position: 'relative',
          transition:
            resizingEdge === 'sidebar' ? 'none' : 'width 200ms ease-out, min-width 200ms ease-out',
          background: 'transparent',
          display: 'flex',
          flexDirection: 'column',
          boxSizing: 'border-box'
        }}
      >
        {isMac && (
          <div
            data-testid="mac-sidebar-safe-area"
            onDoubleClick={() => {
              void window.api.window.toggleMaximize()
            }}
            style={{
              ...DRAG_REGION,
              height: `${MAC_SIDEBAR_TRAFFIC_LIGHT_SAFE_AREA_PX}px`,
              flexShrink: 0,
              userSelect: 'none'
            }}
          />
        )}
        {sidebar}
      </div>

      {!sidebarCollapsed && (
        <DragHandle
          className="drag-handle--sidebar"
          onDrag={handleSidebarDrag}
          onActiveChange={setSidebarDividerActive}
          onDragStateChange={handleSidebarDragStateChange}
          style={{
            position: 'absolute',
            top: 0,
            bottom: 0,
            left: `${effectiveSidebarWidth - RESIZE_HANDLE_HIT_WIDTH / 2}px`
          }}
        />
      )}

      {/* Main work surface: conversation + optional detail panel. The rounded
          left corners keep the sidebar as the app chrome behind it. */}
      <div
        ref={mainSurfaceRef}
        style={{
          flex: 1,
          minWidth: 0,
          position: 'relative',
          overflow: 'hidden',
          display: 'flex',
          flexDirection: 'row',
          background: 'var(--main-surface)',
          borderRadius: '14px 0 0 14px',
          boxShadow: 'var(--main-surface-frame-shadow)'
        }}
      >
        {/* The rest-state left edge stays the plain --main-surface-left-border
            hairline drawn by the frame shadow. */}
        <ResizeEdgeGlow active={sidebarDividerHighlighted} testId="sidebar-divider-glow" />

        {/* Conversation panel (always visible, fills remaining space) */}
        <div
          style={{
            flex: 1,
            minWidth: '400px',
            overflow: 'hidden',
            display: 'flex',
            flexDirection: 'column',
            background: 'transparent'
          }}
        >
          {conversation}
        </div>

        {/* Detail panel — the outer shell owns the width animation while the
            inner surface clips content. The vertical divider, resize glow, and
            drag handle are anchored to the shell's left edge, so they follow
            the browser-interpolated width instead of jumping to the target
            effectiveDetailPanelWidth before the drawer arrives. */}
        <div
          data-testid="detail-panel-shell"
          style={{
            width: effectiveDetailPanelVisible ? `${effectiveDetailPanelWidth}px` : '0px',
            minWidth: effectiveDetailPanelVisible ? `${DETAIL_MIN_WIDTH}px` : '0px',
            flexShrink: 0,
            position: 'relative',
            overflow: 'visible',
            transition:
              resizingEdge === 'detail'
                ? 'none'
                : `width ${DETAIL_PANEL_TRANSITION_MS}ms ease-out, min-width ${DETAIL_PANEL_TRANSITION_MS}ms ease-out`,
            background: 'transparent',
            display: 'flex',
            flexDirection: 'column'
          }}
        >
          <div
            data-testid="detail-panel-content-clip"
            style={{
              width: '100%',
              flex: 1,
              minWidth: 0,
              minHeight: 0,
              overflow: 'hidden',
              display: 'flex',
              flexDirection: 'column'
            }}
          >
            {effectiveDetailPanelVisible && detail}
          </div>

          {effectiveDetailPanelVisible && (
            <DragHandle
              onDrag={handleDetailDrag}
              onActiveChange={setDetailDividerActive}
              onDragStateChange={handleDetailDragStateChange}
              style={{
                position: 'absolute',
                top: 0,
                bottom: 0,
                left: `${-RESIZE_HANDLE_HIT_WIDTH / 2}px`
              }}
            />
          )}

          {/* Keep the divider mounted through close so it rides the collapsing
              shell all the way to the right edge, then becomes transparent.
              Inset by the card frame's own 1px hairlines so the divider butts
              against them instead of overpainting the top and bottom edges. */}
          <div
            aria-hidden
            data-testid="detail-divider-line"
            style={{
              position: 'absolute',
              top: '1px',
              bottom: '1px',
              left: 0,
              width: '1px',
              background: 'var(--glass-border)',
              opacity: effectiveDetailPanelVisible ? 1 : 0,
              transition: effectiveDetailPanelVisible
                ? 'opacity 0ms linear'
                : `opacity 0ms linear ${DETAIL_PANEL_TRANSITION_MS}ms`,
              pointerEvents: 'none',
              zIndex: 3
            }}
          />

          {/* The resize highlight shares the exact moving edge. Hover/drag
              opacity remains independent from the panel transition. */}
          <ResizeEdgeGlow
            active={effectiveDetailPanelVisible && detailDividerHighlighted}
            testId="detail-divider-glow"
          />
        </div>

      </div>
    </div>
  )
}
