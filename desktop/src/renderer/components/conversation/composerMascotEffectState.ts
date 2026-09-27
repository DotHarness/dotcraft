import type { InferenceSpeedWire, ModelCatalogItem, ReasoningEffortWire } from '../../stores/modelCatalogStore'
import type { ReasoningQuickValue } from './ModelPicker'
import type { ComposerMascotReasoningEffort, ComposerMascotSpeed } from './ComposerShell'

export interface ComposerMascotEffectState {
  reasoningEffort: ComposerMascotReasoningEffort
  speed: ComposerMascotSpeed
}

export const DEFAULT_COMPOSER_MASCOT_EFFECT_STATE: ComposerMascotEffectState = {
  reasoningEffort: 'off',
  speed: 'standard',
}

interface ResolveComposerMascotEffectStateOptions {
  modelName: string
  modelCatalog: ModelCatalogItem[]
  reasoningValue: ReasoningQuickValue
  speedValue: InferenceSpeedWire
}

export function resolveComposerMascotEffectState({
  modelName,
  modelCatalog,
  reasoningValue,
  speedValue,
}: ResolveComposerMascotEffectStateOptions): ComposerMascotEffectState {
  const model = modelCatalog.find((item) => item.id === modelName)
  const resolvedReasoningEffort = reasoningValue === 'default'
    ? model?.reasoning?.defaultEffort ?? 'off'
    : reasoningValue
  const reasoningEffort = toComposerMascotReasoningEffort(resolvedReasoningEffort)
  const speed = speedValue === 'fast' && model?.speed?.supportedModes.includes('fast') === true
    ? 'fast'
    : 'standard'

  return {
    reasoningEffort,
    speed,
  }
}

export function toComposerMascotReasoningEffort(
  value: 'off' | ReasoningEffortWire
): ComposerMascotReasoningEffort {
  return value === 'ultra' ? 'max' : value
}
