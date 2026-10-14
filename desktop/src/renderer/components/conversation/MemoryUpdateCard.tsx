import { useLayoutEffect, useState, type JSX } from 'react'
import { createPortal } from 'react-dom'
import { Brain, ChevronRight } from 'lucide-react'
import { translate } from '../../../shared/locales'
import { useLocale } from '../../contexts/LocaleContext'
import { useTransientOverlay } from '../../hooks/useTransientOverlay'
import { useUIStore } from '../../stores/uiStore'
import type { ConversationItem } from '../../types/conversation'
import { collectMemoryFileNames, formatMemoryUpdatedLabel } from '../../utils/memoryToolDisplay'
import { placeTooltip } from '../ui/ActionTooltip'
import { RunningShimmer } from '../ui/RunningShimmer'
import { ToolDisclosure } from './ToolDisclosure'
import styles from './MemoryUpdateCard.module.css'

const MEMORY_ICON = { icon: Brain }

interface MemoryUpdateCardProps {
  items: readonly ConversationItem[]
  running: boolean
}

export function MemoryUpdateCard({ items, running }: MemoryUpdateCardProps): JSX.Element {
  const locale = useLocale()
  if (!running) return <MemoryUpdatedButton items={items} />
  return (
    <ToolDisclosure
      expanded={false}
      onToggle={() => {}}
      expandable={false}
      icon={MEMORY_ICON}
      title={(
        <RunningShimmer className="dc-tool-row-running-label">
          {translate(locale, 'toolCall.memory.updating')}
        </RunningShimmer>
      )}
    />
  )
}

function openMemorySettings(): void {
  const ui = useUIStore.getState()
  ui.setActiveSettingsTab('personalization')
  ui.setActiveMainView('settings')
}

function MemoryUpdatedButton({ items }: { items: readonly ConversationItem[] }): JSX.Element {
  const locale = useLocale()
  const names = collectMemoryFileNames(items)
  const label = formatMemoryUpdatedLabel(names.length, locale)
  const popover = useTransientOverlay<HTMLButtonElement, HTMLDivElement>({ interactive: true })
  const [position, setPosition] = useState({ left: 0, top: 0 })

  useLayoutEffect(() => {
    const anchor = popover.anchorRef.current
    const overlay = popover.overlayRef.current
    if (!popover.visible || !anchor || !overlay) return
    setPosition(placeTooltip(anchor.getBoundingClientRect(), overlay.getBoundingClientRect(), 'bottom'))
  }, [popover.visible, popover.anchorRef, popover.overlayRef])

  return (
    <>
      <button
        ref={popover.anchorRef}
        type="button"
        className={styles.trigger}
        aria-haspopup="dialog"
        aria-expanded={popover.visible}
        onClick={() => (popover.visible ? popover.hide() : popover.open())}
      >
        <span className="dc-tool-row-title">
          <Brain className="dc-tool-row-icon" size={16} strokeWidth={1.6} aria-hidden />
          <span className="dc-tool-row-text">{label}</span>
        </span>
      </button>
      {popover.visible && createPortal(
        <div
          ref={popover.overlayRef}
          role="dialog"
          aria-label={label}
          className={styles.popover}
          style={{ left: position.left, top: position.top }}
        >
          {names.length > 0 && (
            <ul className={styles.memories}>
              {names.map((name) => <li key={name} className={styles.memory}>{name}</li>)}
            </ul>
          )}
          <button
            type="button"
            className={styles.manage}
            onClick={() => {
              popover.hide()
              openMemorySettings()
            }}
          >
            {translate(locale, 'toolCall.memory.manage')}
            <ChevronRight size={14} aria-hidden />
          </button>
        </div>,
        document.body
      )}
    </>
  )
}
