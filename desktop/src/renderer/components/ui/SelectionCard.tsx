import { useId } from 'react'
import type { CSSProperties, JSX, ReactNode } from 'react'

interface SelectionCardProps {
  name: string
  value: string
  active: boolean
  onSelect: () => void
  title: string
  description?: string
  errorHint?: ReactNode
  extra?: ReactNode
}

/** `errorHint` and `extra` stack below the description, and only on the active card. */
export function SelectionCard({
  name,
  value,
  active,
  onSelect,
  title,
  description,
  errorHint,
  extra
}: SelectionCardProps): JSX.Element {
  const uid = useId()
  const inputId = `selcard-${uid}`
  return (
    <label style={cardStyle(active)} htmlFor={inputId}>
      <input
        id={inputId}
        type="radio"
        name={name}
        value={value}
        checked={active}
        onChange={onSelect}
        style={hiddenInputStyle()}
      />
      <span aria-hidden style={indicatorStyle(active)}>
        <span style={indicatorDotStyle(active)} />
      </span>
      <div style={{ flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column', gap: '4px' }}>
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: '8px',
            flexWrap: 'wrap',
            minWidth: 0
          }}
        >
          <span
            style={{
              fontSize: '13px',
              fontWeight: 600,
              color: 'var(--text-primary)'
            }}
          >
            {title}
          </span>
        </div>
        {description ? (
          <div
            style={{
              fontSize: '11px',
              color: 'var(--text-dimmed)',
              lineHeight: 1.5
            }}
          >
            {description}
          </div>
        ) : null}
        {active && errorHint ? (
          <div
            style={{
              fontSize: '11px',
              color: 'var(--error)',
              lineHeight: 1.5
            }}
          >
            {errorHint}
          </div>
        ) : null}
        {active && extra ? <div style={{ marginTop: '4px' }}>{extra}</div> : null}
      </div>
    </label>
  )
}

function cardStyle(active: boolean): CSSProperties {
  return {
    position: 'relative',
    boxSizing: 'border-box',
    width: '100%',
    border: active ? '1.5px solid var(--accent)' : '1px solid var(--border-default)',
    borderRadius: '10px',
    background: active
      ? 'color-mix(in srgb, var(--accent) 8%, var(--bg-secondary))'
      : 'var(--bg-secondary)',
    padding: '12px 14px',
    display: 'flex',
    gap: '12px',
    alignItems: 'center',
    cursor: 'pointer',
    transition: 'border-color 120ms ease, background-color 120ms ease'
  }
}

function hiddenInputStyle(): CSSProperties {
  return {
    position: 'absolute',
    width: 1,
    height: 1,
    margin: -1,
    padding: 0,
    border: 0,
    overflow: 'hidden',
    clip: 'rect(0,0,0,0)',
    whiteSpace: 'nowrap'
  }
}

function indicatorStyle(active: boolean): CSSProperties {
  return {
    flexShrink: 0,
    width: 16,
    height: 16,
    borderRadius: '50%',
    border: active ? '1.5px solid var(--accent)' : '1.5px solid var(--border-active)',
    background: active ? 'color-mix(in srgb, var(--accent) 14%, transparent)' : 'transparent',
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    transition: 'border-color 120ms ease, background-color 120ms ease'
  }
}

function indicatorDotStyle(active: boolean): CSSProperties {
  return {
    width: 8,
    height: 8,
    borderRadius: '50%',
    background: 'var(--accent)',
    transform: active ? 'scale(1)' : 'scale(0)',
    transition: 'transform 150ms ease'
  }
}
