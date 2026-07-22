import type { CSSProperties, JSX, MouseEvent, ReactNode } from 'react'

import {
  settingsDescriptionStyle,
  settingsHeadingStyle,
  settingsHintStyle,
  settingsLabelStyle
} from './settingsTypography'

interface SettingsGroupProps {
  title?: string
  description?: ReactNode
  headerAction?: ReactNode
  children: ReactNode
  /**
   * When true, the group renders a simple bordered container without row dividers.
   * Useful for groups whose body is a custom layout (e.g. grid of channel icons).
   */
  flush?: boolean
  style?: CSSProperties
}

/**
 * Grouped settings container: a single rounded card with optional header, and
 * rows separated by thin horizontal dividers.
 */
export function SettingsGroup({
  title,
  description,
  headerAction,
  children,
  flush = false,
  style
}: SettingsGroupProps): JSX.Element {
  return (
    <section style={{ ...groupStyle(), ...style }}>
      {(title || description || headerAction) && (
        <header style={headerStyle()}>
          <div style={{ flex: 1, minWidth: 0 }}>
            {title && <div style={settingsHeadingStyle()}>{title}</div>}
            {description && <div style={settingsDescriptionStyle()}>{description}</div>}
          </div>
          {headerAction && <div style={{ flexShrink: 0 }}>{headerAction}</div>}
        </header>
      )}
      <div className="dc-settings-group__body" style={flush ? flushBodyStyle() : bodyStyle()}>
        {children}
      </div>
    </section>
  )
}

interface SettingsRowProps {
  label?: ReactNode
  description?: ReactNode
  htmlFor?: string
  control?: ReactNode
  controlMinWidth?: number | string
  orientation?: 'inline' | 'block'
  children?: ReactNode
  align?: 'center' | 'flex-start'
  style?: CSSProperties
  onContextMenu?: (event: MouseEvent<HTMLDivElement>) => void
}

/**
 * A single row inside a SettingsGroup. `inline` places label/description on the left
 * and control on the right. `block` stacks control beneath label (for wide controls).
 * If `children` is provided instead of `control`, the full row body is custom content.
 */
export function SettingsRow({
  label,
  description,
  htmlFor,
  control,
  controlMinWidth,
  orientation = 'inline',
  children,
  align = 'center',
  style,
  onContextMenu
}: SettingsRowProps): JSX.Element {
  if (children !== undefined && label === undefined && description === undefined && control === undefined) {
    return (
      <div className="dc-settings-row" style={{ ...rowStyle(), ...style }}>
        {children}
      </div>
    )
  }

  if (orientation === 'block') {
    return (
      <div
        className="dc-settings-row"
        onContextMenu={onContextMenu}
        style={{ ...rowStyle(), flexDirection: 'column', alignItems: 'stretch', gap: '10px', ...style }}
      >
        {(label || description) && (
          <div>
            {label && (
              <label htmlFor={htmlFor} style={settingsLabelStyle()}>
                {label}
              </label>
            )}
            {description && <div style={settingsHintStyle()}>{description}</div>}
          </div>
        )}
        {control}
        {children}
      </div>
    )
  }

  return (
    <div className="dc-settings-row" onContextMenu={onContextMenu} style={{ ...rowStyle(), alignItems: align, ...style }}>
      <div style={{ flex: 1, minWidth: 0 }}>
        {label && (
          <label htmlFor={htmlFor} style={settingsLabelStyle()}>
            {label}
          </label>
        )}
        {description && <div style={settingsHintStyle()}>{description}</div>}
      </div>
      {control !== undefined && <div style={{ flexShrink: 0, minWidth: controlMinWidth }}>{control}</div>}
    </div>
  )
}

function groupStyle(): CSSProperties {
  return {
    border: '1px solid var(--border-default)',
    borderRadius: '12px',
    background: 'var(--bg-secondary)',
    overflow: 'hidden'
  }
}

function headerStyle(): CSSProperties {
  return {
    display: 'flex',
    alignItems: 'center',
    gap: '12px',
    padding: '14px 16px',
    borderBottom: '1px solid var(--border-default)',
    background: 'transparent'
  }
}

function bodyStyle(): CSSProperties {
  return {
    display: 'flex',
    flexDirection: 'column'
  }
}

function flushBodyStyle(): CSSProperties {
  return {
    padding: '14px 16px'
  }
}

function rowStyle(): CSSProperties {
  return {
    display: 'flex',
    flexWrap: 'wrap',
    gap: '16px',
    padding: '14px 16px',
    alignItems: 'center'
  }
}

