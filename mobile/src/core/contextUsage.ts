import type { ContextUsageSnapshot, SystemEventNotification, UsageDeltaNotification } from '@dotcraft/sdk/contracts'

export interface ContextUsage {
  tokens: number
  contextWindow: number
  percentLeft: number
}

export type ContextUpdate = { kind: 'snapshot'; snapshot: ContextUsage } | { kind: 'tokens'; tokens: number; percentLeft: number | null }

const TERMINAL_COMPACTION = new Set(['compacted', 'compactSkipped', 'compactFailed', 'compactCancelled'])

function clampShare(value: number): number {
  if (!Number.isFinite(value)) return 0
  return Math.min(1, Math.max(0, value))
}

function count(value: number | null | undefined): number {
  return Math.max(0, Math.trunc(value ?? 0))
}

export function contextUsageOf(snapshot: ContextUsageSnapshot | null | undefined): ContextUsage | null {
  if (!snapshot) return null
  return { tokens: count(snapshot.tokens), contextWindow: count(snapshot.contextWindow), percentLeft: clampShare(snapshot.percentLeft ?? 0) }
}

function tokensUpdate(tokens: number | null | undefined, percentLeft: number | null | undefined): ContextUpdate | null {
  return typeof tokens === 'number' ? { kind: 'tokens', tokens, percentLeft: typeof percentLeft === 'number' ? percentLeft : null } : null
}

export function usageDeltaUpdate(params: UsageDeltaNotification): ContextUpdate | null {
  const snapshot = contextUsageOf(params.contextUsage)
  if (snapshot) return { kind: 'snapshot', snapshot }
  return tokensUpdate(params.totalInputTokens ?? params.contextInputTokens, null)
}

export function systemEventUpdate(params: SystemEventNotification): ContextUpdate | null {
  if (!TERMINAL_COMPACTION.has(params.kind ?? '')) return null
  const snapshot = contextUsageOf(params.contextUsage)
  if (snapshot) return { kind: 'snapshot', snapshot }
  return tokensUpdate(params.tokenCount, params.percentLeft)
}

export function applyContext(current: ContextUsage | null, update: ContextUpdate): ContextUsage | null {
  if (update.kind === 'snapshot') return update.snapshot
  if (!current) return null
  const tokens = count(update.tokens)
  const percentLeft =
    update.percentLeft !== null
      ? clampShare(update.percentLeft)
      : current.contextWindow > 0
        ? clampShare(1 - tokens / current.contextWindow)
        : current.percentLeft
  return { ...current, tokens, percentLeft }
}

export function usedShare(usage: ContextUsage | null): number {
  if (!usage || usage.contextWindow <= 0) return 0
  return clampShare(usage.tokens / usage.contextWindow)
}

const UNITS = ['', 'K', 'M', 'B']

export function compactCount(value: number): string {
  const whole = Math.round(value)
  if (whole < 1000) return String(whole)
  let unit = Math.min(Math.floor(Math.log10(whole) / 3), UNITS.length - 1)
  let rounded = Math.round((whole / 1000 ** unit) * 10) / 10
  if (rounded >= 1000 && unit < UNITS.length - 1) {
    unit += 1
    rounded = Math.round((whole / 1000 ** unit) * 10) / 10
  }
  return `${rounded.toFixed(1).replace(/\.0$/, '')}${UNITS[unit]}`
}
