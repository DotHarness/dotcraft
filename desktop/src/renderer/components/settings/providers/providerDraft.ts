import type { ProviderBackendInfo } from '@dotcraft/sdk/contracts'
import type { MessageKey } from '../../../../shared/locales'
import {
  ANTHROPIC_PROTOCOL,
  defaultProviderEndpoint,
  normalizeProviderProtocol,
  OPENAI_CHAT_COMPLETIONS_PROTOCOL,
  OPENAI_RESPONSES_PROTOCOL,
  providerProtocolLabel,
  tryNormalizeProviderProtocol,
  type DesktopProviderProtocol
} from '../../../../shared/providerProtocols'
import type { ProviderFields } from '../providerConfigWrites'
import type { ProviderInfoWire } from '../providerInfo'

export const API_KEY_METHOD = 'apiKey'
export const CHATGPT_METHOD = 'chatgptOAuth'
const CHATGPT_CONNECTION = 'chatgpt'
const BACKEND_CONNECTION_PREFIX = 'backend:'
const STORED_API_KEY = '********'

export interface ProviderDraft {
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

export type ProviderConnectionMode = 'apiKey' | 'signIn' | 'none'

export interface ProviderConnectionOption {
  value: string
  label: string
  description?: string
  disabled?: boolean
  mode: ProviderConnectionMode
  protocol: string
  backendId: string | null
  authMethod: string
}

type Translate = (key: MessageKey, vars?: Record<string, string | number>) => string

export function createProviderDraft(): ProviderDraft {
  return withDefaultImageGenerationSupport({
    id: '',
    displayName: '',
    protocol: OPENAI_RESPONSES_PROTOCOL,
    backendId: null,
    apiKey: '',
    endPoint: defaultProviderEndpoint(OPENAI_RESPONSES_PROTOCOL),
    networkTimeoutSeconds: '',
    authMethod: API_KEY_METHOD,
    supportsImageGeneration: false,
    supportsImageGenerationTouched: false
  })
}

export function providerDraftFromInfo(provider: ProviderInfoWire): ProviderDraft {
  return {
    id: provider.id,
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

export function providerConnectionOptions(
  backends: readonly ProviderBackendInfo[],
  t: Translate
): ProviderConnectionOption[] {
  const apiKey = (protocol: DesktopProviderProtocol, label: MessageKey): ProviderConnectionOption => ({
    value: protocol, label: t(label), mode: 'apiKey', protocol, backendId: null, authMethod: API_KEY_METHOD
  })
  return [
    apiKey(OPENAI_RESPONSES_PROTOCOL, 'settings.llm.connectionType.openaiResponses'),
    apiKey(OPENAI_CHAT_COMPLETIONS_PROTOCOL, 'settings.llm.connectionType.openaiLegacy'),
    apiKey(ANTHROPIC_PROTOCOL, 'settings.llm.connectionType.anthropic'),
    {
      value: CHATGPT_CONNECTION,
      label: t('settings.llm.connectionType.chatgpt'),
      mode: 'signIn',
      protocol: OPENAI_RESPONSES_PROTOCOL,
      backendId: null,
      authMethod: CHATGPT_METHOD
    },
    ...backends.flatMap((backend) => {
      const methods = backend.authenticationMethods.length > 0 ? backend.authenticationMethods : [null]
      return methods.map((method): ProviderConnectionOption => ({
        value: backendConnectionValue(backend.id, method?.id ?? ''),
        label: method && methods.length > 1 ? `${backend.displayName} · ${method.displayName}` : backend.displayName,
        mode: method && method.kind !== 'ambient' ? 'signIn' : 'none',
        protocol: backend.defaultProtocol,
        backendId: backend.id,
        authMethod: method?.id ?? ''
      }))
    })
  ]
}

export function providerConnectionValue(draft: Pick<ProviderDraft, 'backendId' | 'protocol' | 'authMethod'>): string {
  if (draft.backendId) return backendConnectionValue(draft.backendId, draft.authMethod)
  if (draft.authMethod === CHATGPT_METHOD) return CHATGPT_CONNECTION
  return normalizeProviderProtocol(draft.protocol)
}

export function selectedProviderConnection(
  draft: Pick<ProviderDraft, 'backendId' | 'protocol' | 'authMethod'>,
  options: readonly ProviderConnectionOption[]
): ProviderConnectionOption | null {
  const value = providerConnectionValue(draft)
  return options.find((option) => option.value === value) ?? null
}

export function applyProviderConnection(draft: ProviderDraft, option: ProviderConnectionOption): ProviderDraft {
  if (option.backendId) {
    return {
      ...draft,
      backendId: option.backendId,
      protocol: option.protocol,
      authMethod: option.authMethod,
      apiKey: '',
      endPoint: ''
    }
  }
  const protocol = normalizeProviderProtocol(option.protocol)
  const previousProtocol = draft.backendId ? null : tryNormalizeProviderProtocol(draft.protocol)
  const endPoint = option.mode === 'signIn'
    ? ''
    : !previousProtocol || !draft.endPoint || draft.endPoint === defaultProviderEndpoint(previousProtocol)
      ? defaultProviderEndpoint(protocol)
      : draft.endPoint
  return withDefaultImageGenerationSupport({
    ...draft,
    backendId: null,
    protocol,
    endPoint,
    authMethod: option.authMethod,
    apiKey: option.mode === 'signIn' ? '' : draft.apiKey
  })
}

export function providerConnectionMode(
  draft: Pick<ProviderDraft, 'backendId' | 'protocol' | 'authMethod'>,
  backends: readonly ProviderBackendInfo[]
): ProviderConnectionMode {
  if (!draft.backendId) return draft.authMethod === CHATGPT_METHOD ? 'signIn' : 'apiKey'
  const backend = backends.find((candidate) => candidate.id === draft.backendId)
  if (!backend) return 'signIn'
  const method = backend.authenticationMethods.find((candidate) => candidate.id === draft.authMethod)
  return method && method.kind !== 'ambient' ? 'signIn' : 'none'
}

function backendConnectionValue(backendId: string, method: string): string {
  return `${BACKEND_CONNECTION_PREFIX}${backendId}:${method}`
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
  return [
    provider.backendId ? backend?.displayName ?? provider.backendId : providerProtocolLabel(provider.protocol),
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
  return isOfficialOpenAIEndpoint(draft.endPoint.trim() || defaultProviderEndpoint(protocol))
}

function isOfficialOpenAIEndpoint(endpoint: string): boolean {
  try {
    const candidate = new URL(endpoint)
    const official = new URL(defaultProviderEndpoint(OPENAI_RESPONSES_PROTOCOL))
    return candidate.protocol.toLowerCase() === official.protocol.toLowerCase() &&
      candidate.hostname.toLowerCase() === official.hostname.toLowerCase() &&
      candidate.port === official.port &&
      candidate.pathname.replace(/\/+$/, '').toLowerCase() === official.pathname.replace(/\/+$/, '').toLowerCase()
  } catch {
    return false
  }
}

export function draftBuiltInProtocol(draft: Pick<ProviderDraft, 'protocol'>): DesktopProviderProtocol {
  return normalizeProviderProtocol(draft.protocol)
}
