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
  const child = Children.only(children)
  const describedChild = isValidElement(child)
    ? cloneElement(child as ReactElement<HTMLAttributes<HTMLElement>>, {
        'aria-describedby': visible ? tooltipId : undefined
      })
    : child

  useLayoutEffect(() => {
    if (!visible) return
    const anchor = anchorRef.current
    const tooltip = overlayRef.current
    if (!anchor || !tooltip) return

    const anchorRect = anchor.getBoundingClientRect()
    const tooltipRect = tooltip.getBoundingClientRect()
    setPosition(placeTooltip(anchorRect, tooltipRect, placement))
  }, [visible, placement, tooltipLabel, shortcut, alternateShortcuts])

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
      {visible && createPortal(
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

function placeTooltip(
  anchor: DOMRect,
  tooltip: DOMRect,
  placement: TooltipPlacement
): TooltipPosition {
  let left = anchor.left + anchor.width / 2 - tooltip.width / 2
  let top = anchor.top - tooltip.height - GAP
  let transform: string | undefined

  if (placement === 'bottom') {
    top = anchor.bottom + GAP
  } else if (placement === 'left') {
    left = anchor.left - tooltip.width - GAP
    top = anchor.top + anchor.height / 2 - tooltip.height / 2
  } else if (placement === 'right') {
    left = anchor.right + GAP
    top = anchor.top + anchor.height / 2 - tooltip.height / 2
  }

  left = clamp(left, VIEWPORT_PADDING, window.innerWidth - tooltip.width - VIEWPORT_PADDING)
  top = clamp(top, VIEWPORT_PADDING, window.innerHeight - tooltip.height - VIEWPORT_PADDING)

  if (placement === 'top' || placement === 'bottom') transform = 'translateZ(0)'
  return { left, top, transform }
}

function clamp(value: number, min: number, max: number): number {
  if (max < min) return min
  return Math.min(Math.max(value, min), max)
}
