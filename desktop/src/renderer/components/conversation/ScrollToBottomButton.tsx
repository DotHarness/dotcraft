import { ArrowDown } from 'lucide-react'
import { ActionTooltip } from '../ui/ActionTooltip'

interface ScrollToBottomButtonProps {
  onClick: () => void
  bottomOffsetPx?: number
}

/**
 * Floating button shown when the user has scrolled up from the bottom.
 * Clicking it jumps back to the latest messages.
 */
export function ScrollToBottomButton({ onClick, bottomOffsetPx = 10 }: ScrollToBottomButtonProps): JSX.Element {
  return (
    <ActionTooltip
      label="Scroll to bottom"
      placement="top"
      wrapperStyle={{
        position: 'absolute',
        bottom: `${bottomOffsetPx}px`,
        left: '50%',
        transform: 'translateX(-50%)',
        zIndex: 10
      }}
    >
      <button
        onClick={onClick}
        aria-label="Scroll to bottom"
        style={{
          width: '32px',
          height: '32px',
          borderRadius: '50%',
          background: 'var(--composer-input-background)',
          backdropFilter: 'var(--glass-blur-soft)',
          WebkitBackdropFilter: 'var(--glass-blur-soft)',
          border: '1px solid var(--glass-border)',
          color: 'var(--text-primary)',
          cursor: 'pointer',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          lineHeight: 0,
          boxShadow: '0 8px 24px rgba(0, 0, 0, 0.22), var(--glass-shadow-soft)',
          transition: 'background-color 100ms ease, color 100ms ease, transform 100ms ease'
        }}
      >
        <ArrowDown size={18} strokeWidth={1.9} aria-hidden="true" />
      </button>
    </ActionTooltip>
  )
}
