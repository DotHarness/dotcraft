import type { CSSProperties, HTMLAttributes, ReactNode, Ref } from 'react'
import { CornerDownRight } from 'lucide-react'
import { SIDEBAR_ROW_MIN_HEIGHT } from './sidebarNavRowStyles'

/**
 * Shared by the foreground `ThreadEntry` and the secondary `ReadonlyThreadRow`
 * so the two never drift apart on indentation.
 */
export function threadRowPaddingLeft(opts: { canPin?: boolean; subAgentDepth?: number }): number {
  if (opts.canPin) return 12
  return 14 + (opts.subAgentDepth ?? 0) * 14
}

const subAgentMarkerStyle: CSSProperties = {
  width: '16px',
  minWidth: '16px',
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  color: 'var(--text-dimmed)',
  flexShrink: 0
}

export interface ThreadRowLayoutProps {
  subAgentDepth?: number
  isSubAgent?: boolean
  /** Reserve the pin-column inset instead of indenting by depth. */
  canPin?: boolean
  subAgentLabel?: string

  leading?: ReactNode
  /** Replaces the entire name/badge/status grid (e.g. the inline rename input). */
  mainOverride?: ReactNode

  name?: ReactNode
  nameStyle?: CSSProperties
  badge?: ReactNode
  status?: ReactNode
  statusExtra?: ReactNode

  statusColumn?: string
  badgeColumn?: string
  statusSlotWidth?: string
  statusSlotMinWidth?: string
  statusJustifySelf?: CSSProperties['justifySelf']
  statusContentJustify?: CSSProperties['justifyContent']
  statusSlotRef?: Ref<HTMLDivElement>
  statusSlotProps?: HTMLAttributes<HTMLDivElement>

  active?: boolean

  rowTestId?: string
  gridTestId?: string
  nameTestId?: string
  badgeTestId?: string
  statusTestId?: string

  /** Extra container style (drop/anim/opacity/cursor overrides). Wins over defaults. */
  containerStyle?: CSSProperties
  containerProps?: HTMLAttributes<HTMLDivElement>
}

/**
 * Presentational scaffold for every sidebar thread row: it owns only the geometry
 * so callers cannot drift on height or status alignment. Behaviour (selection,
 * archive, drag, rename) belongs in the wrapper that renders this.
 */
export function ThreadRowLayout({
  subAgentDepth = 0,
  isSubAgent = false,
  canPin = false,
  subAgentLabel,
  leading,
  mainOverride,
  name,
  nameStyle,
  badge,
  status,
  statusExtra,
  statusColumn = '24px',
  badgeColumn = 'minmax(74px, max-content)',
  statusSlotWidth = 'max-content',
  statusSlotMinWidth = '24px',
  statusJustifySelf = 'center',
  statusContentJustify = 'center',
  statusSlotRef,
  statusSlotProps,
  active = false,
  rowTestId,
  gridTestId,
  nameTestId,
  badgeTestId,
  statusTestId,
  containerStyle,
  containerProps
}: ThreadRowLayoutProps): JSX.Element {
  const paddingLeft = threadRowPaddingLeft({ canPin, subAgentDepth })
  const gridColumns = badge
    ? `minmax(0, 1fr) ${badgeColumn} ${statusColumn}`
    : `minmax(0, 1fr) ${statusColumn}`

  return (
    <div
      className="dotcraft-sidebar-row-radius"
      data-testid={rowTestId}
      {...containerProps}
      style={{
        display: 'flex',
        alignItems: 'center',
        position: 'relative',
        // Matches SIDEBAR_NAV_ROW_OUTER so every clickable sidebar row shares the
        // same 4px side inset and lines up on the right edge.
        width: 'calc(100% - 8px)',
        minHeight: SIDEBAR_ROW_MIN_HEIGHT,
        margin: '2px 4px',
        // Right padding is 6px so the 24px status slot ends 10px from the sidebar's
        // inner-right edge, where the ProjectHeader's action buttons end too.
        padding: `3px 6px 3px ${paddingLeft}px`,
        boxSizing: 'border-box',
        borderRadius: 'var(--sidebar-row-radius)',
        backgroundColor: active ? 'var(--sidebar-control-active)' : 'transparent',
        gap: '8px',
        userSelect: 'none',
        ...containerStyle
      }}
    >
      {isSubAgent && (
        <span style={subAgentMarkerStyle} aria-label={subAgentLabel}>
          <CornerDownRight size={12} strokeWidth={2} aria-hidden="true" />
        </span>
      )}
      {leading}
      {mainOverride ?? (
        <div
          data-testid={gridTestId}
          style={{
            flex: 1,
            minWidth: 0,
            display: 'grid',
            gridTemplateColumns: gridColumns,
            columnGap: '7px',
            alignItems: 'center',
            fontSize: 'var(--type-ui-size)',
            lineHeight: 'var(--type-ui-line-height)'
          }}
        >
          <span
            data-testid={nameTestId}
            style={{
              minWidth: 0,
              overflow: 'hidden',
              textOverflow: 'ellipsis',
              whiteSpace: 'nowrap',
              color: 'var(--text-primary)',
              ...nameStyle
            }}
          >
            {name}
          </span>
          {badge && (
            <span
              data-testid={badgeTestId}
              style={{
                minWidth: 0,
                display: 'inline-flex',
                alignItems: 'center',
                justifyContent: 'flex-end',
                justifySelf: 'stretch'
              }}
            >
              {badge}
            </span>
          )}
          <div
            ref={statusSlotRef}
            data-testid={statusTestId}
            {...statusSlotProps}
            style={{
              width: statusSlotWidth,
              minWidth: statusSlotMinWidth,
              justifySelf: statusJustifySelf,
              height: '24px',
              position: 'relative',
              display: 'flex',
              alignItems: 'center',
              justifyContent: statusContentJustify
            }}
          >
            {status}
            {statusExtra}
          </div>
        </div>
      )}
    </div>
  )
}
