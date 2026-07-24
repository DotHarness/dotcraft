import { useCallback, useContext, useEffect, useRef, useState } from 'react'
import { LayerContext } from '../contexts/LayerContext'
import { useTransientOverlayStore } from '../stores/transientOverlayStore'

interface UseTransientOverlayOptions {
  /** Explicitly suppress the overlay (in addition to layer-based suppression). */
  disabled?: boolean
  /**
   * When true the overlay is part of the hover region: the pointer may travel
   * into it (attach `overlayRef` to it), and pointer-inside events don't dismiss.
   */
  interactive?: boolean
  openDelayMs?: number
  closeDelayMs?: number
}

interface UseTransientOverlay<A extends HTMLElement, O extends HTMLElement> {
  visible: boolean
  /** True while a layer above this one (or `disabled`) suppresses opening. */
  blocked: boolean
  anchorRef: React.RefObject<A | null>
  overlayRef: React.RefObject<O | null>
  /** Open immediately (no-op while blocked). */
  open(): void
  /** Open after `openDelayMs` (no-op while blocked). */
  scheduleOpen(): void
  /** Close after `closeDelayMs`. */
  scheduleClose(): void
  /** Close immediately and cancel pending timers. */
  hide(): void
  /** Cancel a pending close (e.g. the pointer entered the interactive overlay). */
  cancelClose(): void
}

/**
 * Shared engine for hover/focus-driven transient overlays (tooltips, detail
 * cards). It centralizes the fragile enter/leave/focus logic so every overlay
 * dismisses robustly instead of getting stuck visible.
 *
 * Beyond the ordinary `mouseleave`/`blur` path (which the browser fires only on
 * pointer movement), an open overlay also closes when:
 *  - a layer opens *above* it (`transientOverlayStore.topDepth` exceeds this
 *    overlay's `LayerContext` depth) — the authoritative fix for "a modal
 *    appeared over a stationary pointer", and
 *  - the user scrolls, presses Escape, points/clicks outside it, the window
 *    loses focus, or the tab is hidden.
 *
 * Opening always runs through the single gated `open()`/`scheduleOpen()`, so
 * there is no path that shows the overlay while it should be suppressed.
 */
export function useTransientOverlay<A extends HTMLElement = HTMLElement, O extends HTMLElement = HTMLElement>(
  options: UseTransientOverlayOptions = {}
): UseTransientOverlay<A, O> {
  const { disabled = false, interactive = false, openDelayMs = 0, closeDelayMs = 0 } = options

  const anchorRef = useRef<A | null>(null)
  const overlayRef = useRef<O | null>(null)
  const openTimer = useRef<number | null>(null)
  const closeTimer = useRef<number | null>(null)
  const [visible, setVisible] = useState(false)

  const myDepth = useContext(LayerContext)
  const topDepth = useTransientOverlayStore((state) => state.topDepth)
  const blocked = disabled || topDepth > myDepth
  const blockedRef = useRef(blocked)
  blockedRef.current = blocked

  const clearOpenTimer = useCallback((): void => {
    if (openTimer.current == null) return
    window.clearTimeout(openTimer.current)
    openTimer.current = null
  }, [])
  const clearCloseTimer = useCallback((): void => {
    if (closeTimer.current == null) return
    window.clearTimeout(closeTimer.current)
    closeTimer.current = null
  }, [])

  const hide = useCallback((): void => {
    clearOpenTimer()
    clearCloseTimer()
    setVisible(false)
  }, [clearOpenTimer, clearCloseTimer])

  const open = useCallback((): void => {
    if (blockedRef.current) return
    clearOpenTimer()
    clearCloseTimer()
    setVisible(true)
  }, [clearOpenTimer, clearCloseTimer])

  const scheduleOpen = useCallback((): void => {
    if (blockedRef.current) return
    clearCloseTimer()
    if (openTimer.current != null) return
    if (openDelayMs <= 0) {
      setVisible(true)
      return
    }
    openTimer.current = window.setTimeout(() => {
      openTimer.current = null
      if (!blockedRef.current) setVisible(true)
    }, openDelayMs)
  }, [clearCloseTimer, openDelayMs])

  const scheduleClose = useCallback((): void => {
    clearOpenTimer()
    if (closeDelayMs <= 0) {
      setVisible(false)
      return
    }
    if (closeTimer.current != null) return
    closeTimer.current = window.setTimeout(() => {
      closeTimer.current = null
      setVisible(false)
    }, closeDelayMs)
  }, [clearOpenTimer, closeDelayMs])

  const cancelClose = clearCloseTimer

  // Authoritative dismissal: a layer opened above us (or we became disabled).
  useEffect(() => {
    if (blocked) hide()
  }, [blocked, hide])

  // Clear timers on unmount.
  useEffect(() => () => {
    clearOpenTimer()
    clearCloseTimer()
  }, [clearOpenTimer, clearCloseTimer])

  // Safety-net dismissal while visible, for cases `mouseleave` never fires.
  useEffect(() => {
    if (!visible) return

    const isInside = (target: EventTarget | null): boolean => {
      const node = target as Node | null
      if (!node) return false
      return Boolean(anchorRef.current?.contains(node) || overlayRef.current?.contains(node))
    }

    const onScroll = (event: Event): void => {
      // A scroll inside an interactive overlay is legitimate; don't dismiss.
      if (interactive && overlayRef.current?.contains(event.target as Node)) return
      hide()
    }
    const onPointerDown = (event: Event): void => {
      if (!isInside(event.target)) hide()
    }
    const onKeyDown = (event: KeyboardEvent): void => {
      if (event.key === 'Escape') hide()
    }
    const onWindowBlur = (): void => hide()
    const onVisibilityChange = (): void => {
      if (document.hidden) hide()
    }

    document.addEventListener('scroll', onScroll, true)
    document.addEventListener('pointerdown', onPointerDown, true)
    document.addEventListener('keydown', onKeyDown, true)
    window.addEventListener('blur', onWindowBlur)
    document.addEventListener('visibilitychange', onVisibilityChange)
    return () => {
      document.removeEventListener('scroll', onScroll, true)
      document.removeEventListener('pointerdown', onPointerDown, true)
      document.removeEventListener('keydown', onKeyDown, true)
      window.removeEventListener('blur', onWindowBlur)
      document.removeEventListener('visibilitychange', onVisibilityChange)
    }
  }, [visible, interactive, hide])

  return { visible, blocked, anchorRef, overlayRef, open, scheduleOpen, scheduleClose, hide, cancelClose }
}
