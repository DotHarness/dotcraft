import { useEffect, useState, type CSSProperties, type JSX } from 'react'
import { useT } from '../../contexts/LocaleContext'
import type { ChatGptUsageSnapshot, ChatGptUsageWindow } from '../../stores/providersStore'
import {
  shapeChatGptUsageWindows,
  type ChatGptUsageWindowKind
} from '../../utils/chatgptUsageWindows'

interface ChatGptUsagePopoverProps {
  usage: ChatGptUsageSnapshot | null
  onClose: () => void
}

const POPOVER_WIDTH = 280

export function ChatGptUsagePopover({ usage, onClose: _onClose }: ChatGptUsagePopoverProps): JSX.Element {
  const t = useT()
  const [now, setNow] = useState(() => Date.now())

  useEffect(() => {
    const id = window.setInterval(() => setNow(Date.now()), 1000)
    return () => window.clearInterval(id)
  }, [])

  if (!usage || !usage.available) {
    return (
      <div style={containerStyle()}>
        <div style={titleStyle()}>{t('composer.chatgptUsage.title')}</div>
        <div style={emptyStateStyle()}>{t('composer.chatgptUsage.unavailable')}</div>
      </div>
    )
  }

  const planLabel = formatPlanLabel(usage.planType, t)
  const displayWindows = shapeChatGptUsageWindows(usage)
  const hasSupplementalUsage = usage.credits?.hasCredits === true || usage.limitReachedKind != null
  return (
    <div style={containerStyle()} role="dialog" aria-label={t('composer.chatgptUsage.title')}>
      <div style={headerRowStyle()}>
        <div style={headerTextStyle()}>
          <span style={titleStyle()}>{t('composer.chatgptUsage.title')}</span>
          {usage.fetchedAt && (
            <span style={subtitleStyle()}>
              {t('composer.chatgptUsage.lastFetched', { time: formatRelative(usage.fetchedAt, now, t) })}
            </span>
          )}
        </div>
        <span style={planTagStyle()}>{planLabel}</span>
      </div>
      <div style={dividerStyle()} />

      {displayWindows.map((display, index) => (
        <UsageWindowRow
          key={`${display.kind}-${index}`}
          label={formatWindowLabel(display.kind, t)}
          window={display.window}
          now={now}
          t={t}
        />
      ))}

      {displayWindows.length === 0 && !hasSupplementalUsage && (
        <div style={emptyStateStyle()}>{t('composer.chatgptUsage.unavailable')}</div>
      )}

      {usage.credits?.hasCredits && (
        <div style={creditsRowStyle()}>
          <span>{t('composer.chatgptUsage.credits')}</span>
          <span style={{ fontVariantNumeric: 'tabular-nums' }}>
            {usage.credits.unlimited
              ? t('composer.chatgptUsage.creditsUnlimited')
              : usage.credits.balance ?? '—'}
          </span>
        </div>
      )}

      {usage.limitReachedKind && (
        <div style={limitWarnStyle()}>
          {t('composer.chatgptUsage.limitReached', { kind: usage.limitReachedKind })}
        </div>
      )}
    </div>
  )
}

interface UsageWindowRowProps {
  label: string
  window: ChatGptUsageWindow
  now: number
  t: (key: string, vars?: Record<string, string | number>) => string
}

function UsageWindowRow({ label, window, now, t }: UsageWindowRowProps): JSX.Element {
  const used = Math.max(0, Math.min(100, window.usedPercent))
  const remaining = 100 - used
  const color = colorForRemaining(remaining)
  const resetMs = new Date(window.resetAt).getTime() - now
  return (
    <div style={windowRowStyle()}>
      <div style={windowLabelRowStyle()}>
        <span style={windowLabelStyle()}>{label}</span>
      </div>
      <div style={progressTrackStyle()}>
        <div style={{ ...progressFillStyle(), width: `${remaining}%`, background: color }} />
      </div>
      <div style={windowFooterStyle()}>
        <span style={{ color, fontVariantNumeric: 'tabular-nums', fontWeight: 600 }}>
          {t('composer.chatgptUsage.percentLeft', { percent: remaining })}
        </span>
        <span>
          {t('composer.chatgptUsage.resetsIn', { duration: formatDuration(resetMs, t) })}
        </span>
      </div>
    </div>
  )
}

function formatWindowLabel(
  kind: ChatGptUsageWindowKind,
  t: (key: string) => string
): string {
  switch (kind) {
    case 'fiveHour': return t('composer.chatgptUsage.windowFiveHour')
    case 'weekly': return t('composer.chatgptUsage.windowWeekly')
    case 'primary': return t('composer.chatgptUsage.windowPrimary')
    case 'secondary': return t('composer.chatgptUsage.windowSecondary')
  }
}

function colorForRemaining(remaining: number): string {
  if (remaining < 20) return 'var(--error, #f85149)'
  if (remaining < 40) return 'var(--warning, #d29922)'
  return 'var(--success, #3fb950)'
}

function formatPlanLabel(plan: string | null, t: (key: string) => string): string {
  switch (plan?.toLowerCase()) {
    case 'free': return t('composer.chatgptBadge.plan.free')
    case 'plus': return t('composer.chatgptBadge.plan.plus')
    case 'pro': return t('composer.chatgptBadge.plan.pro')
    case 'business': return t('composer.chatgptBadge.plan.business')
    case 'enterprise': return t('composer.chatgptBadge.plan.enterprise')
    case 'edu':
    case 'education': return t('composer.chatgptBadge.plan.edu')
    default: return t('composer.chatgptBadge.plan.unknown')
  }
}

function formatDuration(ms: number, t: (key: string, vars?: Record<string, string | number>) => string): string {
  if (ms <= 0) return t('composer.chatgptUsage.duration.now')
  const totalSeconds = Math.floor(ms / 1000)
  const days = Math.floor(totalSeconds / 86400)
  const hours = Math.floor((totalSeconds % 86400) / 3600)
  const minutes = Math.floor((totalSeconds % 3600) / 60)
  if (days > 0) return t('composer.chatgptUsage.duration.daysHours', { days, hours })
  if (hours > 0) return t('composer.chatgptUsage.duration.hoursMinutes', { hours, minutes })
  if (minutes > 0) return t('composer.chatgptUsage.duration.minutes', { minutes })
  return t('composer.chatgptUsage.duration.lessThanMinute')
}

function formatRelative(iso: string, now: number, t: (key: string, vars?: Record<string, string | number>) => string): string {
  const diff = now - new Date(iso).getTime()
  if (diff < 60_000) return t('composer.chatgptUsage.relative.justNow')
  const minutes = Math.floor(diff / 60_000)
  if (minutes < 60) return t('composer.chatgptUsage.relative.minutesAgo', { minutes })
  const hours = Math.floor(minutes / 60)
  return t('composer.chatgptUsage.relative.hoursAgo', { hours })
}

function containerStyle(): CSSProperties {
  return {
    width: POPOVER_WIDTH,
    padding: '12px 14px',
    borderRadius: 10,
    border: '1px solid var(--border-default)',
    background: 'var(--bg-secondary)',
    boxShadow: '0 10px 32px rgba(0,0,0,0.32)',
    color: 'var(--text-primary)',
    fontSize: 12,
    lineHeight: 1.45,
    display: 'flex',
    flexDirection: 'column',
    gap: 10
  }
}

function headerRowStyle(): CSSProperties {
  return { display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between', gap: 8 }
}

function headerTextStyle(): CSSProperties {
  return { display: 'flex', flexDirection: 'column', gap: 2, minWidth: 0 }
}

function subtitleStyle(): CSSProperties {
  return { color: 'var(--text-dimmed, var(--text-secondary))', fontSize: 11 }
}

function dividerStyle(): CSSProperties {
  return { height: 1, background: 'var(--border-default)' }
}

function titleStyle(): CSSProperties {
  return { fontWeight: 600, fontSize: 13, color: 'var(--text-primary)' }
}

function planTagStyle(): CSSProperties {
  return {
    fontSize: 11,
    padding: '2px 8px',
    borderRadius: 999,
    border: '1px solid var(--border-default)',
    background: 'var(--bg-primary)',
    color: 'var(--text-secondary)'
  }
}

function windowRowStyle(): CSSProperties {
  return { display: 'flex', flexDirection: 'column', gap: 4 }
}

function windowLabelRowStyle(): CSSProperties {
  return { display: 'flex', alignItems: 'baseline', justifyContent: 'space-between', gap: 8 }
}

function windowLabelStyle(): CSSProperties {
  return { color: 'var(--text-secondary)', fontSize: 11, fontWeight: 500, letterSpacing: 0.2 }
}

function progressTrackStyle(): CSSProperties {
  return { height: 6, borderRadius: 999, background: 'var(--bg-tertiary)', overflow: 'hidden' }
}

function progressFillStyle(): CSSProperties {
  return { height: '100%', transition: 'width 0.4s ease, background 0.4s ease' }
}

function windowFooterStyle(): CSSProperties {
  return {
    color: 'var(--text-dimmed, var(--text-secondary))',
    fontSize: 11,
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: 8
  }
}

function creditsRowStyle(): CSSProperties {
  return {
    display: 'flex',
    justifyContent: 'space-between',
    padding: '6px 0',
    borderTop: '1px solid var(--border-default)',
    color: 'var(--text-secondary)',
    fontSize: 11
  }
}

function limitWarnStyle(): CSSProperties {
  return {
    marginTop: 4,
    padding: '6px 8px',
    borderRadius: 6,
    background: 'color-mix(in srgb, var(--warning, #d29922) 18%, transparent)',
    color: 'var(--warning, #d29922)',
    fontSize: 11
  }
}

function emptyStateStyle(): CSSProperties {
  return {
    padding: '10px 0',
    color: 'var(--text-secondary)',
    fontSize: 11,
    fontStyle: 'italic'
  }
}
