import type { ProviderInfoWire } from '../../providerInfo'

export interface ImageGenerationConfig {
  enabled: boolean
  providerId: string
}

type ImageGenerationTrouble =
  | { kind: 'none' }
  | { kind: 'provider'; providerId: string; provider: ProviderInfoWire | null }

export function canProviderCreateImages(provider: ProviderInfoWire): boolean {
  if (provider.protocol !== 'openai-responses' && provider.protocol !== 'openai-chat-completions') return false
  if (provider.supportsImageGeneration !== true) return false
  return provider.authMethod === 'chatgptOAuth'
    || provider.managedBy === 'modelService'
    || provider.hasApiKey
}

export function resolveImageGenerationTrouble(
  config: ImageGenerationConfig,
  providers: ProviderInfoWire[],
  workspaceProviderId: string
): ImageGenerationTrouble | null {
  if (!config.enabled || providers.length === 0) return null
  if (!providers.some(canProviderCreateImages)) return { kind: 'none' }
  const providerId = config.providerId || workspaceProviderId
  if (!providerId) return null
  const provider = providers.find((candidate) => candidate.id === providerId) ?? null
  return provider && canProviderCreateImages(provider) ? null : { kind: 'provider', providerId, provider }
}
