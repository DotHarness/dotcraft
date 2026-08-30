import type { DesktopPluginSessionSnapshot, DesktopPluginSurfaceProps } from '@dotcraft/plugin'
import { useEffect, useState, useSyncExternalStore, type CSSProperties, type JSX } from 'react'
import { stringsFor } from './i18n'
import { getSettings, subscribeSettings } from './settings'
import { getUsage, subscribeUsage } from './usage'

function useSettings(): ReturnType<typeof getSettings> {
  return useSyncExternalStore(subscribeSettings, getSettings, getSettings)
}

function useUsage(): ReturnType<typeof getUsage> {
  return useSyncExternalStore(subscribeUsage, getUsage, getUsage)
}

type TokenHudHost = DesktopPluginSurfaceProps<'app.status'>['host']

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

export function TokenHud({ host }: DesktopPluginSurfaceProps<'app.status'>): JSX.Element | null {
  const settings = useSettings()
  const usage = useUsage()
  const session = useSession(host)
  const strings = stringsFor(host.environment.locale)

  if (!settings?.visible) return null

  const speedValue = usage.tokensPerSecond !== null
    ? formatTokensPerSecond(usage.tokensPerSecond)
    : usage.waitingForSample || session.busy ? '…' : '—'
  const ariaParts = [
    usage.tokensPerSecond !== null
      ? `${strings.speedLabel} ${speedValue} tok/s`
      : usage.waitingForSample || session.busy ? strings.speedPending : strings.speedUnavailable
  ]
  if (usage.totalTokens !== null) ariaParts.push(`${strings.totalLabel} ${formatTokens(usage.totalTokens)}`)
  if (usage.cacheHitRate !== null) ariaParts.push(`${strings.cacheLabel} ${Math.round(usage.cacheHitRate * 100)}%`)

  return (
    <div
      className="token-hud"
      data-busy={session.busy ? 'true' : 'false'}
      style={{ '--token-hud-opacity': settings.opacity / 100 } as CSSProperties}
      role="status"
      aria-live="off"
      aria-label={`${strings.hudLabel}: ${ariaParts.join(', ')}`}
    >
      <span className="token-hud-dot" aria-hidden="true" />
      <span className="token-hud-cell" data-metric="speed">
        <span className="token-hud-value">{speedValue}</span>
        <span>tok/s</span>
      </span>
      {usage.totalTokens !== null ? (
        <span className="token-hud-cell" data-metric="total">
          <span className="token-hud-value">{formatTokens(usage.totalTokens)}</span>
          <span>{strings.total}</span>
        </span>
      ) : null}
      {usage.cacheHitRate !== null ? (
        <span className="token-hud-cell" data-metric="cache">
          <span className="token-hud-value">{Math.round(usage.cacheHitRate * 100)}%</span>
          <span>{strings.cache}</span>
        </span>
      ) : null}
    </div>
  )
}
