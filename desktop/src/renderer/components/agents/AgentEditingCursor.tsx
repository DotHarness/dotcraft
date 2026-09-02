import { useEffect, useLayoutEffect, useRef, useState, type JSX, type ReactNode } from 'react'
import { MousePointer2 } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { BUILDER_FIELD_LABEL_KEYS, type BuilderField } from './agentBuilderDraftSync'
import './AgentEditingCursor.css'

export type AgentEditingPhase = 'editing' | 'settled'

const ANCHOR_ATTR = 'data-builder-field-anchor'
const MARKER_TARGET_SELECTOR = '[data-agent-builder-marker-target]'
const MARKER_FALLBACK_SELECTOR = [
  MARKER_TARGET_SELECTOR,
  '.dc-settings-select__value',
  'input',
  'textarea',
  '.agent-builder-chip-label',
  '.agent-builder-pick-empty',
  '.agent-builder-add'
].join(', ')
const MARKER_OFFSET_X_ATTR = 'data-agent-builder-marker-offset-x'
const MARKER_OFFSET_Y_ATTR = 'data-agent-builder-marker-offset-y'
const MARKER_FLIP_PAD = 12
const SETTLED_LABEL_MS = 2400

/** Positional anchor for one builder field; the cursor finds it by the field attribute. */
export function FieldAnchor({
  field,
  className,
  children
}: {
  field: BuilderField
  className?: string
  children: ReactNode
}): JSX.Element {
  return (
    <div className={`agent-builder-field-anchor${className ? ` ${className}` : ''}`} data-builder-field-anchor={field}>
      {children}
    </div>
  )
}

interface AgentEditingCursorProps {
  field: BuilderField | null
  phase: AgentEditingPhase
}

/** One cursor per document: it glides between field anchors instead of remounting. */
export function AgentEditingCursor({ field, phase }: AgentEditingCursorProps): JSX.Element | null {
  const t = useT()
  const ref = useRef<HTMLSpanElement>(null)
  const [placed, setPlaced] = useState(false)
  const [labelShown, setLabelShown] = useState(true)

  useEffect(() => {
    if (!field) return undefined
    setLabelShown(true)
    if (phase === 'editing') return undefined
    const timer = window.setTimeout(() => setLabelShown(false), SETTLED_LABEL_MS)
    return () => window.clearTimeout(timer)
  }, [field, phase])

  useLayoutEffect(() => {
    const cursor = ref.current
    if (!field || !cursor) return undefined
    const doc = cursor.parentElement
    if (!doc) return undefined
    let frame = 0
    let placeFrame = 0

    const findAnchor = (): HTMLElement | null => doc.querySelector<HTMLElement>(`[${ANCHOR_ATTR}="${field}"]`)

    const measure = (): void => {
      const anchor = findAnchor()
      const target = anchor?.querySelector<HTMLElement>(MARKER_FALLBACK_SELECTOR) ?? anchor
      if (!anchor || !target) return
      const docRect = doc.getBoundingClientRect()
      const targetRect = target.getBoundingClientRect()
      const x = targetRect.left - docRect.left + markerContentEnd(target) + (markerOffset(target, MARKER_OFFSET_X_ATTR) ?? 0)
      const y = targetRect.top - docRect.top + (markerOffset(target, MARKER_OFFSET_Y_ATTR) ?? targetRect.height / 2)
      cursor.style.setProperty('--agent-builder-cursor-x', `${Math.round(x)}px`)
      cursor.style.setProperty('--agent-builder-cursor-y', `${Math.round(y)}px`)
      // Flip the label leftward when it would overflow the document.
      cursor.classList.toggle('is-marker-flipped', x + cursor.offsetWidth + MARKER_FLIP_PAD > docRect.width)
    }

    const scheduleMeasure = (): void => {
      if (frame) window.cancelAnimationFrame(frame)
      frame = window.requestAnimationFrame(measure)
    }

    // Position before showing, so the glide only ever runs between two fields.
    measure()
    if (!placed) placeFrame = window.requestAnimationFrame(() => setPlaced(true))

    const anchor = findAnchor()
    const target = anchor?.querySelector<HTMLElement>(MARKER_FALLBACK_SELECTOR) ?? null
    const observer = typeof ResizeObserver !== 'undefined' ? new ResizeObserver(scheduleMeasure) : null
    observer?.observe(doc)
    if (anchor) observer?.observe(anchor)
    if (target) observer?.observe(target)
    window.addEventListener('resize', scheduleMeasure)
    window.addEventListener('scroll', scheduleMeasure, true)
    return () => {
      if (frame) window.cancelAnimationFrame(frame)
      if (placeFrame) window.cancelAnimationFrame(placeFrame)
      observer?.disconnect()
      window.removeEventListener('resize', scheduleMeasure)
      window.removeEventListener('scroll', scheduleMeasure, true)
    }
  })

  if (!field) return null

  const fieldLabel = t(BUILDER_FIELD_LABEL_KEYS[field])
  const label = t(
    phase === 'editing' ? 'agentBuilder.editing.updatingField' : 'agentBuilder.editing.updatedField',
    { field: fieldLabel }
  )

  return (
    <span
      ref={ref}
      className="agent-builder-edit-cursor"
      role="status"
      aria-label={label}
      data-agent-builder-cursor-field={field}
      data-phase={phase}
      data-placed={placed ? 'true' : 'false'}
      data-label={labelShown ? 'shown' : 'hidden'}
    >
      <MousePointer2 className="agent-builder-edit-cursor-arrow" size={17} aria-hidden />
      <span className="agent-builder-edit-cursor-pill">{label}</span>
    </span>
  )
}

function markerOffset(target: HTMLElement, attr: string): number | null {
  const value = target.getAttribute(attr)
  if (value == null) return null
  const parsed = Number.parseFloat(value)
  return Number.isFinite(parsed) ? parsed : null
}

let markerTextCanvas: HTMLCanvasElement | null = null

// Rendered width of one line, for finding where text ends inside a full-width field.
function markerTextWidth(el: HTMLElement, line: string): number {
  try {
    const canvas = (markerTextCanvas ??= document.createElement('canvas'))
    const ctx = canvas.getContext('2d')
    if (!ctx) return 0
    const style = window.getComputedStyle(el)
    ctx.font = `${style.fontWeight} ${style.fontSize} ${style.fontFamily}`
    return ctx.measureText(line).width
  } catch {
    return 0
  }
}

// Text fields span the full width, so measure their text; other targets hug their content.
function markerContentEnd(target: HTMLElement): number {
  if (target instanceof HTMLInputElement || target instanceof HTMLTextAreaElement) {
    const style = window.getComputedStyle(target)
    const paddingLeft = Number.parseFloat(style.paddingLeft) || 0
    const paddingRight = Number.parseFloat(style.paddingRight) || 0
    const raw = target.value || target.placeholder || ''
    const line = target instanceof HTMLTextAreaElement ? (raw.split('\n')[0] ?? '') : raw
    const end = paddingLeft + markerTextWidth(target, line)
    const maxEnd = target.clientWidth - paddingRight
    return maxEnd > 0 ? Math.min(end, maxEnd) : end
  }
  return target.getBoundingClientRect().width
}
