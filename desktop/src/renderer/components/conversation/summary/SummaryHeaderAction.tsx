import type { DesktopPluginThreadSurfaceContext } from '@dotcraft/plugin'
import { useEffect, useLayoutEffect, useRef, useState, type JSX } from 'react'
import { createPortal } from 'react-dom'
import { useT } from '../../../contexts/LocaleContext'
import { IconButton } from '../../ui/IconButton'
import { SummaryIcon } from './SummaryIcon'
import { SummaryPanel } from './SummaryPanel'
import { desktopSummaryServices } from './summaryServices'
import { isPanelLayout, setPopoverOpen, toggleSummary, useSummaryView } from './view'

const POPOVER_WIDTH = 300
const POPOVER_GAP = 6
const VIEWPORT_INSET = 8

interface PopoverPosition {
  top: number
  right: number
  width: number
  maxHeight: number
}

function positionFor(anchor: HTMLElement): PopoverPosition {
  const rect = anchor.getBoundingClientRect()
  const top = rect.bottom + POPOVER_GAP
  return {
    top,
    right: Math.max(VIEWPORT_INSET, window.innerWidth - rect.right),
    width: Math.min(POPOVER_WIDTH, window.innerWidth - VIEWPORT_INSET * 2),
    maxHeight: Math.max(0, window.innerHeight - top - 12)
  }
}

export function SummaryHeaderAction({ context }: { context: DesktopPluginThreadSurfaceContext }): JSX.Element {
  const t = useT()
  const view = useSummaryView()
  const anchorRef = useRef<HTMLSpanElement>(null)
  const popoverRef = useRef<HTMLDivElement>(null)
  const [position, setPosition] = useState<PopoverPosition | null>(null)
  const panelMode = isPanelLayout(view.layout)
  const popoverShown = view.popoverOpen && !panelMode
  const label = panelMode ? t('summary.togglePinned') : t('summary.toggle')

  useEffect(() => {
    setPopoverOpen(false)
  }, [context.threadId])

  useLayoutEffect(() => {
    if (!popoverShown) return
    const anchor = anchorRef.current
    if (!anchor) return
    const place = (): void => setPosition(positionFor(anchor))
    place()
    window.addEventListener('resize', place)
    return () => window.removeEventListener('resize', place)
  }, [popoverShown])

  useEffect(() => {
    if (!popoverShown) return
    const onKey = (event: KeyboardEvent): void => {
      if (event.key !== 'Escape') return
      setPopoverOpen(false)
      anchorRef.current?.querySelector('button')?.focus()
    }
    const onPointer = (event: PointerEvent): void => {
      const target = event.target as Node | null
      if (target && (popoverRef.current?.contains(target) || anchorRef.current?.contains(target))) return
      setPopoverOpen(false)
    }
    document.addEventListener('keydown', onKey)
    document.addEventListener('pointerdown', onPointer, true)
    return () => {
      document.removeEventListener('keydown', onKey)
      document.removeEventListener('pointerdown', onPointer, true)
    }
  }, [popoverShown])

  return (
    <>
      <span ref={anchorRef} className="conversation-summary-toggle">
        <IconButton
          icon={<SummaryIcon />}
          label={label}
          tooltipLabel={label}
          size={28}
          tooltipPlacement="bottom"
          activeTone="neutral"
          active={panelMode ? view.pinned : popoverShown}
          aria-pressed={panelMode ? view.pinned : undefined}
          aria-expanded={panelMode ? undefined : popoverShown}
          aria-haspopup={panelMode ? undefined : 'dialog'}
          onClick={toggleSummary}
        />
      </span>
      {popoverShown && position
        ? createPortal(
            <div
              ref={popoverRef}
              className="conversation-summary-popover"
              role="dialog"
              aria-label={t('summary.panelLabel')}
              style={position}
            >
              <SummaryPanel
                key={context.threadId}
                services={desktopSummaryServices}
                threadId={context.threadId}
                workspacePath={context.workspacePath}
                variant="popover"
              />
            </div>,
            document.body
          )
        : null}
    </>
  )
}
