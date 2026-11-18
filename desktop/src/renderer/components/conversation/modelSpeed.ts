import { MODEL_PREFERENCE_SPEEDS } from '../../../shared/modelPreference'
import type { InferenceSpeedWire, ModelSpeedCapability } from '../../stores/modelCatalogStore'

export type AcceleratedSpeed = Exclude<InferenceSpeedWire, 'standard'>

export function readInferenceSpeed(value: unknown): InferenceSpeedWire {
  if (typeof value !== 'string') return 'standard'
  const normalized = value.trim().toLowerCase()
  return MODEL_PREFERENCE_SPEEDS.find((speed) => speed === normalized) ?? 'standard'
}

export function acceleratedSpeeds(capability: ModelSpeedCapability | null | undefined): AcceleratedSpeed[] {
  return (capability?.supportedModes ?? []).filter((mode): mode is AcceleratedSpeed => mode !== 'standard')
}

export function effectiveSpeed(
  capability: ModelSpeedCapability | null | undefined,
  value: InferenceSpeedWire
): InferenceSpeedWire {
  return value !== 'standard' && acceleratedSpeeds(capability).includes(value) ? value : 'standard'
}
