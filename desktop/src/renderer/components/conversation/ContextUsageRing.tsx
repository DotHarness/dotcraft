import { useMemo, useState, useRef, useEffect } from 'react'
import { useConversationStore } from '../../stores/conversationStore'
import { useT } from '../../contexts/LocaleContext'
import { formatCompactCount } from '../../utils/formatCompactCount'
import { COMPOSER_FOOTER_CONTROL_HEIGHT } from './ComposerShell'
import { revealContext } from './summary/view'

/**
 * Share of the model context window consumed by the active thread. The ring does not
 * change color by severity and renders nothing until a persisted snapshot exists.
 */
export function ContextUsageRing(): JSX.Element | null {
  const t = useT()
  const usage = useConversationStore((s) => s.contextUsage)
  const [hovered, setHovered] = useState(false)
  const anchorRef = useRef<HTMLButtonElement | null>(null)

  const ringData = useMemo(() => {
    if (!usage || usage.contextWindow <= 0) return null
    const filled = Math.max(0, Math.min(1, usage.tokens / usage.contextWindow))
    const percentUsed = Math.round(filled * 100)
    return { filled, percentUsed }
  }, [usage])

  useEffect(() => {
    if (!hovered) return
    const onKey = (e: KeyboardEvent): void => {
      if (e.key === 'Escape') setHovered(false)
    }
    window.addEventListener('keydown', onKey)
    return () => {
      window.removeEventListener('keydown', onKey)
    }
  }, [hovered])

  if (!usage || !ringData) return null

  const size = 14
  const stroke = 2
  const radius = (size - stroke) / 2
  const circumference = 2 * Math.PI * radius
  const dash = circumference * ringData.filled

  const color = 'var(--composer-footer-text, var(--text-secondary, #a5a5a5))'
  const trackColor = 'color-mix(in srgb, var(--composer-footer-text, #a5a5a5) 28%, transparent)'
  const formattedTokens = formatCompactCount(Math.min(usage.tokens, usage.contextWindow))
  const formattedWindow = formatCompactCount(usage.contextWindow)
  return (
    <button
      ref={anchorRef}
      type="button"
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
      onFocus={() => setHovered(true)}
      onBlur={() => setHovered(false)}
      onClick={(event) => {
        setHovered(false)
        revealContext(event.detail === 0)
      }}
      aria-label={t('contextRing.ariaLabel', { percent: ringData.percentUsed })}
      style={{
        position: 'relative',
        display: 'inline-flex',
        alignItems: 'center',
        justifyContent: 'center',
        width: COMPOSER_FOOTER_CONTROL_HEIGHT,
        height: COMPOSER_FOOTER_CONTROL_HEIGHT,
        padding: 0,
        border: 'none',
        borderRadius: '999px',
        background: 'transparent',
        cursor: 'pointer',
        outline: 'none',
        transition: 'none'
      }}
    >
      <svg
        width={size}
        height={size}
        viewBox={`0 0 ${size} ${size}`}
        style={{ transform: 'rotate(-90deg)', transition: 'color 200ms ease' }}
      >
        <circle
          cx={size / 2}
          cy={size / 2}
          r={radius}
          fill="none"
          stroke={trackColor}
          strokeWidth={stroke}
        />
        <circle
          cx={size / 2}
          cy={size / 2}
          r={radius}
          fill="none"
          stroke={color}
          strokeWidth={stroke}
          strokeDasharray={`${dash} ${circumference}`}
          strokeLinecap="round"
          style={{ transition: 'stroke-dasharray 200ms ease' }}
        />
      </svg>

      {hovered && (
        <span
          role="tooltip"
          style={{
            position: 'absolute',
            display: 'block',
            bottom: 'calc(100% + 6px)',
            right: 0,
            minWidth: 220,
            padding: '8px 10px',
            borderRadius: 6,
            background: 'var(--bg-elevated, #1f2228)',
            color: 'var(--text-primary, #f5f5f5)',
            border: '1px solid var(--border-default)',
            boxShadow: '0 4px 16px rgba(0,0,0,0.3)',
            fontSize: 11,
            lineHeight: 1.5,
            pointerEvents: 'none',
            zIndex: 50,
            whiteSpace: 'nowrap',
            textAlign: 'left'
          }}
        >
          <span style={{ display: 'block', fontWeight: 600, marginBottom: 4 }}>
            {t('contextRing.tooltip.title')}
          </span>
          <span style={{ display: 'block' }}>
            {t('contextRing.tooltip.used', {
              percent: ringData.percentUsed,
              left: 100 - ringData.percentUsed
            })}
          </span>
          <span style={{ display: 'block', color: 'var(--text-secondary, #a5a5a5)' }}>
            {t('contextRing.tooltip.tokens', { used: formattedTokens, total: formattedWindow })}
          </span>
        </span>
      )}
    </button>
  )
}
