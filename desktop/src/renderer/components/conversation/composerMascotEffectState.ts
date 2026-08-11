import type { ContextWindowMode } from '../../types/thread'
import type { InferenceSpeedWire, ModelCatalogItem, ReasoningEffortWire } from '../../stores/modelCatalogStore'
import type { ReasoningQuickValue } from './ModelPicker'
import type { ComposerMascotReasoningEffort, ComposerMascotSpeed } from './ComposerShell'

export interface ComposerMascotEffectState {
  reasoningEffort: ComposerMascotReasoningEffort
  speed: ComposerMascotSpeed
  contextMax: boolean
}

export const DEFAULT_COMPOSER_MASCOT_EFFECT_STATE: ComposerMascotEffectState = {
  reasoningEffort: 'off',
  speed: 'standard',
  contextMax: false
}

interface ResolveComposerMascotEffectStateOptions {
  modelName: string
  modelCatalog: ModelCatalogItem[]
  reasoningValue: ReasoningQuickValue
  speedValue: InferenceSpeedWire
  contextMode?: ContextWindowMode
  contextDegraded?: boolean
}

export function resolveComposerMascotEffectState({
  modelName,
  modelCatalog,
  reasoningValue,
  speedValue,
  contextMode,
  contextDegraded
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
    contextMax: contextMode === 'max' || contextDegraded === true
  }
}

export function toComposerMascotReasoningEffort(
  value: 'off' | ReasoningEffortWire
): ComposerMascotReasoningEffort {
  return value === 'ultra' ? 'extraHigh' : value
}
