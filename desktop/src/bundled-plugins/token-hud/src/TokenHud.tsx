import type { DesktopPluginSessionSnapshot, DesktopPluginSurfaceProps } from '@dotcraft/plugin'
import { useEffect, useState, useSyncExternalStore, type JSX, type MouseEvent } from 'react'
import { createPortal } from 'react-dom'
import { stringsFor } from './i18n'
import { getSettings, subscribeSettings } from './settings'
import { useCountedValue, useSpeedTrend, type SpeedTrend } from './speedTrend'
import { ThreadUsagePopover } from './ThreadUsagePopover'
import { getUsage, subscribeUsage } from './usage'

function useSettings(): ReturnType<typeof getSettings> {
  return useSyncExternalStore(subscribeSettings, getSettings, getSettings)
}

function useUsage(): ReturnType<typeof getUsage> {
  return useSyncExternalStore(subscribeUsage, getUsage, getUsage)
}

type TokenHudHost = DesktopPluginSurfaceProps<'composer.status.trailing'>['host']

function snapshotOf({ session }: TokenHudHost): DesktopPluginSessionSnapshot {
  const { workspacePath, threadId, mode, busy } = session
  return { workspacePath, threadId, mode, busy }
}

function useSession(host: TokenHudHost): DesktopPluginSessionSnapshot {
  const [session, setSession] = useState(() => snapshotOf(host))
  useEffect(() => {
    setSession(snapshotOf(host))
    return host.session.onChange(setSession)
  }, [host])
  return session
}

export function formatTokens(value: number): string {
  const scaled = (number: number): string => number >= 100
    ? String(Math.round(number))
    : String(Math.round(number * 10) / 10)
  if (value < 1_000) return String(Math.round(value))
  if (value < 1_000_000) return `${scaled(value / 1_000)}K`
  return `${scaled(value / 1_000_000)}M`
}

export function formatTokensPerSecond(value: number): string {
  return value >= 10 ? String(Math.round(value)) : String(Math.round(value * 10) / 10)
}

/** A cost too small to show a cent reads as "<$0.01" rather than "$0.00". */
export function formatCost(value: number, locale?: string): string {
  const format = new Intl.NumberFormat(locale, { style: 'currency', currency: 'USD', minimumFractionDigits: 2, maximumFractionDigits: 2 })
  return value > 0 && value < 0.01 ? `<${format.format(0.01)}` : format.format(value)
}

export function formatLatency(ms: number): string {
  const seconds = Math.max(0, ms) / 1000
  return `${seconds < 10 ? Math.round(seconds * 10) / 10 : Math.round(seconds)}s`
}

function TrendChevron({ trend }: { trend: SpeedTrend }): JSX.Element {
  return (
    <svg className="token-hud-trend" data-trend={trend} viewBox="0 0 8 8" width="8" height="8" aria-hidden="true">
      <path
        d={trend === 'up' ? 'M1.5 5.25 4 2.75l2.5 2.5' : 'M1.5 2.75 4 5.25l2.5-2.5'}
        fill="none"
        stroke="currentColor"
        strokeWidth="1.3"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </svg>
  )
}

export function TokenHud({ host }: DesktopPluginSurfaceProps<'composer.status.trailing'>): JSX.Element | null {
  const settings = useSettings()
  const usage = useUsage()
  const session = useSession(host)
  const strings = stringsFor(host.environment.locale)
  const [popover, setPopover] = useState<{ right: number; bottom: number } | null>(null)
  const countedSpeed = useCountedValue(usage.tokensPerSecond)
  const speedCue = useSpeedTrend(usage.latestRequestTokensPerSecond)
  const showPopover = (event: MouseEvent<HTMLDivElement>): void => {
    const rect = event.currentTarget.getBoundingClientRect()
    setPopover({ right: window.innerWidth - rect.right, bottom: window.innerHeight - rect.top + 8 })
  }

  if (!settings?.visible) return null

  const speedValue = usage.tokensPerSecond !== null
    ? formatTokensPerSecond(countedSpeed ?? usage.tokensPerSecond)
    : usage.waitingForSample || session.busy ? '…' : null
  const speedTrend = usage.tokensPerSecond !== null ? speedCue.trend : null
  const ariaParts: string[] = []
  if (speedValue !== null) {
    ariaParts.push(
      usage.tokensPerSecond !== null
        ? `${strings.speedLabel} ${formatTokensPerSecond(usage.tokensPerSecond)} tok/s${speedTrend === null ? '' : `, ${speedTrend === 'up' ? strings.speedFaster : strings.speedSlower}`}`
        : strings.speedPending
    )
  }
  if (usage.firstTokenLatencyMs !== null) {
    ariaParts.push(`${strings.latencyLabel} ${formatLatency(usage.firstTokenLatencyMs)}`)
  }
  if (usage.cacheHitRate !== null) ariaParts.push(`${strings.cacheLabel} ${Math.round(usage.cacheHitRate * 100)}%`)
  if (ariaParts.length === 0) return null

  return (
    <>
    <div
      className="token-hud"
      data-busy={session.busy ? 'true' : 'false'}
      role="status"
      aria-live="off"
      aria-label={`${strings.hudLabel}: ${ariaParts.join(', ')}`}
      onMouseEnter={showPopover}
      onMouseLeave={() => setPopover(null)}
    >
      {speedValue !== null ? (
        <span className="token-hud-cell" data-metric="speed">
          <span className="token-hud-speed">
            {speedTrend === null ? null : <TrendChevron key={speedCue.key} trend={speedTrend} />}
            <span className="token-hud-value">{speedValue}</span>
          </span>
          <span>tok/s</span>
        </span>
      ) : null}
      {usage.firstTokenLatencyMs !== null ? (
        <span className="token-hud-cell" data-metric="latency">
          <span className="token-hud-value">{formatLatency(usage.firstTokenLatencyMs)}</span>
          <span>{strings.latency}</span>
        </span>
      ) : null}
      {usage.cacheHitRate !== null ? (
        <span className="token-hud-cell" data-metric="cache">
          <span className="token-hud-value">{Math.round(usage.cacheHitRate * 100)}%</span>
          <span>{strings.cache}</span>
        </span>
      ) : null}
    </div>
    {popover && session.threadId
      ? createPortal(
          <ThreadUsagePopover
            usage={usage.threadUsage ?? null}
            strings={strings}
            formatTokens={formatTokens}
            formatCost={(value) => formatCost(value, host.environment.locale)}
            style={{ right: popover.right, bottom: popover.bottom }}
          />,
          document.body
        )
      : null}
    </>
  )
}
