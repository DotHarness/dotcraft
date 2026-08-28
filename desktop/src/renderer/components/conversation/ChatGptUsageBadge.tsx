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
  type ProviderSummary
} from '../../stores/providersStore'
import { formatPlanLabel } from '../../utils/chatgptPlan'
import {
  shapeChatGptUsageWindows,
  type ChatGptUsageDisplayWindow,
  type ChatGptUsageWindowKind
} from '../../utils/chatgptUsageWindows'
import { ActionTooltip } from '../ui/ActionTooltip'
import { ProviderMark } from '../ui/ProviderMark'
import { ChatGptUsagePopover } from './ChatGptUsagePopover'
import {
  composerFooterControlActiveBackground,
  composerFooterControlHoverBackground
} from './ComposerShell'

interface ChatGptUsageBadgeProps {
  /** Active OAuth provider; pass null/undefined to hide the badge. */
  provider: ProviderSummary | null
}

/** Footer chip for a provider signed in to a ChatGPT subscription; the bar shows the most pressured window. */
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
  const displayWindows = shapeChatGptUsageWindows(usage)
  const pressuredWindow = selectPressuredWindow(displayWindows)
  const remaining = remainingPercent(pressuredWindow?.window ?? null)
  const accentColor = colorForRemaining(remaining)
  const tooltipLabel = formatUsageTooltipLabel(displayWindows, t)

  const ariaParts = [t('composer.chatgptBadge.label'), planLabel]
  for (const display of displayWindows) {
    ariaParts.push(formatUsageAriaLabel(display.kind, remainingPercent(display.window) ?? 0, t))
  }
  const active = hovered || focused
  const badgeButton = (
    <button
      type="button"
      onClick={() => setOpen((current) => !current)}
      aria-haspopup="dialog"
      aria-expanded={open}
      aria-label={ariaParts.join(' – ')}
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
      // Only treat keyboard focus as "focused" so the highlight clears after a mouse
      // click + click-outside (a plain click leaves DOM focus on the button, which would
      // otherwise keep the chip stuck in its selected state once the popover closes).
      onFocus={(event) => {
        if (event.currentTarget.matches(':focus-visible')) setFocused(true)
      }}
      onBlur={() => setFocused(false)}
      style={badgeStyle(open, active)}
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

function selectPressuredWindow(windows: ChatGptUsageDisplayWindow[]): ChatGptUsageDisplayWindow | null {
  if (windows.length === 0) return null
  return windows.reduce((best, current) =>
    current.window.usedPercent > best.window.usedPercent ? current : best, windows[0])
}

function remainingPercent(window: ChatGptUsageDisplayWindow['window'] | null): number | null {
  if (!window) return null
  return Math.max(0, Math.min(100, 100 - window.usedPercent))
}

function formatUsageTooltipLabel(
  windows: ChatGptUsageDisplayWindow[],
  t: (key: string, vars?: Record<string, string | number>) => string
): string {
  const parts = windows.map((display) => formatUsageTooltipPart(
    display.kind,
    Math.round(remainingPercent(display.window) ?? 0),
    t
  ))
  return parts.length > 0 ? parts.join(', ') : t('composer.chatgptBadge.usageUnavailable')
}

function formatUsageTooltipPart(
  kind: ChatGptUsageWindowKind,
  percent: number,
  t: (key: string, vars?: Record<string, string | number>) => string
): string {
  switch (kind) {
    case 'fiveHour': return t('composer.chatgptBadge.sessionRemaining', { percent })
    case 'weekly': return t('composer.chatgptBadge.weeklyRemaining', { percent })
    case 'primary': return t('composer.chatgptBadge.primaryRemaining', { percent })
    case 'secondary': return t('composer.chatgptBadge.secondaryRemaining', { percent })
  }
}

function formatUsageAriaLabel(
  kind: ChatGptUsageWindowKind,
  percent: number,
  t: (key: string, vars?: Record<string, string | number>) => string
): string {
  switch (kind) {
    case 'fiveHour': return t('composer.chatgptBadge.aria.sessionLeft', { percent })
    case 'weekly': return t('composer.chatgptBadge.aria.weeklyLeft', { percent })
    case 'primary': return t('composer.chatgptBadge.aria.primaryLeft', { percent })
    case 'secondary': return t('composer.chatgptBadge.aria.secondaryLeft', { percent })
  }
}

function colorForRemaining(remaining: number | null): string {
  if (remaining == null) return 'var(--accent, #10b981)'
  if (remaining < 20) return 'var(--error, #f85149)'
  if (remaining < 40) return 'var(--warning, #d29922)'
  return 'var(--success, #3fb950)'
}

function badgeStyle(open: boolean, active: boolean): CSSProperties {
  return {
    display: 'inline-flex',
    alignItems: 'center',
    gap: '6px',
    justifyContent: 'center',
    padding: '0 7px',
    height: '24px',
    width: '70px',
    borderRadius: '999px',
    border: 'none',
    background: open
      ? composerFooterControlActiveBackground
      : active
        ? composerFooterControlHoverBackground
        : 'transparent',
    color: 'var(--composer-footer-highlight, var(--text-primary))',
    fontSize: '12px',
    lineHeight: 1.0,
    whiteSpace: 'nowrap',
    boxSizing: 'border-box',
    overflow: 'hidden',
    flexShrink: 0,
    cursor: 'pointer',
    transition: 'background-color 120ms ease, color 120ms ease'
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
