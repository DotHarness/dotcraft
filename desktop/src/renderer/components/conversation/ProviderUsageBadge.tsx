import {
  useEffect,
  useRef,
  useState,
  type CSSProperties,
  type JSX
} from 'react'
import { Plug } from 'lucide-react'
import { useLocale, useT } from '../../contexts/LocaleContext'
import { useConnectionStore } from '../../stores/connectionStore'
import {
  useProviderUsage,
  useProvidersStore,
  type ProviderSummary
} from '../../stores/providersStore'
import { tryNormalizeProviderProtocol } from '../../../shared/providerProtocols'
import { formatPlanName } from '../../utils/chatgptPlan'
import {
  shapeChatGptUsageWindows,
  type ChatGptUsageDisplayWindow,
  type ChatGptUsageWindowKind
} from '../../utils/chatgptUsageWindows'
import { formatBalanceAmount, providerTopUpUrl } from '../../utils/providerBalance'
import { ActionTooltip } from '../ui/ActionTooltip'
import { ProviderMark, type ProviderMarkKind } from '../ui/ProviderMark'
import { getProviderProtocolMarkKind } from '../settings/panels/ProviderProtocolIcon'
import { ProviderUsagePopover } from './ProviderUsagePopover'

interface ProviderUsageBadgeProps {
  provider: ProviderSummary | null
}

export function ProviderUsageBadge({ provider }: ProviderUsageBadgeProps): JSX.Element | null {
  const t = useT()
  const locale = useLocale()
  const providerId = provider?.id ?? null
  const usage = useProviderUsage(providerId)
  const providerManagement = useConnectionStore((state) => state.capabilities?.providerManagement === true)
  const refreshUsage = useProvidersStore((state) => state.refreshUsage)
  const [open, setOpen] = useState(false)
  const wrapperRef = useRef<HTMLDivElement | null>(null)

  useEffect(() => {
    if (providerId && providerManagement) void refreshUsage(providerId)
  }, [providerId, providerManagement, refreshUsage])

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

  if (!provider || !usage?.available) return null

  const title = provider.authMethod === 'chatgptOAuth' ? t('composer.chatgptUsage.title') : provider.displayName
  const label = provider.authMethod === 'chatgptOAuth' ? t('composer.chatgptBadge.label') : provider.displayName
  const planName = formatPlanName(usage.planType)
  const displayWindows = shapeChatGptUsageWindows(usage)
  const pressuredWindow = selectPressuredWindow(displayWindows)
  const remaining = remainingPercent(pressuredWindow?.window ?? null)
  const accentColor = colorForRemaining(remaining)
  const balanceMode = usage.balances != null && displayWindows.length === 0
  const firstBalance = usage.balances?.[0] ?? null
  const balanceText = firstBalance ? formatBalanceAmount(firstBalance.total, firstBalance.currency, locale) : '—'
  const insufficientBalance = usage.limitReachedKind === 'balance'
  const tooltipLabel = balanceMode
    ? insufficientBalance
      ? t('composer.balanceBadge.insufficient')
      : t('composer.balanceBadge.tooltip', { amount: balanceText })
    : formatUsageTooltipLabel(displayWindows, t)
  const markKind = providerMarkKind(provider)

  const ariaParts = [[label, planName].filter(Boolean).join(' ')]
  if (balanceMode) ariaParts.push(tooltipLabel)
  for (const display of displayWindows) {
    ariaParts.push(formatUsageAriaLabel(display.kind, remainingPercent(display.window) ?? 0, t))
  }
  const badgeButton = (
    <button
      type="button"
      onClick={() => setOpen((current) => !current)}
      aria-haspopup="dialog"
      aria-expanded={open}
      aria-label={ariaParts.join(' – ')}
      className="dc-composer-pill"
      style={balanceMode ? balanceBadgeStyle : badgeStyle}
    >
      {markKind
        ? <ProviderMark kind={markKind} size={14} style={iconStyle()} />
        : <Plug size={14} style={iconStyle()} aria-hidden />}
      {balanceMode ? (
        <span style={balanceTextStyle(insufficientBalance)} aria-hidden>{balanceText}</span>
      ) : (
        <span style={miniTrackStyle()} aria-hidden>
          <span style={{ ...miniFillStyle(), width: `${remaining ?? 0}%`, background: accentColor }} />
        </span>
      )}
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
          <ProviderUsagePopover
            title={title}
            usage={usage}
            topUpUrl={providerTopUpUrl(provider)}
            onClose={() => setOpen(false)}
          />
        </div>
      )}
    </div>
  )
}

function providerMarkKind(provider: ProviderSummary): ProviderMarkKind | null {
  if (provider.webSearchBackend === 'deepseek') return 'deepseek'
  return provider.authMethod === 'chatgptOAuth' || tryNormalizeProviderProtocol(provider.protocol)
    ? getProviderProtocolMarkKind(provider.protocol)
    : null
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

const badgeStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  gap: '6px',
  justifyContent: 'center',
  padding: '0 7px',
  height: '28px',
  width: '70px',
  borderRadius: '999px',
  border: 'none',
  color: 'var(--composer-footer-highlight, var(--text-primary))',
  fontSize: 'var(--type-ui-size)',
  lineHeight: 1.0,
  whiteSpace: 'nowrap',
  boxSizing: 'border-box',
  overflow: 'hidden',
  flexShrink: 0,
  cursor: 'pointer'
}

const balanceBadgeStyle: CSSProperties = {
  ...badgeStyle,
  width: 'auto',
  minWidth: '70px',
  maxWidth: '140px',
  padding: '0 9px'
}

function balanceTextStyle(insufficient: boolean): CSSProperties {
  return {
    minWidth: 0,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    fontVariantNumeric: 'tabular-nums',
    color: insufficient ? 'var(--error, #f85149)' : undefined
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
    left: 0,
    zIndex: 20
  }
}
