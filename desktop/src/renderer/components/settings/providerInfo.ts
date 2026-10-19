import { normalizeProviderProtocol } from '../../../shared/providerProtocols'

interface ProviderCapabilitiesWire {
  streamingChat?: boolean
  toolCalling?: boolean
  modelListing?: boolean
  tokenUsageReporting?: boolean
  cachedInputUsageReporting?: boolean
  promptCacheRequestShaping?: boolean
  extendedThinking?: boolean
  toolChoiceControls?: boolean
  rawMetadataPassthrough?: boolean
}

export interface ProviderInfoWire {
  backendId?: string
  id: string
  displayName: string
  protocol: string
  apiKey?: string | null
  managedBy?: string
  isAuthenticated?: boolean
  hasApiKey: boolean
  webSearchBackend?: string
  endPoint: string
  networkTimeoutSeconds?: number | null
  supportsImageGeneration?: boolean
  capabilities?: ProviderCapabilitiesWire
  authMethod?: 'apiKey' | 'chatgptOAuth'
  chatGptAccountId?: string | null
  chatGptPlanType?: string | null
}

export function normalizeProviderList(value: unknown): ProviderInfoWire[] {
  const source = value != null && typeof value === 'object' ? value as { providers?: unknown } : {}
  if (!Array.isArray(source.providers)) return []
  return source.providers
    .map((item): ProviderInfoWire | null => {
      if (item == null || typeof item !== 'object') return null
      const raw = item as Partial<ProviderInfoWire>
      const id = typeof raw.id === 'string' ? raw.id.trim() : ''
      if (!id) return null
      const rawAuthMethod = typeof raw.authMethod === 'string' ? raw.authMethod.toLowerCase() : ''
      return {
        id,
        backendId: typeof raw.backendId === 'string' ? raw.backendId : undefined,
        displayName: typeof raw.displayName === 'string' && raw.displayName.trim() !== '' ? raw.displayName : id,
        protocol: raw.backendId && typeof raw.protocol === 'string' ? raw.protocol : normalizeProviderProtocol(raw.protocol),
        apiKey: typeof raw.apiKey === 'string' ? raw.apiKey : null,
        managedBy: raw.managedBy,
        isAuthenticated: raw.isAuthenticated,
        hasApiKey: raw.hasApiKey === true,
        webSearchBackend: typeof raw.webSearchBackend === 'string' ? raw.webSearchBackend : undefined,
        endPoint: typeof raw.endPoint === 'string' ? raw.endPoint : '',
        networkTimeoutSeconds:
          typeof raw.networkTimeoutSeconds === 'number' && Number.isFinite(raw.networkTimeoutSeconds)
            ? raw.networkTimeoutSeconds
            : null,
        supportsImageGeneration: raw.supportsImageGeneration === true,
        capabilities: raw.capabilities,
        authMethod: rawAuthMethod === 'chatgptoauth' ? 'chatgptOAuth' : 'apiKey',
        chatGptAccountId: typeof raw.chatGptAccountId === 'string' && raw.chatGptAccountId.trim() !== ''
          ? raw.chatGptAccountId
          : null,
        chatGptPlanType: typeof raw.chatGptPlanType === 'string' && raw.chatGptPlanType.trim() !== ''
          ? raw.chatGptPlanType
          : null
      }
    })
    .filter((item): item is ProviderInfoWire => item != null)
}
