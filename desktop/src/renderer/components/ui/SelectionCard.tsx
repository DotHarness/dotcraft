import { useId } from 'react'
import type { CSSProperties, JSX, ReactNode } from 'react'

interface SelectionCardProps {
  name: string
  value: string
  active: boolean
  onSelect: () => void
  title: string
  description?: string
  resolvedBadge?: ReactNode
  errorHint?: ReactNode
  extra?: ReactNode
}

/**
 * Radio-style selection card with a custom concentric dot indicator.
 * - Active card gains accent border, tinted background, and filled indicator.
 * - `resolvedBadge` renders inline to the right of the title on the active card (e.g. "Available").
 * - `errorHint` / `extra` are stacked below the description within the same card.
 */
export function SelectionCard({
  name,
  value,
  active,
  onSelect,
  title,
  description,
  resolvedBadge,
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
          {active && resolvedBadge ? <span style={{ display: 'inline-flex' }}>{resolvedBadge}</span> : null}
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

export function ResolvedPill({ label }: { label: string }): JSX.Element {
  return (
    <span
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        gap: '4px',
        padding: '2px 8px',
        borderRadius: '999px',
        fontSize: '11px',
        fontWeight: 600,
        background: 'color-mix(in srgb, var(--success, #3fb950) 18%, transparent)',
        color: 'var(--success, #3fb950)',
        lineHeight: 1.4
      }}
    >
      <svg width="11" height="11" viewBox="0 0 20 20" fill="none" aria-hidden>
        <circle cx="10" cy="10" r="7" stroke="currentColor" strokeWidth="1.6" />
        <path
          d="m6.8 10.1 2.1 2.1 4.4-4.7"
          stroke="currentColor"
          strokeWidth="1.7"
          strokeLinecap="round"
          strokeLinejoin="round"
        />
      </svg>
      <span>{label}</span>
    </span>
  )
}

function cardStyle(active: boolean): CSSProperties {
  return {
    position: 'relative',
    border: active ? '1.5px solid var(--accent)' : '1px solid var(--border-default)',
    borderRadius: '10px',
    background: active
      ? 'color-mix(in srgb, var(--accent) 8%, var(--bg-secondary))'
      : 'var(--bg-secondary)',
    padding: '12px 14px',
    display: 'flex',
    gap: '12px',
    alignItems: 'flex-start',
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
    marginTop: 2,
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
