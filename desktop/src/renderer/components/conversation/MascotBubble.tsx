import type { JSX } from 'react'

/**
 * Teams-style speech bubble shown above the composer mascot.
 *
 * Visual spec: neutral elevated surface (`--bg-secondary` + `--border-default`
 * + tokenized shadow). The tone is communicated only as a small semantic accent
 * (left rule + status dot) using `--error` / `--success` / `--warning` / `--info`,
 * never as a coloured surface. At most one primary action, rendered in the neutral
 * inverted style; secondary actions stay neutral-bordered.
 *
 * There is no separate close control: dismissal is one of the (anthropomorphic)
 * reply actions the caller supplies (e.g. "OK", "Got it", "Not now").
 *
 * Presentational only: the caller positions the bubble relative to the mascot and
 * owns all copy (already localized).
 */

export type MascotBubbleTone = 'info' | 'success' | 'warning' | 'error'

export interface MascotBubbleAction {
  label: string
  onClick: () => void
  primary?: boolean
}

interface MascotBubbleProps {
  tone?: MascotBubbleTone
  title: string
  body?: string
  actions?: MascotBubbleAction[]
}

function toneColor(tone: MascotBubbleTone): string {
  switch (tone) {
    case 'success':
      return 'var(--success)'
    case 'warning':
      return 'var(--warning)'
    case 'error':
      return 'var(--error)'
    case 'info':
    default:
      return 'var(--info)'
  }
}

export function MascotBubble({
  tone = 'info',
  title,
  body,
  actions = []
}: MascotBubbleProps): JSX.Element {
  const accent = toneColor(tone)

  return (
    <div
      role="status"
      className="composer-mascot-bubble"
      style={{
        position: 'relative',
        width: '248px',
        maxWidth: '76vw',
        background: 'var(--bg-secondary)',
        border: '1px solid var(--border-default)',
        borderLeft: `3px solid ${accent}`,
        borderRadius: '12px',
        padding: '10px 12px 11px',
        boxShadow: 'var(--shadow-lg)',
        pointerEvents: 'auto',
        transformOrigin: 'bottom right'
      }}
    >
      {/* Tail pointing down toward the mascot below-right. */}
      <span
        aria-hidden
        style={{
          position: 'absolute',
          right: '18px',
          bottom: '-6px',
          width: '11px',
          height: '11px',
          background: 'var(--bg-secondary)',
          borderRight: '1px solid var(--border-default)',
          borderBottom: '1px solid var(--border-default)',
          transform: 'rotate(45deg)'
        }}
      />

      <div style={{ display: 'flex', alignItems: 'center', gap: '7px' }}>
        <span style={{ width: '7px', height: '7px', borderRadius: '50%', background: accent, flex: 'none' }} aria-hidden />
        <span
          style={{
            fontSize: 'var(--type-ui-size)',
            lineHeight: 'var(--type-ui-line-height)',
            fontWeight: 'var(--type-ui-emphasis-weight)',
            color: 'var(--text-primary)'
          }}
        >
          {title}
        </span>
      </div>

      {body && (
        <p
          style={{
            margin: '4px 0 0',
            fontSize: 'var(--type-secondary-size)',
            lineHeight: 'var(--type-secondary-line-height)',
            color: 'var(--text-secondary)'
          }}
        >
          {body}
        </p>
      )}

      {actions.length > 0 && (
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: '6px', marginTop: '9px' }}>
          {actions.map((action, i) => (
            <button
              key={i}
              type="button"
              onClick={action.onClick}
              style={{
                fontSize: 'var(--type-secondary-size)',
                lineHeight: 'var(--type-secondary-line-height)',
                fontWeight: 'var(--type-ui-emphasis-weight)',
                fontFamily: 'inherit',
                borderRadius: '8px',
                padding: '5px 10px',
                cursor: 'pointer',
                border: action.primary ? '1px solid var(--text-primary)' : '1px solid var(--border-default)',
                background: action.primary ? 'var(--text-primary)' : 'transparent',
                color: action.primary ? 'var(--bg-primary)' : 'var(--text-primary)',
                transition: 'background-color 100ms ease'
              }}
            >
              {action.label}
            </button>
          ))}
        </div>
      )}
    </div>
  )
}
