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

/**
 * Connection status indicator for the sidebar footer.
 * Shows a colored dot and label reflecting the current AppServer connection state.
 * Spec §5.3 and spec §9.7
 */
export function ConnectionStatusIndicator(): JSX.Element {
  const t = useT()
  const { status, errorMessage } = useConnectionStore()
  const config = STATUS_CONFIG[status]
  const label = connectionStatusLabel(status, errorMessage, t)

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

      <style>{`
        @keyframes pulse {
          0%, 100% { opacity: 1; }
          50% { opacity: 0.4; }
        }
      `}</style>
      </div>
    </ActionTooltip>
  )
}
