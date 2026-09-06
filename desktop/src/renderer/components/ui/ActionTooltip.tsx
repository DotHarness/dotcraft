import {
  Children,
  cloneElement,
  isValidElement,
  useId,
  useLayoutEffect,
  useState,
  type CSSProperties,
  type HTMLAttributes,
  type JSX,
  type ReactElement,
  type ReactNode
} from 'react'
import { createPortal } from 'react-dom'
import { useTransientOverlay } from '../../hooks/useTransientOverlay'
import type { ShortcutSpec } from './shortcutKeys'
import { ShortcutBadge } from './ShortcutBadge'

type TooltipPlacement = 'top' | 'bottom' | 'left' | 'right'

interface ActionTooltipProps {
  /** Empty shows nothing while keeping the wrapper, so the control is not remounted. */
  label: string
  shortcut?: ShortcutSpec
  alternateShortcuts?: readonly ShortcutSpec[]
  placement?: TooltipPlacement
  disabledReason?: string
  multiline?: boolean
  children: ReactNode
  wrapperStyle?: CSSProperties
}

interface TooltipPosition {
  left: number
  top: number
  transform?: string
}

const VIEWPORT_PADDING = 8
const GAP = 8

export function ActionTooltip({
  label,
  shortcut,
  alternateShortcuts,
  placement = 'top',
  disabledReason,
  multiline = false,
  children,
  wrapperStyle
}: ActionTooltipProps): JSX.Element {
  const tooltipId = useId()
  const { visible, anchorRef, overlayRef, open, hide } = useTransientOverlay<HTMLSpanElement, HTMLDivElement>()
  const [position, setPosition] = useState<TooltipPosition>({ left: 0, top: 0 })
  const tooltipLabel = disabledReason || label
  const shown = visible && tooltipLabel.trim() !== ''
  const child = Children.only(children)
  const describedChild = isValidElement(child)
    ? cloneElement(child as ReactElement<HTMLAttributes<HTMLElement>>, {
        'aria-describedby': shown ? tooltipId : undefined
      })
    : child

  useLayoutEffect(() => {
    if (!shown) return
    const anchor = anchorRef.current
    const tooltip = overlayRef.current
    if (!anchor || !tooltip) return

    const tooltipRect = tooltip.getBoundingClientRect()
    setPosition(placeTooltip(anchorRect(anchor), tooltipRect, placement))
  }, [shown, placement, tooltipLabel, shortcut, alternateShortcuts])

  const shortcutGroups = !disabledReason ? [shortcut, ...(alternateShortcuts ?? [])].filter(Boolean) as ShortcutSpec[] : []

  return (
    <>
      <span
        ref={anchorRef}
        onMouseEnter={open}
        onMouseLeave={hide}
        onFocusCapture={open}
        onBlurCapture={hide}
        style={{
          display: 'inline-flex',
          flexShrink: 0,
          ...wrapperStyle
        }}
      >
        {describedChild}
      </span>
      {shown && createPortal(
        <div
          id={tooltipId}
          ref={overlayRef}
          role="tooltip"
          className={multiline ? 'dc-action-tooltip dc-action-tooltip--multiline' : 'dc-action-tooltip'}
          data-multiline={multiline ? 'true' : undefined}
          style={{
            position: 'fixed',
            left: position.left,
            top: position.top,
            transform: position.transform,
            zIndex: 'var(--z-tooltip)',
            pointerEvents: 'none'
          }}
        >
          <span className="dc-action-tooltip__label">{tooltipLabel}</span>
          {shortcutGroups.length > 0 && (
            <span aria-hidden="true" className="dc-action-tooltip__shortcuts">
              {shortcutGroups.map((shortcutGroup, index) => (
                <span key={`${shortcutGroup.join('-')}-${index}`} className="dc-action-tooltip__group">
                  {index > 0 && <span className="dc-action-tooltip__alt-separator">/</span>}
                  <ShortcutBadge shortcut={shortcutGroup} />
                </span>
              ))}
            </span>
          )}
        </div>,
        document.body
      )}
    </>
  )
}

/**
 * A control that leaves the flow — a hover-revealed row action pinned to its
 * row's trailing edge — collapses this wrapper to nothing, which would place the
 * tooltip against a phantom point. Measure what is drawn instead.
 */
function anchorRect(anchor: HTMLElement): DOMRect {
  const rect = anchor.getBoundingClientRect()
  if (rect.width > 0 && rect.height > 0) return rect
  const child = anchor.firstElementChild
  return child ? child.getBoundingClientRect() : rect
}

export function placeTooltip(
  anchor: DOMRect,
  tooltip: DOMRect,
  placement: TooltipPlacement,
  viewportWidth: number = window.innerWidth,
  viewportHeight: number = window.innerHeight
): TooltipPosition {
  const side = resolvePlacement(anchor, tooltip, placement, viewportWidth, viewportHeight)
  let left = anchor.left + anchor.width / 2 - tooltip.width / 2
  let top = anchor.top - tooltip.height - GAP
  let transform: string | undefined

  if (side === 'bottom') {
    top = anchor.bottom + GAP
  } else if (side === 'left') {
    left = anchor.left - tooltip.width - GAP
    top = anchor.top + anchor.height / 2 - tooltip.height / 2
  } else if (side === 'right') {
    left = anchor.right + GAP
    top = anchor.top + anchor.height / 2 - tooltip.height / 2
  }

  left = clamp(left, VIEWPORT_PADDING, viewportWidth - tooltip.width - VIEWPORT_PADDING)
  top = clamp(top, VIEWPORT_PADDING, viewportHeight - tooltip.height - VIEWPORT_PADDING)

  if (side === 'top' || side === 'bottom') transform = 'translateZ(0)'
  return { left, top, transform }
}

/**
 * Mirroring happens inside an axis, never across it: a tooltip asked for the
 * block axis stays on it even when it has to flip, because the inline axis
 * beside a row belongs to that row's details card. Keep the requested side when
 * neither fits and let clamping resolve it.
 */
function resolvePlacement(
  anchor: DOMRect,
  tooltip: DOMRect,
  placement: TooltipPlacement,
  viewportWidth: number,
  viewportHeight: number
): TooltipPlacement {
  const fitsTop = anchor.top - tooltip.height - GAP >= VIEWPORT_PADDING
  const fitsBottom = anchor.bottom + tooltip.height + GAP <= viewportHeight - VIEWPORT_PADDING
  const fitsLeft = anchor.left - tooltip.width - GAP >= VIEWPORT_PADDING
  const fitsRight = anchor.right + tooltip.width + GAP <= viewportWidth - VIEWPORT_PADDING

  switch (placement) {
    case 'top':
      return fitsTop || !fitsBottom ? 'top' : 'bottom'
    case 'bottom':
      return fitsBottom || !fitsTop ? 'bottom' : 'top'
    case 'left':
      return fitsLeft || !fitsRight ? 'left' : 'right'
    case 'right':
      return fitsRight || !fitsLeft ? 'right' : 'left'
  }
}

function clamp(value: number, min: number, max: number): number {
  if (max < min) return min
  return Math.min(Math.max(value, min), max)
}
