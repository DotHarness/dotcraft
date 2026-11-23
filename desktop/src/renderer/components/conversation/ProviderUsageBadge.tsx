import {
  useEffect,
  useRef,
  useState,
  type CSSProperties,
  type JSX
} from 'react'
import { useLocale, useT } from '../../contexts/LocaleContext'
import { useConnectionStore } from '../../stores/connectionStore'
import {
  useProviderUsage,
  useProvidersStore,
  type ProviderSummary
} from '../../stores/providersStore'
import { formatPlanName } from '../../utils/chatgptPlan'
import {
  shapeChatGptUsageWindows,
  type ChatGptUsageDisplayWindow,
  type ChatGptUsageWindowKind
} from '../../utils/chatgptUsageWindows'
import { formatBalanceAmount } from '../../utils/providerBalance'
import { ProviderMark, type ProviderMarkKind } from '../ui/ProviderMark'
import { providerPresetMark } from '../settings/providers/providerPresets'
import { ProviderUsagePopover } from './ProviderUsagePopover'
import { ApiKeyUsageBadge } from './ApiKeyUsageBadge'
import { PluginUsageIcon } from './PluginUsageIcon'
import { MultiProviderUsageBadge } from './providerUsage/MultiProviderUsageBadge'
import type { UsageEntry } from './providerUsage/MultiProviderUsagePanel'
import { orderBySeverity, readProviderUsage } from './providerUsage/usageReading'
import { useUsageProviders } from './providerUsage/useUsageProviders'
import { UsageHoverCard, useUsageHover } from './providerUsage/UsageHoverCard'

interface ProviderUsageBadgeProps {
  provider: ProviderSummary | null
}

export function ProviderUsageBadge({ provider }: ProviderUsageBadgeProps): JSX.Element | null {
  const t = useT()
  const locale = useLocale()
  const providers = useUsageProviders(provider)
  const usageById = useProvidersStore((state) => state.usage)
  const providerManagement = useConnectionStore((state) => state.capabilities?.providerManagement === true)
  const refreshUsage = useProvidersStore((state) => state.refreshUsage)
  const providerIds = providers.map((candidate) => candidate.id).join('\n')

  useEffect(() => {
    if (!providerManagement) return
    for (const id of providerIds.split('\n')) if (id) void refreshUsage(id)
  }, [providerIds, providerManagement, refreshUsage])

  const toEntry = (candidate: ProviderSummary): UsageEntry[] => {
    const usage = usageById[candidate.id.trim().toLowerCase()] ?? null
    const reading = readProviderUsage(usage, locale, t)
    return usage && reading ? [{ provider: candidate, usage, reading }] : []
  }
  const [main, ...rest] = providers
  const others = orderBySeverity(rest.flatMap(toEntry))
  if (!main || others.length === 0) return <SingleProviderUsageBadge provider={provider} />
  return <MultiProviderUsageBadge main={main} lead={toEntry(main)[0] ?? null} others={others} />
}

function SingleProviderUsageBadge({ provider }: ProviderUsageBadgeProps): JSX.Element | null {
  const usage = useProviderUsage(provider?.id ?? null)
  if (provider?.backendId === 'openrouter') return <ApiKeyUsageBadge provider={provider} markKind="openrouter" />
  if (provider && usage?.available && usage.labels?.scope === 'apiKey') return <ApiKeyUsageBadge provider={provider} markKind={null} />
  return <ProviderQuotaUsageBadge provider={provider} />
}

function ProviderQuotaUsageBadge({ provider }: ProviderUsageBadgeProps): JSX.Element | null {
  const t = useT()
  const locale = useLocale()
  const providerId = provider?.id ?? null
  const usage = useProviderUsage(providerId)
  const providerManagement = useConnectionStore((state) => state.capabilities?.providerManagement === true)
  const refreshUsage = useProvidersStore((state) => state.refreshUsage)
  const [open, setOpen] = useState(false)
  const hover = useUsageHover()
  useEffect(() => {
    if (open && providerId && providerManagement) void refreshUsage(providerId)
  }, [open, providerId, providerManagement, refreshUsage])
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
  const balanceLabel = insufficientBalance
    ? t('composer.balanceBadge.insufficient')
    : t('composer.balanceBadge.tooltip', { amount: balanceText })
  const markKind = usage.pluginId ? null : providerMarkKind(provider)
  const reading = readProviderUsage(usage, locale, t)

  const ariaParts = [[label, planName].filter(Boolean).join(' ')]
  if (balanceMode) ariaParts.push(balanceLabel)
  for (const display of displayWindows) {
    ariaParts.push(formatUsageAriaLabel(display.kind, remainingPercent(display.window) ?? 0, t))
  }

  return (
    <div ref={wrapperRef} style={{ position: 'relative' }} {...hover.handlers}>
      <button
        type="button"
        onClick={() => {
          hover.dismiss()
          setOpen((current) => !current)
        }}
        aria-haspopup="dialog"
        aria-expanded={open}
        aria-label={ariaParts.join(' – ')}
        className="dc-composer-pill"
        style={balanceMode ? balanceBadgeStyle : badgeStyle}
      >
        {markKind
          ? <ProviderMark kind={markKind} size={14} style={iconStyle()} />
          : <PluginUsageIcon pluginId={usage.pluginId ?? provider.pluginId ?? null} size={14} style={iconStyle()} />}
        {balanceMode ? (
          <span style={balanceTextStyle(insufficientBalance)} aria-hidden>{balanceText}</span>
        ) : (
          <span style={miniTrackStyle()} aria-hidden>
            <span style={{ ...miniFillStyle(), width: `${remaining ?? 0}%`, background: accentColor }} />
          </span>
        )}
      </button>
      {hover.visible && !open && reading && <UsageHoverCard entries={[{ provider, usage, reading }]} />}
      {open && (
        <div style={popoverWrapperStyle()}>
          <ProviderUsagePopover title={title} usage={usage} pluginId={usage.pluginId ?? provider.pluginId ?? null} />
        </div>
      )}
    </div>
  )
}

function providerMarkKind(provider: ProviderSummary): ProviderMarkKind | null {
  if (provider.webSearchBackend === 'deepseek') return 'deepseek'
  return providerPresetMark(provider)
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
  minWidth: '70px',
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
  color: 'var(--composer-footer-text)',
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
