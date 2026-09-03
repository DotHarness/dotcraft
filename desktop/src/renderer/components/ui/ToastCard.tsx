import {
  useCallback,
  useEffect,
  useLayoutEffect,
  useRef,
  useState,
  type CSSProperties,
  type MouseEvent
} from 'react'
import { X, CheckCircle2, Info, TriangleAlert, Undo2, XCircle } from 'lucide-react'
import { useToastStore, type Toast, type ToastType } from '../../stores/toastStore'
import { MarkdownRenderer } from '../conversation/MarkdownRenderer'
import { useT } from '../../contexts/LocaleContext'
import { Button } from './Button'
import { IconButton } from './IconButton'
import { IdentityMark } from './IdentityMark'

/* Matches the opacity transition in primitives/toast.css. */
const LEAVE_MS = 220

export interface ToastCardProps {
  toast: Toast
  index: number
  expanded: boolean
  expandedTop: number
  collapsedY: number
  collapsedScale: number
  collapsedOpacity: number
  onMeasure: (id: string, height: number) => void
}

export function ToastCard({
  toast,
  index,
  expanded,
  expandedTop,
  collapsedY,
  collapsedScale,
  collapsedOpacity,
  onMeasure
}: ToastCardProps): JSX.Element {
  const cardRef = useRef<HTMLDivElement | null>(null)
  const [entered, setEntered] = useState(false)
  const [leaving, setLeaving] = useState(false)
  const t = useT()
  const reduceMotion = reducedMotionActive()
  const settleToast = useToastStore((s) => s.settleToast)
  const removeToast = useToastStore((s) => s.removeToast)

  const remainingRef = useRef(toast.duration)
  const startedAtRef = useRef<number | null>(null)
  const timerRef = useRef<number | null>(null)

  const leave = useCallback(
    (via: 'action' | 'expire'): void => {
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

  const stackedAction = toast.action != null && toast.description != null
  // An element that renders nothing still costs Button its icon span and gap.
  const actionIcon = toastActionIcon(toast.action?.icon)
  const actionButton = toast.action ? (
    <Button
      variant="secondary"
      size="sm"
      className="dc-toast__action"
      {...(actionIcon ? { iconLeft: actionIcon } : {})}
      onClick={handleAction}
    >
      {toast.action.label}
    </Button>
  ) : null

  return (
    <div
      ref={cardRef}
      className="dc-toast"
      data-behind={!expanded && index > 0 ? 'true' : undefined}
      data-leaving={leaving ? 'true' : undefined}
      style={cardStyle}
    >
      <div className="dc-toast__surface" data-tone={toast.type === 'info' ? undefined : toast.type}>
        <div className="dc-toast__head">
          <span className="dc-toast__icon" data-mark={toast.leading ? 'true' : undefined} aria-hidden>
            {toast.leading ? (
              <IdentityMark role="compact" size={20} src={toast.leading.src} fallback={toast.leading.fallback} />
            ) : (
              <ToastIcon type={toast.type} />
            )}
          </span>
          <div className="dc-toast__body">
            {toast.markdown ? (
              <div className="dc-toast__markdown">
                <MarkdownRenderer content={toast.message} />
              </div>
            ) : (
              <p className="dc-toast__title">{toast.message}</p>
            )}
            {toast.description ? (
              <p className="dc-toast__description">{toast.description}</p>
            ) : null}
          </div>
          {stackedAction ? null : actionButton}
          <IconButton
            className="dc-toast__close"
            icon={<X size={14} aria-hidden />}
            label={t('common.close')}
            size={24}
            radius={8}
            onClick={handleDismiss}
          />
        </div>
        {stackedAction ? <div className="dc-toast__actions">{actionButton}</div> : null}
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

function toastActionIcon(name?: string): JSX.Element | null {
  if (name === 'undo') return <Undo2 size={14} aria-hidden />
  return null
}

function reducedMotionActive(): boolean {
  if (typeof document === 'undefined') return false
  const setting = document.documentElement.dataset.reduceMotion
  if (setting === 'on') return true
  if (setting === 'off') return false
  return typeof window.matchMedia === 'function' &&
    window.matchMedia('(prefers-reduced-motion: reduce)').matches
}
