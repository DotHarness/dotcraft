import type { MessageKey } from '../../../../shared/locales'
import { OTHER_KEY, UNKNOWN_KEY } from './chartSeries'

export type TFn = (key: MessageKey | string, vars?: Record<string, string | number>) => string

export type UsageDimension = 'tokenType' | 'surface' | 'model' | 'toolSource' | 'skill'

const SURFACE_KEYS: Record<string, MessageKey> = {
  'dotcraft-desktop': 'settings.usage.surface.desktop',
  cli: 'settings.usage.surface.cli',
  acp: 'settings.usage.surface.acp',
  automations: 'settings.usage.surface.automations',
  subagent: 'settings.usage.surface.subagent'
}

const TOKEN_TYPE_KEYS: Record<string, MessageKey> = {
  uncached: 'settings.usage.breakdown.fresh',
  cached: 'settings.usage.breakdown.cached',
  cacheWrite: 'settings.usage.breakdown.cacheWrite',
  output: 'settings.usage.breakdown.output'
}

/** Server keys are raw identifiers; the client owns their display names. */
export function usageKeyLabel(
  t: TFn,
  dimension: UsageDimension,
  key: string,
  pluginName?: (pluginId: string) => string | null
): string {
  if (key === OTHER_KEY) return t('settings.usage.label.other')
  if (key === UNKNOWN_KEY) return t('settings.usage.label.unknown')
  switch (dimension) {
    case 'tokenType':
      return TOKEN_TYPE_KEYS[key] ? t(TOKEN_TYPE_KEYS[key]) : key
    case 'surface':
      return SURFACE_KEYS[key] ? t(SURFACE_KEYS[key]) : key
    case 'toolSource':
      if (key === 'builtin') return t('settings.usage.label.builtin')
      if (key === 'client') return t('settings.usage.label.client')
      if (key === 'binding') return t('settings.usage.label.binding')
      if (key.startsWith('mcp:')) return t('settings.usage.label.mcp', { server: key.slice(4) })
      if (key.startsWith('plugin:')) return pluginName?.(key.slice(7)) ?? key.slice(7)
      return key
    default:
      return key
  }
}
