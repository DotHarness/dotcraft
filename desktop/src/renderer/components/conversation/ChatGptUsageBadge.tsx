import {
  useEffect,
  useRef,
  useState,
  type CSSProperties,
  type JSX
} from 'react'
import { useT } from '../../contexts/LocaleContext'
import {
  useChatGptUsage,
  type ChatGptUsageSnapshot,
  type ChatGptUsageWindow,
  type ProviderSummary
} from '../../stores/providersStore'
import { formatPlanLabel } from '../../utils/chatgptPlan'
import { ActionTooltip } from '../ui/ActionTooltip'
import { ProviderMark } from '../ui/ProviderMark'
import { ChatGptUsagePopover } from './ChatGptUsagePopover'

interface ChatGptUsageBadgeProps {
  /** Active OAuth provider; pass null/undefined to hide the badge. */
  provider: ProviderSummary | null
}

/**
 * Footer chip indicating the active provider is signed in to a ChatGPT subscription. When usage
 * data is available, displays a compact OpenAI mark plus the most pressured remaining headroom bar.
 * Click opens a popover with the two-window breakdown.
 */
export function ChatGptUsageBadge({ provider }: ChatGptUsageBadgeProps): JSX.Element | null {
  const t = useT()
  const usage = useChatGptUsage()
  const [open, setOpen] = useState(false)
  const [hovered, setHovered] = useState(false)
  const [focused, setFocused] = useState(false)
  const wrapperRef = useRef<HTMLDivElement | null>(null)

  useEffect(() => {
    if (!open) return
    function onPointerDown(event: PointerEvent): void {
      if (wrapperRef.current && !wrapperRef.current.contains(event.target as Node)) {
        setOpen(false)
      }
    }
    function onKey(event: KeyboardEvent): void {
      if (event.key === 'Escape') setOpen(false)
    }
    window.addEventListener('pointerdown', onPointerDown, true)
    window.addEventListener('keydown', onKey)
    return () => {
      window.removeEventListener('pointerdown', onPointerDown, true)
      window.removeEventListener('keydown', onKey)
    }
  }, [open])

  if (!provider) return null

  const planLabel = formatPlanLabel(provider.chatGptPlanType ?? usage?.planType ?? null, t)
  const pressuredWindow = selectPressuredWindow(usage)
  const remaining = remainingPercent(pressuredWindow)
  const primaryRemaining = remainingPercent(usage?.primary ?? null)
  const secondaryRemaining = remainingPercent(usage?.secondary ?? null)
  const accentColor = colorForRemaining(remaining)
  const tooltipLabel = formatUsageTooltipLabel(primaryRemaining, secondaryRemaining, t)

  const ariaParts = [t('composer.chatgptBadge.label'), planLabel]
  if (primaryRemaining != null) ariaParts.push(t('composer.chatgptBadge.aria.sessionLeft', { percent: primaryRemaining }))
  if (secondaryRemaining != null) ariaParts.push(t('composer.chatgptBadge.aria.weeklyLeft', { percent: secondaryRemaining }))
  const active = open || hovered || focused
  const badgeButton = (
    <button
      type="button"
      onClick={() => setOpen((current) => !current)}
      aria-haspopup="dialog"
      aria-expanded={open}
      aria-label={ariaParts.join(' – ')}
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
      onFocus={() => setFocused(true)}
      onBlur={() => setFocused(false)}
      style={badgeStyle(active)}
    >
      <ProviderMark kind="openai" size={14} style={iconStyle()} />
      <span style={miniTrackStyle()} aria-hidden>
        <span style={{ ...miniFillStyle(), width: `${remaining ?? 0}%`, background: accentColor }} />
      </span>
    </button>
  )

  return (
    <div ref={wrapperRef} style={{ position: 'relative' }}>
      {open ? badgeButton : (
        <ActionTooltip label={tooltipLabel} placement="top">
          {badgeButton}
        </ActionTooltip>
      )}
      {open && (
        <div style={popoverWrapperStyle()}>
          <ChatGptUsagePopover usage={usage} onClose={() => setOpen(false)} />
        </div>
      )}
    </div>
  )
}

function selectPressuredWindow(usage: ChatGptUsageSnapshot | null): ChatGptUsageWindow | null {
  if (!usage?.available) return null
  const candidates = [usage.primary, usage.secondary].filter((w): w is ChatGptUsageWindow => w != null)
  if (candidates.length === 0) return null
  return candidates.reduce((best, current) =>
    current.usedPercent > best.usedPercent ? current : best, candidates[0])
}

function remainingPercent(window: ChatGptUsageWindow | null): number | null {
  if (!window) return null
  return Math.max(0, Math.min(100, 100 - window.usedPercent))
}

function formatUsageTooltipLabel(
  primaryRemaining: number | null,
  secondaryRemaining: number | null,
  t: (key: string, vars?: Record<string, string | number>) => string
): string {
  const parts: string[] = []
  if (primaryRemaining != null) {
    parts.push(t('composer.chatgptBadge.sessionRemaining', { percent: Math.round(primaryRemaining) }))
  }
  if (secondaryRemaining != null) {
    parts.push(t('composer.chatgptBadge.weeklyRemaining', { percent: Math.round(secondaryRemaining) }))
  }
  return parts.length > 0 ? parts.join(', ') : t('composer.chatgptBadge.usageUnavailable')
}

function colorForRemaining(remaining: number | null): string {
  if (remaining == null) return 'var(--accent, #10b981)'
  if (remaining < 20) return 'var(--error, #f85149)'
  if (remaining < 40) return 'var(--warning, #d29922)'
  return 'var(--success, #3fb950)'
}

function badgeStyle(active: boolean): CSSProperties {
  return {
    display: 'inline-flex',
    alignItems: 'center',
    gap: '6px',
    justifyContent: 'center',
    padding: '0 7px',
    height: '24px',
    width: '70px',
    borderRadius: '999px',
    border: active
      ? '1px solid var(--accent)'
      : '1px solid transparent',
    background: active ? 'var(--bg-tertiary)' : 'transparent',
    color: 'var(--composer-footer-highlight, var(--text-primary))',
    fontSize: '12px',
    lineHeight: 1.0,
    whiteSpace: 'nowrap',
    boxSizing: 'border-box',
    overflow: 'hidden',
    flexShrink: 0,
    cursor: 'pointer'
  }
}

function iconStyle(): CSSProperties {
  return {
    width: 14,
    height: 14,
    display: 'block',
    flexShrink: 0,
    opacity: 0.86
  }
}

function miniTrackStyle(): CSSProperties {
  return {
    width: '42px',
    height: '4px',
    borderRadius: '999px',
    background: 'var(--bg-tertiary)',
    overflow: 'hidden',
    flexShrink: 0
  }
}

function miniFillStyle(): CSSProperties {
  return {
    display: 'block',
    height: '100%',
    borderRadius: '999px',
    transition: 'width 0.4s ease, background 0.4s ease'
  }
}

function popoverWrapperStyle(): CSSProperties {
  return {
    position: 'absolute',
    bottom: 'calc(100% + 8px)',
    right: 0,
    zIndex: 20
  }
}
