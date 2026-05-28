export const OPENAI_RESPONSES_PROTOCOL = 'openai-responses'
export const OPENAI_CHAT_COMPLETIONS_PROTOCOL = 'openai-chat-completions'
export const ANTHROPIC_PROTOCOL = 'anthropic'
export const LEGACY_OPENAI_PROTOCOL = 'openai'

export type DesktopProviderProtocol =
  | typeof OPENAI_RESPONSES_PROTOCOL
  | typeof OPENAI_CHAT_COMPLETIONS_PROTOCOL
  | typeof ANTHROPIC_PROTOCOL

export const DESKTOP_PROVIDER_PROTOCOLS: DesktopProviderProtocol[] = [
  OPENAI_RESPONSES_PROTOCOL,
  OPENAI_CHAT_COMPLETIONS_PROTOCOL,
  ANTHROPIC_PROTOCOL
]

export const DEFAULT_OPENAI_ENDPOINT = 'https://api.openai.com/v1'
export const DEFAULT_ANTHROPIC_ENDPOINT = 'https://api.anthropic.com'

export function tryNormalizeProviderProtocol(value: unknown): DesktopProviderProtocol | null {
  if (typeof value !== 'string') {
    return null
  }

  switch (value.trim().toLowerCase()) {
    case OPENAI_RESPONSES_PROTOCOL:
      return OPENAI_RESPONSES_PROTOCOL
    case OPENAI_CHAT_COMPLETIONS_PROTOCOL:
    case LEGACY_OPENAI_PROTOCOL:
      return OPENAI_CHAT_COMPLETIONS_PROTOCOL
    case ANTHROPIC_PROTOCOL:
      return ANTHROPIC_PROTOCOL
    default:
      return null
  }
}

export function normalizeProviderProtocol(value: unknown): DesktopProviderProtocol {
  return tryNormalizeProviderProtocol(value) ?? OPENAI_CHAT_COMPLETIONS_PROTOCOL
}

export function defaultProviderEndpoint(protocol: DesktopProviderProtocol): string {
  return protocol === ANTHROPIC_PROTOCOL ? DEFAULT_ANTHROPIC_ENDPOINT : DEFAULT_OPENAI_ENDPOINT
}

export function providerProtocolLabel(protocol: DesktopProviderProtocol): string {
  switch (protocol) {
    case OPENAI_RESPONSES_PROTOCOL:
      return 'OpenAI-Responses'
    case OPENAI_CHAT_COMPLETIONS_PROTOCOL:
      return 'OpenAI-Legacy'
    case ANTHROPIC_PROTOCOL:
      return 'Anthropic'
  }
}
