import type { ProviderAuthUsageResult, ProviderAuthUsageWindow } from '@dotcraft/sdk/contracts'

export interface UsageWindow {
  seconds: number
  percentLeft: number
  resetAt: string
}

const HOUR = 3600
const DAY = 24 * HOUR

function windowOf(raw: ProviderAuthUsageWindow | null | undefined): UsageWindow[] {
  if (!raw || typeof raw.usedPercent !== 'number' || typeof raw.windowSeconds !== 'number' || typeof raw.resetAt !== 'string') return []
  if (raw.windowSeconds <= 0) return []
  return [{ seconds: raw.windowSeconds, percentLeft: Math.round(Math.min(100, Math.max(0, 100 - raw.usedPercent))), resetAt: raw.resetAt }]
}

export function usageWindows(result: ProviderAuthUsageResult | null | undefined): UsageWindow[] {
  if (result?.available !== true) return []
  return [...windowOf(result.primary), ...windowOf(result.secondary)].sort((left, right) => left.seconds - right.seconds)
}

export function windowSpan(seconds: number): { unit: 'hours' | 'days'; count: number } {
  const hours = Math.max(1, Math.round(seconds / HOUR))
  return hours >= 24 && hours % 24 === 0 ? { unit: 'days', count: Math.round(seconds / DAY) } : { unit: 'hours', count: hours }
}
