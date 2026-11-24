import type { CSSProperties, JSX, MouseEvent, ReactNode } from 'react'

import {
  settingsDescriptionStyle,
  settingsHeadingStyle,
  settingsHintStyle,
  settingsLabelStyle,
  settingsPlaceholderStyle
} from './settingsTypography'

interface SettingsGroupProps {
  title?: string
  description?: ReactNode
  headerAction?: ReactNode
  reserveHeaderAction?: boolean
  children: ReactNode
  flush?: boolean
  /** Off when the body paints its own box, such as an empty state, so frames do not nest. */
  framed?: boolean
  style?: CSSProperties
}

export function SettingsGroup({
  title,
  description,
  headerAction,
  reserveHeaderAction = false,
  children,
  flush = false,
  framed = true,
  style
}: SettingsGroupProps): JSX.Element {
  return (
    <section style={{ ...groupStyle(), ...style }}>
      {(title || description || headerAction) && (
        <header>
          <div style={titleLineStyle(Boolean(headerAction) || reserveHeaderAction)}>
            <div style={{ flex: 1, minWidth: 0 }}>
              {title && <h2 data-settings-label style={{ margin: 0, ...settingsHeadingStyle() }}>{title}</h2>}
            </div>
            {headerAction && <div style={{ flexShrink: 0, display: 'flex', alignItems: 'center' }}>{headerAction}</div>}
          </div>
          {description && (
            <div style={settingsDescriptionStyle(!(headerAction || reserveHeaderAction))}>{description}</div>
          )}
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
          <div style={{ minWidth: 0 }}>
            {label && (
              <label data-settings-label htmlFor={htmlFor} style={settingsLabelStyle()}>
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
          <label data-settings-label htmlFor={htmlFor} style={settingsLabelStyle()}>
            {label}
          </label>
        )}
        {description && <div style={settingsHintStyle()}>{description}</div>}
      </div>
      {control !== undefined && <div style={{ flexShrink: 0, minWidth: controlMinWidth, maxWidth: '100%' }}>{control}</div>}
    </div>
  )
}

interface SettingsEmptyRowProps {
  tone?: 'dimmed' | 'error'
  action?: ReactNode
  children: ReactNode
}

export function SettingsEmptyRow({ tone = 'dimmed', action, children }: SettingsEmptyRowProps): JSX.Element {
  return (
    <div className="dc-settings-row" style={emptyRowStyle(tone)}>
      <span>{children}</span>
      {action}
    </div>
  )
}

function emptyRowStyle(tone: 'dimmed' | 'error'): CSSProperties {
  return {
    ...rowStyle(),
    ...settingsPlaceholderStyle(),
    gap: '12px',
    minHeight: 'calc(28px + var(--type-ui-line-height) + 4px + var(--type-hint-line-height))',
    justifyContent: 'center',
    textAlign: 'center',
    color: tone === 'error' ? 'var(--error)' : 'var(--text-dimmed)'
  }
}

function groupStyle(): CSSProperties {
  return {
    display: 'flex',
    flexDirection: 'column',
    gap: '8px'
  }
}

/** Grows to fit the action so it never overhangs the header, where a scroll container would clip it. */
function titleLineStyle(hasAction: boolean): CSSProperties {
  return {
    display: 'flex',
    alignItems: 'center',
    gap: '12px',
    ...(hasAction ? { minHeight: 'var(--button-height)' } : null)
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

