import type { ProviderInfoWire } from '../../providerInfo'

export const SECRET_KEY_MASK = '********'

export type SecretKeyCommit =
  { kind: 'none' } | { kind: 'clear' } | { kind: 'set'; value: string }

export function isSecretStored(value: unknown): boolean {
  return typeof value === 'string' && value !== ''
}

export function resolveSecretKeyCommit(
  draft: string | null,
  stored: boolean
): SecretKeyCommit {
  if (draft === null) return { kind: 'none' }
  const value = draft.trim()
  if (value === SECRET_KEY_MASK) return { kind: 'none' }
  if (value === '') return stored ? { kind: 'clear' } : { kind: 'none' }
  return { kind: 'set', value }
}

export function normalizeSearchProvider(
  value: unknown
): 'Auto' | 'Exa' | 'Bing' | 'DeepSeek' {
  switch (typeof value === 'string' ? value.toLowerCase() : '') {
    case 'exa':
      return 'Exa'
    case 'bing':
      return 'Bing'
    case 'deepseek':
      return 'DeepSeek'
    default:
      return 'Auto'
  }
}

export function canProviderSearchDeepSeek(provider: ProviderInfoWire): boolean {
  return provider.webSearchBackend === 'deepseek'
}

export function isSearchProviderAuthenticated(
  provider: ProviderInfoWire
): boolean {
  return provider.managedBy === 'modelService'
    ? provider.isAuthenticated === true
    : provider.hasApiKey
}
