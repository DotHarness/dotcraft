import type { ProviderBackendInfo } from '@dotcraft/sdk/contracts'
import type { MessageKey } from '../../../../shared/locales'
import {
  defaultProviderEndpoint,
  normalizeProviderProtocol,
  OPENAI_CHAT_COMPLETIONS_PROTOCOL,
  OPENAI_RESPONSES_PROTOCOL,
  tryNormalizeProviderProtocol,
  DEFAULT_OPENAI_ENDPOINT,
  type DesktopProviderProtocol
} from '../../../../shared/providerProtocols'
import { slugProviderId, uniqueProviderId } from '../../../utils/providerId'
import type { ProviderFields } from '../providerConfigWrites'
import type { ProviderInfoWire } from '../providerInfo'
import {
  API_KEY_METHOD,
  backendAuthMode,
  CHATGPT_METHOD,
  isOfficialEndpoint,
  presetConnection,
  providerPresetIdFor,
  sameEndpoint,
  type ProviderConnectionMode,
  type ProviderPreset,
  type ProviderPresetId
} from './providerPresets'

const STORED_API_KEY = '********'

export interface ProviderDraft {
  backendSettings?: Record<string, import('@dotcraft/sdk/contracts').JsonValue>
  /** Editor-only: the preset the user chose, so overriding an endpoint does not move the provider to another preset. */
  preset: ProviderPresetId
  id: string
  displayName: string
  protocol: string
  backendId: string | null
  apiKey: string
  endPoint: string
  networkTimeoutSeconds: string
  authMethod: string
  supportsImageGeneration: boolean
  supportsImageGenerationTouched: boolean
}

export type ProviderEditorId = string | '__new__' | null

type Translate = (key: MessageKey, vars?: Record<string, string | number>) => string

export function createProviderDraft(providers: ReadonlyArray<{ id: string }> = []): ProviderDraft {
  return withDefaultImageGenerationSupport({
    preset: 'openai',
    id: uniqueProviderId('openai', providers),
    displayName: 'OpenAI',
    protocol: OPENAI_RESPONSES_PROTOCOL,
    backendId: null,
    apiKey: '',
    endPoint: DEFAULT_OPENAI_ENDPOINT,
    networkTimeoutSeconds: '',
    authMethod: API_KEY_METHOD,
    supportsImageGeneration: false,
    supportsImageGenerationTouched: false
  })
}

export function providerDraftFromInfo(provider: ProviderInfoWire): ProviderDraft {
  return {
    preset: providerPresetIdFor(provider),
    id: provider.id,
    backendSettings: provider.backendSettings,
    displayName: provider.displayName,
    protocol: provider.backendId ? provider.protocol : normalizeProviderProtocol(provider.protocol),
    backendId: provider.backendId ?? null,
    apiKey: provider.hasApiKey ? STORED_API_KEY : '',
    endPoint: provider.endPoint ?? '',
    networkTimeoutSeconds:
      typeof provider.networkTimeoutSeconds === 'number' ? String(provider.networkTimeoutSeconds) : '',
    authMethod: provider.authMethod ?? API_KEY_METHOD,
    supportsImageGeneration: provider.supportsImageGeneration === true,
    supportsImageGenerationTouched: true
  }
}

export interface ProviderPresetNaming {
  previous: ProviderPreset | null
  providers: ReadonlyArray<{ id: string }>
}

/** Switches the draft to a preset; `naming` renames a new provider whose id and name still follow the previous preset. */
export function applyProviderPreset(
  draft: ProviderDraft,
  preset: ProviderPreset,
  naming: ProviderPresetNaming | null
): ProviderDraft {
  const connection = presetConnection(preset)
  const custom = preset.id === 'custom'
  const wasBuiltInKey = !draft.backendId && draft.authMethod === API_KEY_METHOD
  const next: ProviderDraft = {
    ...draft,
    preset: preset.id,
    backendId: connection.backendId,
    protocol: custom ? tryNormalizeProviderProtocol(draft.backendId ? '' : draft.protocol) ?? connection.protocol : connection.protocol,
    authMethod: connection.authMethod,
    endPoint: custom
      ? draft.backendId || isOfficialEndpoint(draft.endPoint) ? '' : draft.endPoint
      : connection.endPoint,
    apiKey: wasBuiltInKey && !preset.backendId && connection.authMethod === API_KEY_METHOD ? draft.apiKey : '',
    backendSettings: preset.backendId ? {} : draft.backendSettings
  }
  return withDefaultImageGenerationSupport(naming ? renameForPreset(next, draft, preset, naming) : next)
}

function renameForPreset(next: ProviderDraft, draft: ProviderDraft, preset: ProviderPreset, naming: ProviderPresetNaming): ProviderDraft {
  const previous = naming.previous
  const displayName = draft.displayName.trim()
  const id = draft.id.trim()
  const nameFollowsPreset = !displayName || displayName === previous?.defaultDisplayName
  const idFollowsPreset = !id || (nameFollowsPreset && (id === slugProviderId(displayName) ||
    (previous?.defaultId ? id === uniqueProviderId(previous.defaultId, naming.providers) : false)))
  return {
    ...next,
    displayName: nameFollowsPreset ? preset.defaultDisplayName : draft.displayName,
    id: idFollowsPreset ? preset.defaultId ? uniqueProviderId(preset.defaultId, naming.providers) : '' : draft.id
  }
}

export function applyProviderAuthMethod(draft: ProviderDraft, preset: ProviderPreset, method: string): ProviderDraft {
  const connection = presetConnection(preset, method)
  if (connection.authMethod === draft.authMethod) return draft
  return { ...draft, authMethod: connection.authMethod, apiKey: '' }
}

export function applyProviderApiFormat(draft: ProviderDraft, protocol: DesktopProviderProtocol): ProviderDraft {
  return withDefaultImageGenerationSupport({ ...draft, protocol })
}

export function providerConnectionMode(
  draft: Pick<ProviderDraft, 'backendId' | 'authMethod'>,
  backends: readonly ProviderBackendInfo[]
): ProviderConnectionMode {
  if (!draft.backendId) return draft.authMethod === CHATGPT_METHOD ? 'signIn' : 'apiKey'
  const backend = backends.find((candidate) => candidate.id === draft.backendId)
  if (!backend) return 'signIn'
  return backendAuthMode(backend.id, backend.authenticationMethods.find((candidate) => candidate.id === draft.authMethod))
}

export function canConfigureImageGeneration(draft: Pick<ProviderDraft, 'backendId' | 'protocol' | 'authMethod'>): boolean {
  if (draft.backendId) return false
  return isOpenAIFamilyProtocol(draft.protocol) || draft.authMethod === CHATGPT_METHOD
}

export function withDefaultImageGenerationSupport(draft: ProviderDraft): ProviderDraft {
  if (draft.supportsImageGenerationTouched) return draft
  return { ...draft, supportsImageGeneration: defaultImageGenerationSupport(draft) }
}

export function providerFieldsFromDraft(
  draft: ProviderDraft,
  savedProvider: ProviderInfoWire | null
): ProviderFields {
  const timeout = draft.networkTimeoutSeconds.trim()
  return {
    displayName: draft.displayName.trim(),
    protocol: draft.protocol,
    backendId: draft.backendId,
    backendSettings: draft.backendSettings,
    authMethod: draft.authMethod,
    apiKey: savedProvider?.hasApiKey === true && draft.apiKey === STORED_API_KEY ? null : draft.apiKey.trim(),
    endPoint: draft.endPoint.trim(),
    networkTimeoutSeconds: timeout ? Number(timeout) : null,
    supportsImageGeneration: canConfigureImageGeneration(draft) ? draft.supportsImageGeneration : null
  }
}

export function isStoredApiKey(draft: Pick<ProviderDraft, 'apiKey'>): boolean {
  return draft.apiKey === STORED_API_KEY
}

export function providerUsable(provider: ProviderInfoWire): boolean {
  if (provider.isAuthenticated !== undefined) return provider.isAuthenticated === true
  if (provider.backendId) return true
  return provider.hasApiKey
}

export function providerProblem(
  provider: ProviderInfoWire,
  backends: readonly ProviderBackendInfo[] | null,
  t: Translate
): string | null {
  if (provider.backendId) {
    if (backends && !backends.some((backend) => backend.id === provider.backendId)) {
      return t('settings.llm.providerBackendUnavailable')
    }
    return provider.isAuthenticated === false ? t('settings.llm.providerNotSignedIn') : null
  }
  if (provider.managedBy === 'modelService') {
    return provider.isAuthenticated ? null : t('settings.llm.serviceAuthenticationRequired')
  }
  if (provider.authMethod === CHATGPT_METHOD) {
    return provider.isAuthenticated === true ? null : t('settings.llm.providerChatGptNotSignedIn')
  }
  return provider.hasApiKey ? null : t('settings.llm.providerKeyMissing')
}

export function providerDetails(
  provider: ProviderInfoWire,
  backends: readonly ProviderBackendInfo[] | null,
  t: Translate
): string {
  const backend = provider.backendId ? backends?.find((candidate) => candidate.id === provider.backendId) : null
  const preset = providerPresetIdFor(provider)
  return [
    provider.backendId
      ? backend?.displayName ?? provider.backendId
      : preset === 'custom'
        ? t(`providerPreset.format.${normalizeProviderProtocol(provider.protocol)}`)
        : t(`providerPreset.${preset as 'chatgpt' | 'openai' | 'anthropic' | 'deepseek'}`),
    provider.endPoint.trim() || null,
    provider.managedBy === 'modelService' && provider.isAuthenticated ? t('settings.llm.managedByService') : null
  ].filter(Boolean).join(' · ')
}

function isOpenAIFamilyProtocol(protocol: string): boolean {
  return protocol === OPENAI_RESPONSES_PROTOCOL || protocol === OPENAI_CHAT_COMPLETIONS_PROTOCOL
}

function defaultImageGenerationSupport(draft: Pick<ProviderDraft, 'backendId' | 'protocol' | 'authMethod' | 'endPoint'>): boolean {
  if (draft.backendId) return false
  if (draft.authMethod === CHATGPT_METHOD) return true
  const protocol = tryNormalizeProviderProtocol(draft.protocol)
  if (!protocol || !isOpenAIFamilyProtocol(protocol)) return false
  return sameEndpoint(draft.endPoint.trim() || defaultProviderEndpoint(protocol), DEFAULT_OPENAI_ENDPOINT)
}

export function draftBuiltInProtocol(draft: Pick<ProviderDraft, 'protocol'>): DesktopProviderProtocol {
  return normalizeProviderProtocol(draft.protocol)
}
