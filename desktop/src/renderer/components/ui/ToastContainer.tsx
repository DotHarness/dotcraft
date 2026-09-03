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
import { Button } from './Button'
import { IconButton } from './IconButton'

/** Vertical gap between toasts when the stack is expanded (hover/focus). */
const STACK_GAP_PX = 10

/** When collapsed, each older toast peeks this many pixels below the newest. */
const COLLAPSED_PEEK_PX = 10

/** When collapsed, each older toast scales down by this much per layer. */
const COLLAPSED_SCALE_STEP = 0.04

/** Number of older toasts that remain visible as peeking layers behind the newest. */
const COLLAPSED_VISIBLE_DEPTH = 3

/** Opacity transition length in primitives/toast.css. */
const LEAVE_MS = 220

/**
 * Toasts arrive as a fanned stack, newest in front, and expand to a full vertical
 * list on hover or focus, which also pauses auto-dismiss.
 */
export function ToastContainer(): JSX.Element {
  const toasts = useToastStore((s) => s.toasts)
  const [expanded, setExpanded] = useState(false)
  const t = useT()

  // Below titleBarOverlay so caption buttons do not cover the stack.
  const isMac = window.api.platform === 'darwin'
  const topPx = isMac ? 16 : window.api.titleBarOverlayHeight + 16

  // Newest toast first so it visually sits on top of the stack.
  const ordered = [...toasts].reverse()

  const heightsRef = useRef<Map<string, number>>(new Map())
  const [, forceRender] = useState(0)
  const setToastHeight = useCallback((id: string, height: number): void => {
    const prev = heightsRef.current.get(id)
    if (prev === height) return
    heightsRef.current.set(id, height)
    forceRender((n) => n + 1)
  }, [])

  const heights = ordered.map((toast) => heightsRef.current.get(toast.id) ?? 64)
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
    <section
      className="dc-toast-stack"
      aria-label={t('toast.regionLabel')}
      aria-live="polite"
      aria-relevant="additions text"
      aria-atomic="false"
      data-expanded={expanded ? 'true' : 'false'}
      data-empty={ordered.length === 0 ? 'true' : 'false'}
      onMouseEnter={() => setExpanded(true)}
      onMouseLeave={() => setExpanded(false)}
      onFocus={() => setExpanded(true)}
      onBlur={(e) => {
        if (!e.currentTarget.contains(e.relatedTarget as Node | null)) setExpanded(false)
      }}
      style={{ top: `${topPx}px`, height: `${containerHeight}px` }}
    >
      {ordered.map((toast, index) => {
        const depth = Math.min(index, COLLAPSED_VISIBLE_DEPTH)
        return (
          <ToastItem
            key={toast.id}
            toast={toast}
            index={index}
            expanded={expanded}
            expandedTop={expandedTops[index]}
            collapsedY={depth * COLLAPSED_PEEK_PX}
            collapsedScale={1 - depth * COLLAPSED_SCALE_STEP}
            collapsedOpacity={
              index === 0 ? 1 : index >= COLLAPSED_VISIBLE_DEPTH ? 0 : 1 - index * 0.25
            }
            onMeasure={setToastHeight}
          />
        )
      })}
    </section>,
    document.body
  ) as JSX.Element
}

interface ToastItemProps {
  toast: Toast
  index: number
  /** Expanded stacks also pause the dismiss timer. Driven by stack hover/focus. */
  expanded: boolean
  expandedTop: number
  collapsedY: number
  collapsedScale: number
  collapsedOpacity: number
  onMeasure: (id: string, height: number) => void
}

function ToastItem({
  toast,
  index,
  expanded,
  expandedTop,
  collapsedY,
  collapsedScale,
  collapsedOpacity,
  onMeasure
}: ToastItemProps): JSX.Element {
  const cardRef = useRef<HTMLDivElement | null>(null)
  const [entered, setEntered] = useState(false)
  const [leaving, setLeaving] = useState(false)
  const t = useT()
  const reduceMotion = reducedMotionActive()
  const settleToast = useToastStore((s) => s.settleToast)
  const removeToast = useToastStore((s) => s.removeToast)

  // Pausable auto-dismiss timer: tracks remaining time so resuming after a hover
  // does not lose the time that already elapsed before the user hovered.
  const remainingRef = useRef(toast.duration)
  const startedAtRef = useRef<number | null>(null)
  const timerRef = useRef<number | null>(null)

  const leave = useCallback(
    (via: 'action' | 'expire'): void => {
      // Settle before the fade so a commit fires exactly when the window closes.
      settleToast(toast.id, via)
      if (reduceMotion) {
        removeToast(toast.id)
        return
      }
      setLeaving(true)
      window.setTimeout(() => removeToast(toast.id), LEAVE_MS)
    },
    [reduceMotion, removeToast, settleToast, toast.id]
  )

  useEffect(() => {
    if (toast.duration <= 0) return
    if (leaving) return
    if (expanded) {
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
    timerRef.current = window.setTimeout(() => leave('expire'), remainingRef.current)
    return () => {
      if (timerRef.current != null) {
        window.clearTimeout(timerRef.current)
        timerRef.current = null
      }
    }
  }, [expanded, leaving, leave, toast.duration])

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

  function handleDismiss(e: MouseEvent<HTMLButtonElement>): void {
    e.stopPropagation()
    leave('expire')
  }

  function handleAction(e: MouseEvent<HTMLButtonElement>): void {
    e.stopPropagation()
    leave('action')
  }

  const targetY = expanded ? expandedTop : collapsedY
  const targetScale = expanded ? 1 : collapsedScale
  const targetOpacity = leaving ? 0 : !entered ? 0 : expanded ? 1 : collapsedOpacity
  const enterOffset = !entered ? 32 : leaving ? 16 : 0

  const cardStyle: CSSProperties = {
    transform: `translateY(${targetY}px) translateX(${enterOffset}px) scale(${targetScale})`,
    opacity: targetOpacity,
    zIndex: 1000 - index
  }

  return (
    <div
      ref={cardRef}
      className="dc-toast"
      data-behind={!expanded && index > 0 ? 'true' : undefined}
      data-leaving={leaving ? 'true' : undefined}
      style={cardStyle}
    >
      <div
        className="dc-toast__surface"
        data-type={toast.type}
        data-has-action={toast.action ? 'true' : undefined}
      >
        <span className="dc-toast__icon" data-type={toast.type} aria-hidden>
          <ToastIcon type={toast.type} />
        </span>
        <div className="dc-toast__body">
          {toast.markdown ? (
            <div className="dc-toast__markdown">
              <MarkdownRenderer content={toast.message} />
            </div>
          ) : (
            <p>{toast.message}</p>
          )}
        </div>
        {toast.action ? (
          <Button
            variant="ghost"
            size="sm"
            className="dc-toast__action"
            iconLeft={<ToastActionIcon name={toast.action.icon} />}
            onClick={handleAction}
          >
            {toast.action.label}
          </Button>
        ) : null}
        <IconButton
          className="dc-toast__close"
          icon={<X size={14} aria-hidden />}
          label={t('common.close')}
          size={28}
          radius={8}
          onClick={handleDismiss}
        />
      </div>
    </div>
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

/** Mirrors shared/reduced-motion.css: the app setting wins, the OS preference is the default. */
function reducedMotionActive(): boolean {
  if (typeof document === 'undefined') return false
  const setting = document.documentElement.dataset.reduceMotion
  if (setting === 'on') return true
  if (setting === 'off') return false
  return typeof window.matchMedia === 'function' &&
    window.matchMedia('(prefers-reduced-motion: reduce)').matches
}
