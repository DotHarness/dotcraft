import type { CSSProperties, HTMLAttributes, ReactNode, Ref } from 'react'
import { CornerDownRight } from 'lucide-react'
import { SIDEBAR_ROW_MIN_HEIGHT } from './sidebarNavRowStyles'

/**
 * Left padding for a thread / subagent row. `canPin` reserves the pin-column
 * inset; otherwise the row indents by its subagent depth. Single source of truth
 * for both the foreground `ThreadEntry` and the secondary `ReadonlyThreadRow`, so
 * the two never drift apart on indentation.
 */
export function threadRowPaddingLeft(opts: { canPin?: boolean; subAgentDepth?: number }): number {
  if (opts.canPin) return 6
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
  /** Subagent nesting depth — drives the left indent. */
  subAgentDepth?: number
  /** Render the `↳` subagent marker and skip the pin inset. */
  isSubAgent?: boolean
  /** Reserve the pin-column inset instead of indenting by depth. */
  canPin?: boolean
  /** Accessible label / tooltip text for the subagent marker. */
  subAgentLabel?: string

  /** Leading content rendered after the subagent marker (pin button, channel badge). */
  leading?: ReactNode
  /** Replaces the entire name/badge/status grid (e.g. the inline rename input). */
  mainOverride?: ReactNode

  /** Thread name node. */
  name?: ReactNode
  /** Extra style merged onto the name cell (weight/colour differences per caller). */
  nameStyle?: CSSProperties
  /** Optional middle badge column (pending / drop hint). */
  badge?: ReactNode
  /** Status slot content (relative time, spinner, status dot/icon). */
  status?: ReactNode
  /** Extra (typically absolutely-positioned) nodes inside the status slot: archive/confirm. */
  statusExtra?: ReactNode

  /** Grid track for the status column. */
  statusColumn?: string
  /** Grid track for the badge column (used when `badge` is present). */
  badgeColumn?: string
  /** Width of the status slot box. */
  statusSlotWidth?: string
  /** Minimum width of the status slot box. */
  statusSlotMinWidth?: string
  /** Alignment of the status slot within its grid cell. */
  statusJustifySelf?: CSSProperties['justifySelf']
  /** Alignment of the content inside the status slot (centered by default). */
  statusContentJustify?: CSSProperties['justifyContent']
  /** Ref to the status slot element (archive focus management). */
  statusSlotRef?: Ref<HTMLDivElement>
  /** Extra props spread onto the status slot (e.g. onBlurCapture). */
  statusSlotProps?: HTMLAttributes<HTMLDivElement>

  /** Highlighted (active/selected) background — overridable via `containerStyle`. */
  active?: boolean

  rowTestId?: string
  gridTestId?: string
  nameTestId?: string
  badgeTestId?: string
  statusTestId?: string

  /** Extra container style (drop/anim/opacity/cursor overrides). Wins over defaults. */
  containerStyle?: CSSProperties
  /** Container element passthrough (click/context-menu/drag handlers, role, etc.). */
  containerProps?: HTMLAttributes<HTMLDivElement>
}

/**
 * Presentational scaffold shared by every sidebar thread row. It owns the row
 * geometry (height, padding, squircle radius, indent), the subagent marker, the
 * name/badge/status grid, and the centered status slot. Behaviour (selection,
 * archive, drag, rename for the live workspace; switch-then-open for secondary
 * workspaces) lives in the wrapper that renders this — only the layout is shared,
 * which is why the two callers can no longer drift on height or status alignment.
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
      className="dotcraft-sidebar-control-radius"
      data-testid={rowTestId}
      {...containerProps}
      style={{
        display: 'flex',
        alignItems: 'center',
        position: 'relative',
        width: 'calc(100% - 20px)',
        minHeight: SIDEBAR_ROW_MIN_HEIGHT,
        margin: '2px 10px',
        padding: `3px 12px 3px ${paddingLeft}px`,
        boxSizing: 'border-box',
        borderRadius: 'var(--sidebar-control-radius)',
        backgroundColor: active ? 'var(--sidebar-control-active)' : 'transparent',
        gap: '6px',
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
