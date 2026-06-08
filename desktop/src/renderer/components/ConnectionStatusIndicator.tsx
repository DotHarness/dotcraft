import { useConnectionStore } from '../stores/connectionStore'
import { useT } from '../contexts/LocaleContext'
import { connectionStatusLabel } from '../utils/connectionStatusLabel'
import { SIDEBAR_NAV_ICON_SLOT } from './sidebar/sidebarNavRowStyles'
import { ActionTooltip } from './ui/ActionTooltip'

const STATUS_CONFIG = {
  connecting: {
    color: 'var(--warning)',
    pulse: true
  },
  connected: {
    color: 'var(--success)',
    pulse: false
  },
  disconnected: {
    color: 'var(--error)',
    pulse: true
  },
  error: {
    color: 'var(--error)',
    pulse: false
  }
} as const

const PULSE_KEYFRAMES = `
  @keyframes pulse {
    0%, 100% { opacity: 1; }
    50% { opacity: 0.4; }
  }
`

/**
 * Connection status indicator for the sidebar footer.
 * Shows the current AppServer connection state.
 *
 * - `variant="row"` (default): colored dot plus a text label.
 * - `variant="dot"`: an ambient dot only — quiet (dimmed) when connected, and
 *   colored/pulsing when connecting, disconnected, or in error. The full label is
 *   exposed via tooltip and accessible name. Used in the compact sidebar footer.
 *
 * Spec §5.3 and spec §9.7
 */
export function ConnectionStatusIndicator({
  variant = 'row'
}: {
  variant?: 'row' | 'dot'
} = {}): JSX.Element {
  const t = useT()
  const { status, errorMessage } = useConnectionStore()
  const config = STATUS_CONFIG[status]
  const label = connectionStatusLabel(status, errorMessage, t)

  if (variant === 'dot') {
    return (
      <ActionTooltip label={label} placement="top">
        <span
          role="img"
          aria-label={label}
          style={{ display: 'inline-flex', alignItems: 'center', flexShrink: 0 }}
        >
          <span
            style={{
              display: 'block',
              width: '8px',
              height: '8px',
              borderRadius: '50%',
              backgroundColor: config.color,
              opacity: status === 'connected' ? 0.55 : 1,
              animation: config.pulse ? 'pulse 2s ease-in-out infinite' : 'none'
            }}
            aria-hidden="true"
          />
          <style>{PULSE_KEYFRAMES}</style>
        </span>
      </ActionTooltip>
    )
  }

  return (
    <ActionTooltip label={label} placement="top" wrapperStyle={{ minWidth: 0, flex: 1 }}>
      <div
        style={{
        display: 'flex',
        alignItems: 'center',
        gap: '8px',
        minWidth: 0,
        flex: 1
      }}
      >
      <span style={SIDEBAR_NAV_ICON_SLOT}>
        <span
          style={{
            display: 'block',
            width: '8px',
            height: '8px',
            borderRadius: '50%',
            backgroundColor: config.color,
            flexShrink: 0,
            animation: config.pulse ? 'pulse 2s ease-in-out infinite' : 'none'
          }}
          aria-hidden="true"
        />
      </span>
      <span
        style={{
          fontSize: '12px',
          lineHeight: 1.2,
          color: 'var(--text-secondary)',
          overflow: 'hidden',
          textOverflow: 'ellipsis',
          whiteSpace: 'nowrap'
        }}
      >
        {label}
      </span>

      <style>{PULSE_KEYFRAMES}</style>
      </div>
    </ActionTooltip>
  )
}
