import type { ConfigDescriptorWire, ModuleConfigStatus } from './channelModules'

function getNestedConfigValue(
  config: Record<string, unknown>,
  dottedKey: string
): unknown {
  const parts = dottedKey.split('.').filter(Boolean)
  if (parts.length === 0) return undefined
  let current: unknown = config
  for (const part of parts) {
    if (current == null || typeof current !== 'object' || Array.isArray(current)) return undefined
    current = (current as Record<string, unknown>)[part]
  }
  return current
}

/** `dotcraft.*` keys are injected by the host, so users never fill them in. */
export function findMissingRequiredConfigFields(
  config: Record<string, unknown>,
  descriptors: ConfigDescriptorWire[]
): string[] {
  const missing: string[] = []
  for (const descriptor of descriptors) {
    if (!descriptor.required) continue
    if (descriptor.key.startsWith('dotcraft.')) continue
    const value = getNestedConfigValue(config, descriptor.key)
    const isMissing =
      value == null ||
      (typeof value === 'string' && value.trim() === '') ||
      (Array.isArray(value) && value.length === 0)
    if (isMissing) {
      missing.push(descriptor.displayLabel || descriptor.key)
    }
  }
  return missing
}

export function isModuleConfigured(status: ModuleConfigStatus | undefined): boolean {
  return status !== undefined && status.exists && status.missingRequired.length === 0
}
