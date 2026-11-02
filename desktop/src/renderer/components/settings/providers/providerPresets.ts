import type { ProviderBackendInfo } from '@dotcraft/sdk/contracts'
import type { MessageKey } from '../../../../shared/locales'
import {
  ANTHROPIC_PROTOCOL,
  DEFAULT_ANTHROPIC_ENDPOINT,
  DEFAULT_OPENAI_ENDPOINT,
  DESKTOP_PROVIDER_PROTOCOLS,
  OPENAI_RESPONSES_PROTOCOL,
  tryNormalizeProviderProtocol,
  type DesktopProviderProtocol
} from '../../../../shared/providerProtocols'
import type { ProviderMarkKind } from '../../ui/ProviderMark'

export const API_KEY_METHOD = 'apiKey'
export const CHATGPT_METHOD = 'chatgptOAuth'
export const OPENROUTER_BACKEND_ID = 'openrouter'
export const OPENROUTER_ENDPOINT = 'https://openrouter.ai/api/v1'
export const DEEPSEEK_ENDPOINT = 'https://api.deepseek.com/anthropic'

export type ProviderConnectionMode = 'apiKey' | 'signIn' | 'none'
export type ProviderPresetId = 'chatgpt' | 'openai' | 'anthropic' | 'deepseek' | 'custom' | `backend:${string}`

export interface ProviderPresetAuth {
  method: string
  label: string
  mode: ProviderConnectionMode
}

export interface ProviderPreset {
  id: ProviderPresetId
  label: string
  hint: string
  mark: ProviderMarkKind | null
  backendId: string | null
  protocol: string
  endPoint: string
  defaultId: string
  defaultDisplayName: string
  auth: ProviderPresetAuth[]
}

export interface ProviderPresetConnection {
  backendId: string | null
  protocol: string
  authMethod: string
  endPoint: string
}

interface ProviderConnectionShape {
  backendId?: string | null
  authMethod?: string | null
  protocol: string
  endPoint?: string
}

type Translate = (key: MessageKey, vars?: Record<string, string | number>) => string

/** Backends compiled into the official host, for setup, which runs before AppServer can list backends. */
export const BUILT_IN_BACKENDS: readonly ProviderBackendInfo[] = [{
  id: OPENROUTER_BACKEND_ID,
  displayName: 'OpenRouter',
  defaultProtocol: 'auto',
  protocols: ['auto', 'anthropic', 'openai-responses', 'openai-chat-completions'],
  authenticationMethods: [
    { id: API_KEY_METHOD, displayName: 'API key', kind: 'apiKey' },
    { id: 'openrouterOAuth', displayName: 'Sign in with OpenRouter', kind: 'oauth' }
  ]
}]

export function providerPresets(backends: readonly ProviderBackendInfo[], t: Translate): ProviderPreset[] {
  const apiKey = (): ProviderPresetAuth[] => [{ method: API_KEY_METHOD, label: t('providerPreset.auth.apiKey'), mode: 'apiKey' }]
  const ordered = [...backends].sort((a, b) => Number(b.id === OPENROUTER_BACKEND_ID) - Number(a.id === OPENROUTER_BACKEND_ID))
  return [
    {
      id: 'chatgpt',
      label: t('providerPreset.chatgpt'),
      hint: t('providerPreset.chatgptHint'),
      mark: 'openai',
      backendId: null,
      protocol: OPENAI_RESPONSES_PROTOCOL,
      endPoint: '',
      defaultId: 'chatgpt',
      defaultDisplayName: 'ChatGPT',
      auth: [{ method: CHATGPT_METHOD, label: t('providerPreset.auth.signIn'), mode: 'signIn' }]
    },
    {
      id: 'openai',
      label: t('providerPreset.openai'),
      hint: t('providerPreset.openaiHint'),
      mark: 'openai',
      backendId: null,
      protocol: OPENAI_RESPONSES_PROTOCOL,
      endPoint: DEFAULT_OPENAI_ENDPOINT,
      defaultId: 'openai',
      defaultDisplayName: 'OpenAI',
      auth: apiKey()
    },
    {
      id: 'anthropic',
      label: t('providerPreset.anthropic'),
      hint: t('providerPreset.anthropicHint'),
      mark: 'anthropic',
      backendId: null,
      protocol: ANTHROPIC_PROTOCOL,
      endPoint: DEFAULT_ANTHROPIC_ENDPOINT,
      defaultId: 'anthropic',
      defaultDisplayName: 'Anthropic',
      auth: apiKey()
    },
    {
      id: 'deepseek',
      label: t('providerPreset.deepseek'),
      hint: t('providerPreset.deepseekHint'),
      mark: 'deepseek',
      backendId: null,
      protocol: ANTHROPIC_PROTOCOL,
      endPoint: DEEPSEEK_ENDPOINT,
      defaultId: 'deepseek',
      defaultDisplayName: 'DeepSeek',
      auth: apiKey()
    },
    ...ordered.map((backend) => backendPreset(backend, t)),
    {
      id: 'custom',
      label: t('providerPreset.custom'),
      hint: t('providerPreset.customHint'),
      mark: null,
      backendId: null,
      protocol: OPENAI_RESPONSES_PROTOCOL,
      endPoint: '',
      defaultId: '',
      defaultDisplayName: '',
      auth: apiKey()
    }
  ]
}

function backendPreset(backend: ProviderBackendInfo, t: Translate): ProviderPreset {
  const openRouter = backend.id === OPENROUTER_BACKEND_ID
  const methods = backend.authenticationMethods.length > 0 ? backend.authenticationMethods : [null]
  return {
    id: `backend:${backend.id}`,
    label: backend.displayName,
    hint: t(openRouter ? 'providerPreset.openrouterHint' : 'providerPreset.backendHint'),
    mark: openRouter ? 'openrouter' : null,
    backendId: backend.id,
    protocol: backend.defaultProtocol,
    endPoint: openRouter ? OPENROUTER_ENDPOINT : '',
    defaultId: backend.id,
    defaultDisplayName: backend.displayName,
    auth: methods.map((method) => {
      const mode = backendAuthMode(backend.id, method)
      return {
        method: method?.id ?? '',
        label: openRouter
          ? t(mode === 'apiKey' ? 'providerPreset.auth.apiKey' : 'providerPreset.auth.signIn')
          : method?.displayName ?? backend.displayName,
        mode
      }
    })
  }
}

/** Only OpenRouter stores a key in provider config; other backends collect credentials through sign-in prompts. */
export function backendAuthMode(
  backendId: string,
  method: { kind: string } | null | undefined
): ProviderConnectionMode {
  if (backendId === OPENROUTER_BACKEND_ID && method?.kind === 'apiKey') return 'apiKey'
  return method && method.kind !== 'ambient' ? 'signIn' : 'none'
}

export function findProviderPreset(presets: readonly ProviderPreset[], id: ProviderPresetId): ProviderPreset | null {
  return presets.find((preset) => preset.id === id) ?? null
}

export function providerPresetAuth(preset: ProviderPreset, method: string): ProviderPresetAuth {
  return preset.auth.find((candidate) => candidate.method === method) ?? preset.auth[0]
}

export function providerPresetIdFor(provider: ProviderConnectionShape): ProviderPresetId {
  if (provider.backendId) return `backend:${provider.backendId}`
  if (provider.authMethod === CHATGPT_METHOD) return 'chatgpt'
  const protocol = tryNormalizeProviderProtocol(provider.protocol)
  const endPoint = provider.endPoint?.trim() ?? ''
  if (protocol === OPENAI_RESPONSES_PROTOCOL && (!endPoint || sameEndpoint(endPoint, DEFAULT_OPENAI_ENDPOINT))) return 'openai'
  if (protocol === ANTHROPIC_PROTOCOL && (!endPoint || sameEndpoint(endPoint, DEFAULT_ANTHROPIC_ENDPOINT))) return 'anthropic'
  if (protocol === ANTHROPIC_PROTOCOL && sameEndpoint(endPoint, DEEPSEEK_ENDPOINT)) return 'deepseek'
  return 'custom'
}

/** A configured provider's mark: its preset's, else its API format's for custom endpoints; null draws the generic plug. */
export function providerPresetMark(provider: ProviderConnectionShape): ProviderMarkKind | null {
  const preset = providerPresetIdFor(provider)
  if (preset === 'chatgpt' || preset === 'openai') return 'openai'
  if (preset === 'anthropic' || preset === 'deepseek') return preset
  if (preset === 'custom') return tryNormalizeProviderProtocol(provider.protocol) === ANTHROPIC_PROTOCOL ? 'anthropic' : 'openai'
  return preset === `backend:${OPENROUTER_BACKEND_ID}` ? 'openrouter' : null
}

export function presetConnection(preset: ProviderPreset, method?: string): ProviderPresetConnection {
  return {
    backendId: preset.backendId,
    protocol: preset.protocol,
    authMethod: providerPresetAuth(preset, method ?? '').method,
    endPoint: preset.endPoint
  }
}

export function apiFormatOptions(t: Translate): Array<{ value: DesktopProviderProtocol; label: string }> {
  return DESKTOP_PROVIDER_PROTOCOLS.map((protocol) => ({ value: protocol, label: t(`providerPreset.format.${protocol}`) }))
}

export function sameEndpoint(endpoint: string, expected: string): boolean {
  try {
    const candidate = new URL(endpoint)
    const target = new URL(expected)
    return candidate.protocol.toLowerCase() === target.protocol.toLowerCase() &&
      candidate.hostname.toLowerCase() === target.hostname.toLowerCase() &&
      candidate.port === target.port &&
      candidate.pathname.replace(/\/+$/, '').toLowerCase() === target.pathname.replace(/\/+$/, '').toLowerCase()
  } catch {
    return false
  }
}

export function isOfficialEndpoint(endpoint: string): boolean {
  return [DEFAULT_OPENAI_ENDPOINT, DEFAULT_ANTHROPIC_ENDPOINT, DEEPSEEK_ENDPOINT].some((official) => sameEndpoint(endpoint, official))
}
