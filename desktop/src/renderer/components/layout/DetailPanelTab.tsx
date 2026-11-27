import {
  useLayoutEffect,
  useRef,
  useState,
  type KeyboardEvent,
  type ReactNode
} from 'react'
import { X } from 'lucide-react'
import { ActionTooltip } from '../ui/ActionTooltip'
import type { ContextMenuPosition } from '../ui/ContextMenu'

export interface DetailPanelTabProps {
  tabKey: string
  active: boolean
  title: string
  icon: ReactNode
  label: string
  badge?: number
  closeLabel: string
  id?: string
  ariaControls?: string
  onActivate: () => void
  onClose: () => void
  onContextMenu: (position: ContextMenuPosition) => void
  className?: string
}

export function DetailPanelTab({
  tabKey,
  active,
  title,
  icon,
  label,
  badge,
  closeLabel,
  id,
  ariaControls,
  onActivate,
  onClose,
  onContextMenu,
  className
}: DetailPanelTabProps): JSX.Element {
  const labelRef = useRef<HTMLSpanElement>(null)
  const [truncated, setTruncated] = useState(false)

  useLayoutEffect(() => {
    const el = labelRef.current
    if (!el) return
    const update = (): void => {
      // 1px tolerance avoids subpixel DPI rounding triggering the fade mask on fitting text.
      setTruncated(el.scrollWidth > el.clientWidth + 1)
    }
    update()
    const observer = new ResizeObserver(update)
    observer.observe(el)
    return () => observer.disconnect()
  }, [label])

  const handleKeyDown = (event: KeyboardEvent<HTMLButtonElement>): void => {
    if (event.key === 'ContextMenu' || (event.key === 'F10' && event.shiftKey)) {
      event.preventDefault()
      const rect = event.currentTarget.getBoundingClientRect()
      onContextMenu({ x: rect.left, y: rect.bottom + 4 })
      return
    }
    if (event.key === 'Delete') {
      event.preventDefault()
      onClose()
      return
    }
    if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return
    const tabs = Array.from(
      event.currentTarget.closest('[role="tablist"]')?.querySelectorAll<HTMLButtonElement>('[role="tab"]') ?? []
    )
    const currentIndex = tabs.indexOf(event.currentTarget)
    if (currentIndex < 0) return

    event.preventDefault()
    const nextIndex = event.key === 'Home'
      ? 0
      : event.key === 'End'
        ? tabs.length - 1
        : (currentIndex + (event.key === 'ArrowRight' ? 1 : -1) + tabs.length) % tabs.length
    const nextTab = tabs[nextIndex]
    nextTab.focus()
    nextTab.click()
  }

  return (
    <div
      className={className ? `dotcraft-detail-panel-tab-shell ${className}` : 'dotcraft-detail-panel-tab-shell'}
      data-tab-key={tabKey}
      data-active={active ? 'true' : undefined}
      data-has-badge={badge !== undefined ? 'true' : undefined}
      onContextMenu={(e) => {
        e.preventDefault()
        onContextMenu({ x: e.clientX, y: e.clientY })
      }}
      onAuxClick={(e) => {
        if (e.button === 1) {
          e.preventDefault()
          onClose()
        }
      }}
    >
      <ActionTooltip label={title} placement="bottom" wrapperClassName="dotcraft-detail-panel-tab__anchor">
        <button
          type="button"
          role="tab"
          id={id}
          aria-controls={ariaControls}
          aria-selected={active}
          tabIndex={active ? 0 : -1}
          className="dotcraft-detail-panel-tab"
          onClick={onActivate}
          onKeyDown={handleKeyDown}
        >
          <span className="dotcraft-detail-panel-tab__leading" aria-hidden="true">
            {icon}
          </span>
          <span
            ref={labelRef}
            className="dotcraft-detail-panel-tab__label"
            data-truncated={truncated ? 'true' : undefined}
          >
            {label}
          </span>
          {badge !== undefined && (
            <span className="dotcraft-detail-panel-tab__badge">
              {badge}
            </span>
          )}
        </button>
      </ActionTooltip>
      <button
        type="button"
        className="dotcraft-detail-panel-tab__close"
        aria-label={closeLabel}
        tabIndex={active ? 0 : -1}
        onClick={onClose}
      >
        <X size={12} strokeWidth={2} aria-hidden style={{ display: 'block' }} />
      </button>
    </div>
  )
}
