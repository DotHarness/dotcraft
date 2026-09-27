import type { ReasoningEffortWire, ReasoningOutputWire } from '../../stores/modelCatalogStore'
import type { ReasoningQuickValue } from './ModelPicker'

export interface ResolvedReasoningConfig {
  enabled: boolean
  effort: ReasoningEffortWire
  output: ReasoningOutputWire
}

export const DEFAULT_REASONING_CONFIG: ResolvedReasoningConfig = {
  enabled: false,
  effort: 'medium',
  output: 'full'
}

export function buildReasoningPayload(
  value: ReasoningQuickValue,
  current: ResolvedReasoningConfig
): ResolvedReasoningConfig | null {
  if (value === 'default') return null
  if (value === 'off') {
    return {
      enabled: false,
      effort: current.effort,
      output: current.output
    }
  }
  return {
    enabled: true,
    effort: value,
    output: current.output
  }
}

export function readReasoningObject(value: unknown): ResolvedReasoningConfig | null {
  if (!value || typeof value !== 'object') return null
  const obj = value as Record<string, unknown>
  const enabledRaw = obj.enabled ?? obj.Enabled
  const effort = normalizeReasoningEffort(obj.effort ?? obj.Effort)
  const output = normalizeReasoningOutput(obj.output ?? obj.Output)
  return {
    enabled: typeof enabledRaw === 'boolean' ? enabledRaw : false,
    effort: effort ?? 'medium',
    output: output ?? 'full'
  }
}

function normalizeReasoningEffort(value: unknown): ReasoningEffortWire | null {
  if (typeof value !== 'string') return null
  const normalized = value.replace(/[-_\s]/g, '').toLowerCase()
  if (normalized === 'low') return 'low'
  if (normalized === 'medium') return 'medium'
  if (normalized === 'high') return 'high'
  if (normalized === 'extrahigh') return 'extraHigh'
  if (normalized === 'max') return 'max'
  if (normalized === 'ultra') return 'ultra'
  return null
}

function normalizeReasoningOutput(value: unknown): ReasoningOutputWire | null {
  if (typeof value !== 'string') return null
  const normalized = value.trim().toLowerCase()
  if (normalized === 'none') return 'none'
  if (normalized === 'summary') return 'summary'
  if (normalized === 'full') return 'full'
  return null
}
