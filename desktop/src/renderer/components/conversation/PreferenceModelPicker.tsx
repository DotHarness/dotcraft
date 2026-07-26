import type { CSSProperties, JSX } from 'react'
import {
  cloneModelPreference,
  type ModelPreference
} from '../../../shared/modelPreference'
import type {
  ModelCatalogItem,
  ReasoningEffortWire
} from '../../stores/modelCatalogStore'
import { Input } from '../ui/Input'
import { Skeleton } from '../ui/Skeleton'
import { ModelPicker, type ReasoningQuickValue } from './ModelPicker'

const FIELD_TRIGGER_STYLE: CSSProperties = {
  width: '100%',
  maxWidth: 'none',
  height: '38px',
  minWidth: 0,
  padding: '0 10px',
  border: 'none',
  borderRadius: '7px',
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'space-between',
  gap: '8px',
  color: 'var(--text-primary)',
  outline: 'none',
  boxShadow: 'none',
  fontSize: 'var(--type-ui-size)',
  lineHeight: 'var(--type-ui-line-height)',
  fontWeight: 'var(--type-ui-weight)'
}

export interface PreferenceModelPickerProps {
  preference: ModelPreference
  models: ModelCatalogItem[]
  loading?: boolean
  disabled?: boolean
  errorMessage?: string | null
  manualFallback?: boolean
  onRetry?: () => void
  onChange: (preference: ModelPreference) => void
  onManualCommit?: (preference: ModelPreference) => void
  inputId?: string
  inputAriaLabel?: string
  placeholder?: string
}

export function PreferenceModelPicker({
  preference,
  models,
  loading = false,
  disabled = false,
  errorMessage = null,
  manualFallback = false,
  onRetry,
  onChange,
  onManualCommit,
  inputId,
  inputAriaLabel,
  placeholder
}: PreferenceModelPickerProps): JSX.Element {
  const active = models.find((model) => model.id === preference.model)

  if (loading) {
    return (
      <div
        role="status"
        aria-label={inputAriaLabel}
        style={{
          display: 'flex',
          alignItems: 'center',
          width: '100%',
          height: '40px',
          padding: '0 10px',
          border: '1px solid var(--border-default)',
          borderRadius: '8px',
          background: 'var(--bg-primary)'
        }}
      >
        <Skeleton width="42%" height={12} radius={6} />
      </div>
    )
  }

  if (manualFallback) {
    return (
      <Input
        id={inputId}
        aria-label={inputAriaLabel}
        value={preference.model}
        onChange={(event) => {
          const next = cloneModelPreference(preference)
          next.model = event.currentTarget.value
          onChange(next)
        }}
        onBlur={() => onManualCommit?.(cloneModelPreference(preference))}
        onKeyDown={(event) => {
          if (event.key !== 'Enter') return
          event.preventDefault()
          onManualCommit?.(cloneModelPreference(preference))
          event.currentTarget.blur()
        }}
        placeholder={placeholder}
        disabled={disabled}
        mono
      />
    )
  }

  return (
    <div
      className="dc-model-preference-field"
      style={{
        width: '100%',
        minWidth: 0,
        border: '1px solid var(--border-default)',
        borderRadius: '8px',
        background: 'var(--bg-primary)',
        opacity: disabled ? 0.56 : undefined
      }}
    >
      <ModelPicker
        modelName={preference.model}
        modelOptions={models.map((model) => model.id)}
        modelCatalog={models}
        modelListReady={!errorMessage && models.length > 0}
        reasoningValue={preference.reasoning.enabled
          ? preference.reasoning.effort as ReasoningEffortWire
          : 'off'}
        speedValue={preference.speed}
        disabled={disabled}
        errorMessage={errorMessage}
        onChange={(model) => {
          const next = cloneModelPreference(preference)
          next.model = model
          onChange(normalizePreferenceForModel(next, models))
        }}
        onReasoningChange={(value: ReasoningQuickValue) => {
          if (value === 'default') return
          const next = cloneModelPreference(preference)
          next.reasoning.enabled = value !== 'off'
          if (value !== 'off') next.reasoning.effort = value
          onChange(next)
        }}
        onSpeedChange={(speed) => {
          const next = cloneModelPreference(preference)
          next.speed = speed
          onChange(next)
        }}
        onRetry={onRetry}
        contextMode={preference.contextWindow.mode}
        contextSupportsMax={active?.contextWindow?.supportsMax === true}
        contextConfiguredWindow={active?.contextWindow?.configuredWindow ?? 0}
        onContextModeChange={(mode) => {
          const next = cloneModelPreference(preference)
          next.contextWindow.mode = mode
          onChange(next)
        }}
        allowDefaultModel={false}
        triggerVariant="field"
        triggerId={inputId}
        triggerAriaLabel={inputAriaLabel}
        triggerStyle={FIELD_TRIGGER_STYLE}
      />
    </div>
  )
}

export function normalizePreferenceForModel(
  preference: ModelPreference,
  models: ModelCatalogItem[]
): ModelPreference {
  const next = cloneModelPreference(preference)
  const model = models.find((item) => item.id === next.model)
  if (!model) return next
  const reasoning = model.reasoning
  if (reasoning) {
    if (!reasoning.supportsDisable && !next.reasoning.enabled) {
      next.reasoning.enabled = true
      next.reasoning.effort = reasoning.defaultEffort
      next.reasoning.output = reasoning.defaultOutput
    } else if (next.reasoning.enabled) {
      if (!reasoning.supportedEfforts.some((item) => item.effort === next.reasoning.effort)) {
        next.reasoning.effort = reasoning.defaultEffort
      }
      if (!reasoning.supportedOutputs.includes(next.reasoning.output)) {
        next.reasoning.output = reasoning.defaultOutput
      }
    }
  }
  if (model.contextWindow?.supportsMax !== true) next.contextWindow.mode = 'default'
  return next
}

export function createCatalogDefaultPreference(
  model: ModelCatalogItem | undefined,
  fallbackModel: string
): ModelPreference {
  const modelId = model?.id ?? fallbackModel.trim()
  const reasoning = model?.reasoning
  return {
    model: modelId,
    reasoning: reasoning && !reasoning.supportsDisable
      ? {
          enabled: true,
          effort: reasoning.defaultEffort,
          output: reasoning.defaultOutput
        }
      : {
          enabled: false,
          effort: reasoning?.defaultEffort ?? 'medium',
          output: reasoning?.defaultOutput ?? 'full'
        },
    speed: model?.speed?.defaultMode ?? 'standard',
    contextWindow: { mode: 'default' }
  }
}
