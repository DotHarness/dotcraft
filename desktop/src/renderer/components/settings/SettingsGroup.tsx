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
  /** Off when the body paints its own box, such as an empty state, so frames do not nest. */
  framed?: boolean
  style?: CSSProperties
}

export function SettingsGroup({
  title,
  description,
  headerAction,
  children,
  flush = false,
  framed = true,
  style
}: SettingsGroupProps): JSX.Element {
  return (
    <section style={{ ...groupStyle(), ...style }}>
      {(title || description || headerAction) && (
        <header style={headerStyle(Boolean(headerAction), Boolean(description))}>
          <div style={{ flex: 1, minWidth: 0 }}>
            {title && <h2 style={{ margin: 0, ...settingsHeadingStyle() }}>{title}</h2>}
            {description && <div style={settingsDescriptionStyle()}>{description}</div>}
          </div>
          {headerAction && <div style={headerActionStyle(Boolean(description))}>{headerAction}</div>}
        </header>
      )}
      <div
        className="dc-settings-group__body"
        style={framed ? (flush ? flushBodyStyle() : bodyStyle()) : undefined}
      >
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
 * `inline` puts the control right of the label, `block` stacks it beneath. Passing
 * `children` instead of `control` replaces the whole row body.
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
    display: 'flex',
    flexDirection: 'column',
    gap: '8px'
  }
}

/** A header carrying an action is never shorter than the control band, so the action
 *  cannot overhang the gap to the card below. */
function headerStyle(hasAction: boolean, described: boolean): CSSProperties {
  return {
    display: 'flex',
    alignItems: hasAction && !described ? 'center' : 'flex-start',
    gap: '12px',
    ...(hasAction ? { minHeight: 'var(--button-height)' } : null)
  }
}

/** Boxed to the title's own line so a taller control stays centred on the title
 *  instead of drifting down beside the description. */
function headerActionStyle(described: boolean): CSSProperties {
  return {
    flexShrink: 0,
    display: 'flex',
    alignItems: 'center',
    height: described ? 'var(--type-heading-line-height)' : undefined
  }
}

function cardStyle(): CSSProperties {
  return {
    border: '1px solid var(--border-default)',
    borderRadius: '12px',
    background: 'var(--bg-secondary)',
    overflow: 'hidden'
  }
}

function bodyStyle(): CSSProperties {
  return {
    ...cardStyle(),
    display: 'flex',
    flexDirection: 'column'
  }
}

function flushBodyStyle(): CSSProperties {
  return {
    ...cardStyle(),
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

