import { useEffect, useRef, useState, type CSSProperties, type ReactNode } from 'react'
import { ArrowDownUp } from 'lucide-react'
import type { SidebarThreadSortMode } from '../../../shared/sidebarThreadOrder'
import { useT } from '../../contexts/LocaleContext'
import { DisclosureChevron } from '../ui/DisclosureChevron'
import { Skeleton } from '../ui/Skeleton'
import {
  ContextMenu,
  type ContextMenuEntry,
  type ContextMenuItem,
  type ContextMenuPosition
} from '../ui/ContextMenu'
import { MoreActionsButton } from '../ui/MoreActionsButton'
import { SIDEBAR_RAIL_CONTENT_INSET } from './sidebarNavRowStyles'

export function SectionOptionsMenu({
  label,
  items,
  onOpenChange
}: {
  label: string
  items: ContextMenuEntry[]
  onOpenChange?: (open: boolean) => void
}): JSX.Element {
  const [position, setPosition] = useState<ContextMenuPosition | null>(null)

  function update(next: ContextMenuPosition | null): void {
    setPosition(next)
    onOpenChange?.(next != null)
  }

  return (
    <>
      <MoreActionsButton
        label={label}
        size={24}
        radius={6}
        iconSize={15}
        className="dc-thread-list-icon-button"
        open={position != null}
        // The menu closes on outside mousedown; keep that from reopening it on click.
        onMouseDown={(event) => { if (position) event.stopPropagation() }}
        onClick={(event) => {
          event.stopPropagation()
          if (position) {
            update(null)
            return
          }
          const rect = event.currentTarget.getBoundingClientRect()
          update({ x: rect.left, y: rect.bottom + 4 })
        }}
      />
      {position && <ContextMenu position={position} items={items} onClose={() => update(null)} />}
    </>
  )
}

export function sortChatsMenuEntry(
  t: ReturnType<typeof useT>,
  mode: SidebarThreadSortMode,
  onSelect: (mode: SidebarThreadSortMode) => void
): ContextMenuItem {
  return {
    label: t('threadList.sortChatsBy'),
    icon: <ArrowDownUp size={14} aria-hidden />,
    onClick: () => {},
    submenu: [
      {
        label: t('threadList.sortLastUpdated'),
        selection: 'radio',
        checked: mode === 'updated',
        onClick: () => onSelect('updated')
      },
      {
        label: t('threadList.sortManual'),
        selection: 'radio',
        checked: mode === 'manual',
        onClick: () => onSelect('manual')
      }
    ]
  }
}

/** Callers gate `visible` on their own hover/focus state, so the chevron only appears while their header or row is hovered. */
export function CollapseChevron({ collapsed, visible }: { collapsed: boolean; visible: boolean }): JSX.Element {
  return (
    <span
      aria-hidden
      style={{
        display: 'inline-flex',
        flexShrink: 0,
        color: 'var(--text-dimmed)',
        opacity: visible ? 1 : 0,
        transition: 'opacity 120ms ease'
      }}
    >
      <DisclosureChevron expanded={!collapsed} />
    </span>
  )
}

export function SidebarSectionHeader({
  label,
  toggleLabel,
  collapsed,
  onToggle,
  actionsOpen = false,
  actions
}: {
  label: string
  toggleLabel: string
  collapsed: boolean
  onToggle: () => void
  actionsOpen?: boolean
  actions: ReactNode
}): JSX.Element {
  const [hovered, setHovered] = useState(false)
  const [focused, setFocused] = useState(false)
  const showActions = hovered || focused || actionsOpen

  return (
    <div
      role="button"
      tabIndex={0}
      aria-expanded={!collapsed}
      aria-label={toggleLabel}
      onClick={onToggle}
      onKeyDown={(event) => {
        if (event.target !== event.currentTarget) return
        if (event.key !== 'Enter' && event.key !== ' ') return
        event.preventDefault()
        onToggle()
      }}
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
      onFocus={() => setFocused(true)}
      onBlur={(event) => {
        if (!event.currentTarget.contains(event.relatedTarget as Node | null)) {
          setFocused(false)
        }
      }}
      style={{ ...sidebarSectionHeaderStyle, position: 'relative' }}
    >
      <span
        style={{
          color: 'var(--text-secondary)',
          fontSize: 'var(--type-secondary-size)',
          lineHeight: 'var(--type-secondary-line-height)',
          fontWeight: 'var(--type-ui-emphasis-weight)'
        }}
      >
        {label}
      </span>
      <CollapseChevron collapsed={collapsed} visible={showActions} />
      <div
        onClick={(event) => event.stopPropagation()}
        style={{
          marginLeft: 'auto',
          display: 'inline-flex',
          alignItems: 'center',
          justifyContent: 'flex-end',
          gap: '4px',
          opacity: showActions ? 1 : 0,
          pointerEvents: showActions ? 'auto' : 'none',
          transition: 'opacity 120ms ease'
        }}
      >
        {actions}
      </div>
    </div>
  )
}

const PROJECT_COLLAPSE_MS = 260
const PROJECT_COLLAPSE_TRANSITION =
  `grid-template-rows ${PROJECT_COLLAPSE_MS}ms cubic-bezier(0.4, 0, 0.2, 1), opacity 180ms ease`

/**
 * The wrapper stays mounted so both directions animate via `grid-template-rows:
 * 1fr ↔ 0fr`; only the rows inside unmount, after the collapse transition.
 * `transitionend` drives that unmount, with a timer for when it never fires.
 */
export function CollapsibleThreads({
  collapsed,
  marginTop = -2,
  children
}: {
  collapsed: boolean
  /**
   * Top margin (px) used to cancel the preceding header's bottom margin. Defaults
   * to -2 for the per-project list; group-level wrappers pass 0 because their
   * section headers carry no bottom margin.
   */
  marginTop?: number
  children: ReactNode
}): JSX.Element {
  const [present, setPresent] = useState(!collapsed)
  const [open, setOpen] = useState(!collapsed)
  const closeTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  const rafRef = useRef<number | null>(null)

  useEffect(() => {
    const clearClose = (): void => {
      if (closeTimerRef.current != null) {
        clearTimeout(closeTimerRef.current)
        closeTimerRef.current = null
      }
    }
    const clearRaf = (): void => {
      if (rafRef.current != null) {
        cancelAnimationFrame(rafRef.current)
        rafRef.current = null
      }
    }
    clearClose()
    clearRaf()
    if (!collapsed) {
      setPresent(true)
      // The wrapper is already mounted at 0fr; flip to 1fr next frame so the
      // height transitions in instead of snapping.
      rafRef.current = requestAnimationFrame(() => {
        setOpen(true)
        rafRef.current = null
      })
    } else {
      setOpen(false)
      closeTimerRef.current = setTimeout(() => {
        setPresent(false)
        closeTimerRef.current = null
      }, PROJECT_COLLAPSE_MS + 80)
    }
    return () => {
      clearClose()
      clearRaf()
    }
  }, [collapsed])

  return (
    <div
      style={{
        display: 'grid',
        gridTemplateRows: open ? '1fr' : '0fr',
        opacity: open ? 1 : 0,
        marginTop: `${marginTop}px`,
        transition: PROJECT_COLLAPSE_TRANSITION
      }}
      onTransitionEnd={(event) => {
        if (event.propertyName === 'grid-template-rows' && collapsed) {
          if (closeTimerRef.current != null) {
            clearTimeout(closeTimerRef.current)
            closeTimerRef.current = null
          }
          setPresent(false)
        }
      }}
    >
      <div style={{ overflow: 'hidden', minWidth: 0 }} inert={collapsed}>
        {present ? children : null}
      </div>
    </div>
  )
}

export function ProjectThreadSkeletonList(): JSX.Element {
  const t = useT()
  const rows = [
    { title: '68%', time: 30 },
    { title: '54%', time: 38 },
    { title: '74%', time: 24 },
    { title: '46%', time: 34 }
  ]

  return (
    <div
      role="status"
      aria-busy="true"
      aria-label={t('threadList.loading')}
      style={{
        display: 'flex',
        flexDirection: 'column',
        gap: '2px',
        paddingTop: '2px',
        paddingBottom: '4px'
      }}
    >
      {rows.map((row, index) => (
        <div
          key={index}
          data-testid="project-thread-skeleton-row"
          style={{
            display: 'grid',
            gridTemplateColumns: 'minmax(0, 1fr) minmax(24px, max-content)',
            alignItems: 'center',
            columnGap: '7px',
            width: 'calc(100% - 32px)',
            minHeight: '30px',
            margin: '2px 10px 2px 22px',
            padding: '6px 12px',
            boxSizing: 'border-box'
          }}
        >
          <Skeleton width={row.title} height={12} />
          <Skeleton width={row.time} height={10} />
        </div>
      ))}
    </div>
  )
}

export function ProjectHint({
  label,
  alignment = 'thread'
}: {
  label: string
  alignment?: 'thread' | 'section'
}): JSX.Element {
  return (
    <div
      style={{
        padding: alignment === 'section'
          ? `4px ${SIDEBAR_RAIL_CONTENT_INSET} 8px`
          : '4px 16px 8px 32px',
        color: 'var(--text-dimmed)',
        fontSize: 'var(--type-secondary-size)',
        fontWeight: 400,
        lineHeight: 'var(--type-secondary-line-height)'
      }}
    >
      {label}
    </div>
  )
}

export const sidebarSectionHeaderStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: '4px',
  minHeight: '28px',
  padding: `8px ${SIDEBAR_RAIL_CONTENT_INSET} 2px`,
  cursor: 'pointer',
  userSelect: 'none'
}
