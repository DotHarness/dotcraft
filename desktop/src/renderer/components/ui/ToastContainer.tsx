import {
  useCallback,
  useEffect,
  useLayoutEffect,
  useRef,
  useState,
  type CSSProperties,
  type MouseEvent
} from 'react'
import { createPortal } from 'react-dom'
import { X, CheckCircle2, Info, TriangleAlert, Undo2, XCircle } from 'lucide-react'
import { useToastStore, type Toast, type ToastType } from '../../stores/toastStore'
import { MarkdownRenderer } from '../conversation/MarkdownRenderer'
import { useT } from '../../contexts/LocaleContext'

/** Distance from viewport right; toast sits below titleBarOverlay so caption buttons do not cover it. */
const TOAST_EDGE_INSET_PX = 12

/** Width of each toast card; horizontal inset (12px each side) keeps it off the window edge. */
const TOAST_WIDTH_PX = 380

/** Vertical gap between toasts when the stack is expanded (hover/focus). */
const STACK_GAP_PX = 10

/** When collapsed, each older toast peeks this many pixels below the newest. */
const COLLAPSED_PEEK_PX = 10

/** When collapsed, each older toast scales down by this much per layer. */
const COLLAPSED_SCALE_STEP = 0.04

/** Number of older toasts that remain visible as peeking layers behind the newest. */
const COLLAPSED_VISIBLE_DEPTH = 3

/** Hide the progress bar for long-lived toasts (e.g. background job results) where it would just sit full. */
const PROGRESS_BAR_MAX_DURATION_MS = 60_000

/**
 * Stacked toast notification container fixed to the top-right of the window.
 * Toasts arrive as a fanned stack — newest in front, older ones peeking behind
 * with reduced scale and opacity — and expand to a full vertical list on hover/focus.
 * Each toast has an explicit × close button; the auto-dismiss progress bar pauses on hover.
 */
export function ToastContainer(): JSX.Element {
  const toasts = useToastStore((s) => s.toasts)
  const removeToast = useToastStore((s) => s.removeToast)
  const [expanded, setExpanded] = useState(false)

  const isMac = window.api.platform === 'darwin'
  const topPx = isMac ? 16 : window.api.titleBarOverlayHeight + 16

  // Newest toast first so it visually sits on top of the stack.
  const ordered = [...toasts].reverse()

  // Heights are measured per-toast via ResizeObserver to lay out the expanded stack correctly.
  const heightsRef = useRef<Map<string, number>>(new Map())
  const [, forceRender] = useState(0)
  const setToastHeight = useCallback((id: string, height: number): void => {
    const prev = heightsRef.current.get(id)
    if (prev === height) return
    heightsRef.current.set(id, height)
    forceRender((n) => n + 1)
  }, [])

  const heights = ordered.map((t) => heightsRef.current.get(t.id) ?? 64)
  const expandedTops: number[] = []
  let runningTop = 0
  for (let i = 0; i < ordered.length; i++) {
    expandedTops.push(runningTop)
    runningTop += heights[i] + STACK_GAP_PX
  }

  const containerHeight = expanded
    ? Math.max(0, runningTop - STACK_GAP_PX)
    : (heights[0] ?? 0) +
      Math.min(Math.max(0, ordered.length - 1), COLLAPSED_VISIBLE_DEPTH) * COLLAPSED_PEEK_PX

  return createPortal(
    <div
      aria-live="polite"
      aria-atomic="false"
      data-expanded={expanded ? 'true' : 'false'}
      onMouseEnter={() => setExpanded(true)}
      onMouseLeave={() => setExpanded(false)}
      onFocus={() => setExpanded(true)}
      onBlur={(e) => {
        if (!e.currentTarget.contains(e.relatedTarget as Node | null)) setExpanded(false)
      }}
      style={{
        position: 'fixed',
        top: `${topPx}px`,
        right: `${TOAST_EDGE_INSET_PX}px`,
        zIndex: 30000,
        width: `min(${TOAST_WIDTH_PX}px, calc(100vw - ${TOAST_EDGE_INSET_PX * 2}px))`,
        height: `${containerHeight}px`,
        pointerEvents: ordered.length > 0 ? 'auto' : 'none',
        transition: prefersReducedMotion() ? 'none' : 'height 320ms cubic-bezier(0.16, 1, 0.3, 1)'
      }}
    >
      {ordered.map((toast, index) => {
        const expandedTop = expandedTops[index]
        const collapsedY = Math.min(index, COLLAPSED_VISIBLE_DEPTH) * COLLAPSED_PEEK_PX
        const collapsedScale = 1 - Math.min(index, COLLAPSED_VISIBLE_DEPTH) * COLLAPSED_SCALE_STEP
        const collapsedOpacity =
          index === 0 ? 1 : index >= COLLAPSED_VISIBLE_DEPTH ? 0 : 1 - index * 0.25
        return (
          <ToastItem
            key={toast.id}
            toast={toast}
            index={index}
            expanded={expanded}
            paused={expanded}
            expandedTop={expandedTop}
            collapsedY={collapsedY}
            collapsedScale={collapsedScale}
            collapsedOpacity={collapsedOpacity}
            onMeasure={setToastHeight}
            onDismiss={() => removeToast(toast.id)}
          />
        )
      })}
    </div>,
    document.body
  ) as JSX.Element
}

interface ToastItemProps {
  toast: Toast
  index: number
  expanded: boolean
  /** Pauses both the dismiss timer and the progress animation. Driven by stack hover/focus. */
  paused: boolean
  expandedTop: number
  collapsedY: number
  collapsedScale: number
  collapsedOpacity: number
  onMeasure: (id: string, height: number) => void
  onDismiss: () => void
}

function ToastItem({
  toast,
  index,
  expanded,
  paused,
  expandedTop,
  collapsedY,
  collapsedScale,
  collapsedOpacity,
  onMeasure,
  onDismiss
}: ToastItemProps): JSX.Element {
  const cardRef = useRef<HTMLDivElement | null>(null)
  const [entered, setEntered] = useState(false)
  const [leaving, setLeaving] = useState(false)
  const t = useT()
  const reduceMotion = prefersReducedMotion()

  // Pausable auto-dismiss timer: tracks remaining time so resuming after a hover
  // does not lose the time that already elapsed before the user hovered.
  const remainingRef = useRef(toast.duration)
  const startedAtRef = useRef<number | null>(null)
  const timerRef = useRef<number | null>(null)
  const onDismissRef = useRef(onDismiss)
  onDismissRef.current = onDismiss

  // An interactive toast settles exactly once: either the action (e.g. Undo) is
  // taken, or it expires (timeout/close) and the commit callback fires — never both.
  const settledRef = useRef(false)
  const toastRef = useRef(toast)
  toastRef.current = toast
  const settle = useCallback((viaAction: boolean) => {
    if (settledRef.current) return
    settledRef.current = true
    const current = toastRef.current
    if (viaAction) current.action?.onClick()
    else current.onExpire?.()
  }, [])

  useEffect(() => {
    if (toast.duration <= 0) return
    if (leaving) return
    if (paused) {
      if (timerRef.current != null) {
        window.clearTimeout(timerRef.current)
        timerRef.current = null
      }
      if (startedAtRef.current != null) {
        remainingRef.current = Math.max(
          0,
          remainingRef.current - (Date.now() - startedAtRef.current)
        )
        startedAtRef.current = null
      }
      return
    }
    startedAtRef.current = Date.now()
    timerRef.current = window.setTimeout(() => {
      settle(false)
      onDismissRef.current()
    }, remainingRef.current)
    return () => {
      if (timerRef.current != null) {
        window.clearTimeout(timerRef.current)
        timerRef.current = null
      }
    }
  }, [paused, leaving, toast.duration])

  useLayoutEffect(() => {
    const el = cardRef.current
    if (!el) return
    onMeasure(toast.id, el.getBoundingClientRect().height)
    const ro = new ResizeObserver((entries) => {
      for (const entry of entries) onMeasure(toast.id, entry.contentRect.height)
    })
    ro.observe(el)
    return () => ro.disconnect()
  }, [onMeasure, toast.id])

  useEffect(() => {
    if (reduceMotion) {
      setEntered(true)
      return
    }
    const id = requestAnimationFrame(() => setEntered(true))
    return () => cancelAnimationFrame(id)
  }, [reduceMotion])

  function handleDismiss(e?: MouseEvent<HTMLButtonElement>): void {
    e?.stopPropagation()
    settle(false)
    if (reduceMotion) {
      onDismiss()
      return
    }
    setLeaving(true)
    window.setTimeout(onDismiss, 220)
  }

  function handleAction(e: MouseEvent<HTMLButtonElement>): void {
    e.stopPropagation()
    settle(true)
    if (reduceMotion) {
      onDismiss()
      return
    }
    setLeaving(true)
    window.setTimeout(onDismiss, 220)
  }

  const semanticColor = typeToSemanticColor(toast.type)
  const targetY = expanded ? expandedTop : collapsedY
  const targetScale = expanded ? 1 : collapsedScale
  const targetOpacity = leaving ? 0 : !entered ? 0 : expanded ? 1 : collapsedOpacity
  const enterOffset = !entered ? 32 : leaving ? 16 : 0

  const cardStyle: CSSProperties = {
    position: 'absolute',
    top: 0,
    right: 0,
    left: 0,
    transform: `translateY(${targetY}px) translateX(${enterOffset}px) scale(${targetScale})`,
    transformOrigin: 'top right',
    opacity: targetOpacity,
    zIndex: 1000 - index,
    transition: reduceMotion
      ? 'none'
      : 'transform 360ms cubic-bezier(0.16, 1, 0.3, 1), opacity 220ms ease-out',
    pointerEvents: !expanded && index > 0 ? 'none' : 'auto',
    willChange: 'transform, opacity'
  }

  const surfaceStyle: CSSProperties = {
    position: 'relative',
    display: 'grid',
    gridTemplateColumns: toast.action ? 'auto minmax(0, 1fr) auto auto' : 'auto minmax(0, 1fr) auto',
    alignItems: 'flex-start',
    columnGap: '10px',
    padding: '12px',
    borderRadius: '12px',
    border: '1px solid var(--glass-border-strong)',
    background: 'var(--glass-surface-strong)',
    backdropFilter: 'var(--glass-blur)',
    WebkitBackdropFilter: 'var(--glass-blur)',
    boxShadow: 'var(--glass-shadow-soft)',
    color: 'var(--text-primary)',
    fontSize: '13px',
    lineHeight: 1.45,
    overflow: 'hidden',
    cursor: 'default',
    userSelect: 'none'
  }

  return (
    <div ref={cardRef} role="alert" style={cardStyle}>
      <div style={surfaceStyle}>
        <div
          aria-hidden
          style={{
            width: 28,
            height: 28,
            display: 'inline-flex',
            alignItems: 'center',
            justifyContent: 'center',
            borderRadius: 8,
            background: `color-mix(in srgb, ${semanticColor} 14%, transparent)`,
            color: semanticColor,
            flexShrink: 0,
            marginTop: 1
          }}
        >
          <ToastIcon type={toast.type} />
        </div>
        <div style={{ minWidth: 0, paddingTop: 2 }}>
          {toast.markdown ? (
            <div
              style={{
                maxHeight: 240,
                overflow: 'auto',
                fontSize: '13px',
                lineHeight: 1.5,
                color: 'var(--text-primary)',
                wordBreak: 'break-word'
              }}
            >
              <MarkdownRenderer content={toast.message} />
            </div>
          ) : (
            <p
              style={{
                margin: 0,
                fontSize: '13px',
                lineHeight: 1.45,
                color: 'var(--text-primary)',
                wordBreak: 'break-word'
              }}
            >
              {toast.message}
            </p>
          )}
        </div>
        {toast.action ? (
          <button
            type="button"
            onClick={handleAction}
            style={{
              display: 'inline-flex',
              alignItems: 'center',
              gap: 6,
              height: 26,
              marginTop: 1,
              padding: '0 10px',
              border: '1px solid var(--glass-border-strong)',
              borderRadius: 7,
              background: 'transparent',
              color: 'var(--text-primary)',
              font: 'inherit',
              fontSize: '12.5px',
              fontWeight: 600,
              whiteSpace: 'nowrap',
              cursor: 'pointer',
              flexShrink: 0,
              transition: 'background-color 120ms ease'
            }}
            onMouseEnter={(e) => {
              e.currentTarget.style.background = 'color-mix(in srgb, var(--text-primary) 8%, transparent)'
            }}
            onMouseLeave={(e) => {
              e.currentTarget.style.background = 'transparent'
            }}
          >
            <ToastActionIcon name={toast.action.icon} />
            <span>{toast.action.label}</span>
          </button>
        ) : null}
        <button
          type="button"
          aria-label={t('common.close')}
          onClick={handleDismiss}
          style={{
            display: 'inline-flex',
            alignItems: 'center',
            justifyContent: 'center',
            width: 22,
            height: 22,
            marginTop: 1,
            border: 'none',
            borderRadius: 6,
            background: 'transparent',
            color: 'var(--text-dimmed)',
            cursor: 'pointer',
            transition: 'background-color 120ms ease, color 120ms ease',
            flexShrink: 0
          }}
          onMouseEnter={(e) => {
            e.currentTarget.style.background =
              'color-mix(in srgb, var(--text-primary) 8%, transparent)'
            e.currentTarget.style.color = 'var(--text-primary)'
          }}
          onMouseLeave={(e) => {
            e.currentTarget.style.background = 'transparent'
            e.currentTarget.style.color = 'var(--text-dimmed)'
          }}
        >
          <X size={14} aria-hidden />
        </button>
        <ProgressBar
          color={semanticColor}
          durationMs={toast.duration}
          paused={paused}
          reduceMotion={reduceMotion}
        />
      </div>
    </div>
  )
}

function ProgressBar({
  color,
  durationMs,
  paused,
  reduceMotion
}: {
  color: string
  durationMs: number
  paused: boolean
  reduceMotion: boolean
}): JSX.Element | null {
  if (durationMs > PROGRESS_BAR_MAX_DURATION_MS || reduceMotion) return null
  return (
    <span
      aria-hidden
      style={{
        position: 'absolute',
        left: 12,
        right: 12,
        bottom: 6,
        height: 2,
        borderRadius: 2,
        background: `color-mix(in srgb, ${color} 22%, transparent)`,
        overflow: 'hidden'
      }}
    >
      <span
        style={{
          display: 'block',
          height: '100%',
          width: '100%',
          background: color,
          opacity: 0.7,
          transformOrigin: 'left center',
          animation: `dotcraft-toast-progress ${durationMs}ms linear forwards`,
          animationPlayState: paused ? 'paused' : 'running'
        }}
      />
    </span>
  )
}

function ToastIcon({ type }: { type: ToastType }): JSX.Element {
  if (type === 'success') return <CheckCircle2 size={16} strokeWidth={2.2} aria-hidden />
  if (type === 'warning') return <TriangleAlert size={16} strokeWidth={2.2} aria-hidden />
  if (type === 'error') return <XCircle size={16} strokeWidth={2.2} aria-hidden />
  return <Info size={16} strokeWidth={2.2} aria-hidden />
}

/** Resolves a toast action's named glyph; unknown/omitted names render no icon. */
function ToastActionIcon({ name }: { name?: string }): JSX.Element | null {
  if (name === 'undo') return <Undo2 size={14} aria-hidden />
  return null
}

function typeToSemanticColor(type: ToastType): string {
  switch (type) {
    case 'success':
      return 'var(--success)'
    case 'warning':
      return 'var(--warning)'
    case 'error':
      return 'var(--error)'
    default:
      return 'var(--info)'
  }
}

function prefersReducedMotion(): boolean {
  if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') return false
  return window.matchMedia('(prefers-reduced-motion: reduce)').matches
}
