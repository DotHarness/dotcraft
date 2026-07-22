import type { ButtonHTMLAttributes, CSSProperties, MouseEvent, ReactNode } from 'react'
import { ChevronDown, ChevronLeft, ChevronRight, Search } from 'lucide-react'
import { useState } from 'react'
import { ContextMenu, type ContextMenuPosition } from '../ui/ContextMenu'
import { Button } from '../ui/Button'
import { IconButton } from '../ui/IconButton'

/**
 * Catalog top bars run one control band of their own — shorter and rounder than the
 * ordinary 32px/8px band — shared by every control in the bar so the row reads as one
 * strip. See the Catalog Toolbar Band section in specs/architecture/DESIGN.md.
 */
export const CATALOG_TOOLBAR_CONTROL_SIZE = 28
export const CATALOG_TOOLBAR_CONTROL_RADIUS = 10

/** Icon-only action sized for the catalog toolbar band. The label doubles as its tooltip. */
export function CatalogToolbarIconButton({
  label,
  icon,
  onClick,
  disabled,
  ...props
}: {
  label: string
  icon: ReactNode
  onClick: (event: MouseEvent<HTMLButtonElement>) => void
  disabled?: boolean
} & Omit<ButtonHTMLAttributes<HTMLButtonElement>, 'onClick' | 'children'>): JSX.Element {
  return (
    <IconButton
      label={label}
      tooltipLabel={label}
      tooltipPlacement="bottom"
      size={CATALOG_TOOLBAR_CONTROL_SIZE}
      radius={CATALOG_TOOLBAR_CONTROL_RADIUS}
      icon={icon}
      onClick={onClick}
      disabled={disabled}
      {...props}
    />
  )
}

export interface CatalogFilterOption<T extends string> {
  value: T
  label: string
}

interface CatalogHoverButtonProps extends Omit<ButtonHTMLAttributes<HTMLButtonElement>, 'style'> {
  baseStyle: CSSProperties
  hoverStyle?: CSSProperties
}

export function CatalogHoverButton({
  baseStyle,
  hoverStyle,
  children,
  onMouseEnter,
  onMouseLeave,
  onFocus,
  onBlur,
  ...props
}: CatalogHoverButtonProps): JSX.Element {
  const [active, setActive] = useState(false)
  return (
    <button
      {...props}
      onMouseEnter={(event) => {
        setActive(true)
        onMouseEnter?.(event)
      }}
      onMouseLeave={(event) => {
        setActive(false)
        onMouseLeave?.(event)
      }}
      onFocus={(event) => {
        setActive(true)
        onFocus?.(event)
      }}
      onBlur={(event) => {
        setActive(false)
        onBlur?.(event)
      }}
      style={catalogHoverButtonStyle(baseStyle, active, hoverStyle)}
    >
      {children}
    </button>
  )
}

export function CatalogTabs<T extends string>({
  value,
  items,
  onChange,
  inTopBar = false
}: {
  value: T
  items: Array<{ value: T; label: string }>
  onChange: (value: T) => void
  inTopBar?: boolean
}): JSX.Element {
  const [hovered, setHovered] = useState<T | null>(null)

  return (
    <div style={inTopBar ? styles.tabsInTopBar : styles.tabs}>
      {items.map((item) => (
        <button
          key={item.value}
          type="button"
          onClick={() => onChange(item.value)}
          onMouseEnter={() => setHovered(item.value)}
          onMouseLeave={() => setHovered(null)}
          onFocus={() => setHovered(item.value)}
          onBlur={() => setHovered(null)}
          style={catalogTabStyle(value === item.value, hovered === item.value)}
        >
          {item.label}
        </button>
      ))}
    </div>
  )
}

export function CatalogTopBar({
  navigation,
  actions
}: {
  navigation?: ReactNode
  actions?: ReactNode
}): JSX.Element {
  return (
    <div style={styles.topBar}>
      <div style={styles.topBarNavigation}>{navigation}</div>
      {actions ? <div style={styles.topBarActions}>{actions}</div> : null}
    </div>
  )
}

export function CatalogBreadcrumb({
  parentLabel,
  currentLabel,
  onBack
}: {
  parentLabel: string
  currentLabel: string
  onBack: () => void
}): JSX.Element {
  return (
    <div style={styles.breadcrumb}>
      <Button
        type="button"
        size="sm"
        variant="ghost"
        onClick={onBack}
        style={styles.catalogBreadcrumbButton}
        iconLeft={<ChevronLeft size={14} aria-hidden />}
      >
        {parentLabel}
      </Button>
      <BreadcrumbSeparator />
      <span style={styles.breadcrumbCurrent}>{currentLabel}</span>
    </div>
  )
}

/**
 * Breadcrumb `parent › current` divider.
 *
 * Deliberately the same lucide chevron at the same size as the back button's
 * ChevronLeft, so both read as one icon family. The previous `›` was U+203A — a
 * quotation mark, not a chevron: it rendered from `--font-ui`, which DotCraft
 * swaps per locale, so its shape and width changed between Latin and CJK. The
 * separator is de-emphasised by colour (`--text-dimmed`), not by stroke weight.
 */
export function BreadcrumbSeparator(): JSX.Element {
  return (
    <span style={styles.breadcrumbSep} aria-hidden>
      <ChevronRight size={14} />
    </span>
  )
}

export function CatalogSearchBox({
  value,
  placeholder,
  onChange,
  style
}: {
  value: string
  placeholder: string
  onChange: (value: string) => void
  style?: CSSProperties
}): JSX.Element {
  return (
    <div style={{ ...styles.searchBox, ...style }}>
      <Search size={15} aria-hidden />
      <input
        type="text"
        value={value}
        placeholder={placeholder}
        onChange={(event) => onChange(event.target.value)}
        style={styles.searchInput}
      />
    </div>
  )
}

export function CatalogFilterMenu<T extends string>({
  value,
  options,
  ariaLabel,
  onChange
}: {
  value: T
  options: Array<CatalogFilterOption<T>>
  ariaLabel: string
  onChange: (value: T) => void
}): JSX.Element {
  const [position, setPosition] = useState<ContextMenuPosition | null>(null)
  const [hovered, setHovered] = useState(false)
  const selected = options.find((option) => option.value === value) ?? options[0]

  return (
    <>
      <button
        type="button"
        aria-label={ariaLabel}
        aria-haspopup="menu"
        aria-expanded={position != null}
        onClick={(event) => {
          const rect = event.currentTarget.getBoundingClientRect()
          setPosition({ x: rect.left, y: rect.bottom + 6 })
        }}
        onMouseEnter={() => setHovered(true)}
        onMouseLeave={() => setHovered(false)}
        onFocus={() => setHovered(true)}
        onBlur={() => setHovered(false)}
        style={catalogFilterMenuButtonStyle(hovered || position != null)}
      >
        <span>{selected.label}</span>
        <ChevronDown size={14} aria-hidden />
      </button>
      {position && (
        <ContextMenu
          position={position}
          onClose={() => setPosition(null)}
          items={options.map((option) => ({
            label: option.label,
            onClick: () => onChange(option.value)
          }))}
        />
      )}
    </>
  )
}

function catalogTabStyle(selected: boolean, active: boolean): CSSProperties {
  const highlighted = selected || active
  return {
    ...styles.tab,
    background: highlighted ? 'var(--bg-tertiary)' : 'transparent',
    color: highlighted ? 'var(--text-primary)' : 'var(--text-secondary)'
  }
}

function catalogFilterMenuButtonStyle(active: boolean): CSSProperties {
  return {
    ...styles.filterMenuButton,
    backgroundColor: active ? 'var(--bg-tertiary)' : 'var(--bg-secondary)',
    color: active ? 'var(--text-primary)' : styles.filterMenuButton.color,
    transition: 'background-color 120ms ease, color 120ms ease'
  }
}

function catalogHoverButtonStyle(
  baseStyle: CSSProperties,
  active: boolean,
  hoverStyle?: CSSProperties
): CSSProperties {
  const transition = 'background-color 120ms ease, border-color 120ms ease, color 120ms ease'
  if (!active) return { ...baseStyle, transition }
  // Base styles declare their border via the `border` shorthand. Override that SAME
  // shorthand on hover — never the `borderColor` longhand. Mixing the two confuses
  // React's inline-style diff: on un-hover it clears `borderColor` without re-applying
  // `border`, leaving a stale 1px `currentColor` (dark) frame that persists.
  const hasBorder = typeof baseStyle.border === 'string' && baseStyle.border !== 'none'
  return {
    ...baseStyle,
    background: 'var(--bg-tertiary)',
    backgroundColor: 'var(--bg-tertiary)',
    border: hasBorder ? '1px solid transparent' : baseStyle.border,
    color: 'var(--text-primary)',
    transition,
    ...hoverStyle
  }
}

export function CatalogChip({ label, active = false }: { label: string; active?: boolean }): JSX.Element {
  return <span style={active ? styles.chipActive : styles.chip}>{label}</span>
}

export function CatalogSection({ title, children }: { title: string; children: ReactNode }): JSX.Element {
  return (
    <section style={{ marginBottom: '34px' }}>
      <h2 style={styles.sectionTitle}>{title}</h2>
      {children}
    </section>
  )
}

export function CatalogCompactGrid({ children }: { children: ReactNode }): JSX.Element {
  return <div style={styles.compactGrid}>{children}</div>
}

export const styles = {
  page: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    minHeight: 0,
    // Transparent so the shared ThreePanel main-surface frame (rounded card +
    // inset edge borders) shows through, matching the conversation, welcome, and
    // settings views. An opaque --bg-primary here painted over and hid the frame,
    // and also rendered a slightly different surface color than --main-surface.
    backgroundColor: 'transparent',
    color: 'var(--text-primary)'
  },
  tabs: {
    display: 'flex',
    gap: '4px',
    height: '40px',
    alignItems: 'center',
    padding: '8px 12px 4px',
    boxSizing: 'border-box',
    flexShrink: 0
  },
  tabsInTopBar: {
    display: 'flex',
    gap: '4px',
    height: '100%',
    alignItems: 'center',
    flexShrink: 0
  },
  topBar: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: '12px',
    height: '48px',
    padding: '8px 12px',
    boxSizing: 'border-box',
    flexShrink: 0
  },
  topBarNavigation: {
    display: 'flex',
    alignItems: 'center',
    alignSelf: 'stretch',
    minWidth: 0
  },
  topBarActions: {
    display: 'flex',
    alignItems: 'center',
    gap: '8px',
    flexShrink: 0
  },
  tab: {
    border: 'none',
    borderRadius: '8px',
    padding: '6px 10px',
    background: 'transparent',
    color: 'var(--text-secondary)',
    cursor: 'pointer',
    fontSize: '13px',
    lineHeight: 1.2
  },
  tabActive: {
    border: 'none',
    borderRadius: '8px',
    padding: '6px 10px',
    background: 'var(--bg-tertiary)',
    color: 'var(--text-primary)',
    cursor: 'pointer',
    fontSize: '13px',
    lineHeight: 1.2
  },
  browseHeader: {
    flexShrink: 0,
    padding: '28px 64px 16px',
    borderBottom: '1px solid var(--border-subtle)'
  },
  heroTitle: {
    margin: '0 0 24px',
    textAlign: 'center',
    fontSize: '26px',
    lineHeight: 1.2,
    fontWeight: 700,
    letterSpacing: 0
  },
  searchRow: {
    display: 'flex',
    gap: '8px',
    maxWidth: '760px',
    margin: '0 auto',
    alignItems: 'center'
  },
  searchBox: {
    flex: '1 1 320px',
    minWidth: 0,
    height: '36px',
    boxSizing: 'border-box',
    display: 'flex',
    alignItems: 'center',
    gap: '8px',
    padding: '0 11px',
    borderRadius: '8px',
    border: '1px solid var(--border-default)',
    backgroundColor: 'var(--bg-secondary)',
    color: 'var(--text-secondary)'
  },
  searchInput: {
    width: '100%',
    minWidth: 0,
    border: 'none',
    outline: 'none',
    backgroundColor: 'transparent',
    color: 'var(--text-primary)',
    fontSize: '13px'
  },
  filterMenuButton: {
    height: '36px',
    minWidth: '74px',
    boxSizing: 'border-box',
    borderRadius: '8px',
    border: '1px solid var(--border-default)',
    backgroundColor: 'var(--bg-secondary)',
    color: 'var(--text-primary)',
    padding: '0 10px',
    fontSize: '13px',
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: '6px',
    cursor: 'pointer',
    lineHeight: 1,
    whiteSpace: 'nowrap'
  },
  browseMain: {
    flex: 1,
    minHeight: 0,
    overflow: 'auto',
    padding: '28px 64px 48px'
  },
  sectionTitle: {
    maxWidth: '760px',
    margin: '0 auto 12px',
    paddingTop: '4px',
    borderTop: '1px solid var(--border-subtle)',
    fontSize: '16px',
    lineHeight: 1.3,
    fontWeight: 700,
    color: 'var(--text-primary)'
  },
  compactGrid: {
    maxWidth: '760px',
    margin: '0 auto',
    display: 'grid',
    gridTemplateColumns: 'repeat(2, minmax(0, 1fr))',
    columnGap: '34px',
    rowGap: '18px'
  },
  compactItem: {
    width: '100%',
    minWidth: 0,
    height: '58px',
    display: 'flex',
    alignItems: 'center',
    gap: '12px',
    padding: '0 8px',
    border: 'none',
    borderRadius: '8px',
    backgroundColor: 'transparent',
    color: 'var(--text-primary)',
    cursor: 'pointer',
    textAlign: 'left',
    transition: 'background-color 120ms ease, color 120ms ease'
  },
  rowTitle: {
    fontSize: '13px',
    lineHeight: 1.25,
    fontWeight: 700,
    color: 'var(--text-primary)',
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap'
  },
  rowTitleLine: {
    minWidth: 0,
    display: 'flex',
    alignItems: 'center',
    gap: '6px'
  },
  rowDesc: {
    marginTop: '4px',
    fontSize: '12px',
    lineHeight: 1.3,
    color: 'var(--text-secondary)',
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap'
  },
  statusIcon: {
    minWidth: '28px',
    display: 'inline-flex',
    justifyContent: 'center',
    color: 'var(--text-dimmed)',
    fontSize: '11px',
    whiteSpace: 'nowrap'
  },
  manageHeader: {
    flexShrink: 0,
    padding: '24px 64px 12px',
    borderBottom: '1px solid var(--border-subtle)'
  },
  breadcrumb: {
    display: 'flex',
    alignItems: 'center',
    gap: '8px',
    color: 'var(--text-secondary)',
    fontSize: '13px'
  },
  breadcrumbButton: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: '4px',
    border: 'none',
    background: 'transparent',
    color: 'var(--text-secondary)',
    cursor: 'pointer',
    padding: 0,
    fontSize: '13px'
  },
  catalogBreadcrumbButton: {
    height: 28,
    padding: '0 8px 0 4px'
  },
  breadcrumbSep: {
    display: 'inline-flex',
    alignItems: 'center',
    flexShrink: 0,
    color: 'var(--text-dimmed)'
  },
  breadcrumbCurrent: {
    color: 'var(--text-primary)',
    fontWeight: 700
  },
  manageToolbar: {
    margin: '0 auto',
    maxWidth: '730px',
    display: 'flex',
    alignItems: 'center',
    gap: '8px'
  },
  chip: {
    display: 'inline-flex',
    alignItems: 'center',
    height: '28px',
    padding: '0 10px',
    borderRadius: '8px',
    backgroundColor: 'transparent',
    color: 'var(--text-secondary)',
    fontSize: '13px',
    whiteSpace: 'nowrap'
  },
  chipActive: {
    display: 'inline-flex',
    alignItems: 'center',
    height: '28px',
    padding: '0 10px',
    borderRadius: '8px',
    backgroundColor: 'var(--bg-tertiary)',
    color: 'var(--text-primary)',
    fontSize: '13px',
    whiteSpace: 'nowrap'
  },
  savedHint: {
    fontSize: '12px',
    color: 'var(--success)',
    whiteSpace: 'nowrap'
  },
  manageMain: {
    flex: 1,
    minHeight: 0,
    overflow: 'auto',
    padding: '28px 64px 48px'
  },
  manageRow: {
    maxWidth: '730px',
    margin: '0 auto',
    minHeight: '74px',
    display: 'flex',
    alignItems: 'center',
    gap: '12px'
  },
  emptyText: {
    maxWidth: '760px',
    margin: '0 auto',
    fontSize: '13px',
    color: 'var(--text-secondary)'
  }
} satisfies Record<string, CSSProperties>
