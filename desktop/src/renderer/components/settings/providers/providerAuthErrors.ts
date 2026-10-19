import type { ProviderAuthLoginState } from '@dotcraft/sdk/contracts'
import { readAppServerErrorFields } from '../../../../shared/appServerError'
import type { MessageKey } from '../../../../shared/locales'

type Translate = (key: MessageKey, vars?: Record<string, string | number>) => string

const ERROR_KEYS: Record<string, MessageKey> = {
  ProviderLoginNotSupported: 'settings.llm.signIn.error.notSupported',
  ProviderManagedByModelService: 'settings.llm.signIn.error.managedByService',
  ProviderNotConfigured: 'settings.llm.signIn.error.notConfigured',
  ProviderLoginInProgress: 'settings.llm.signIn.error.inProgress',
  ProviderLoginPromptStale: 'settings.llm.signIn.error.promptStale'
}

export function describeProviderAuthError(error: unknown, t: Translate): string {
  const { code, data } = readAppServerErrorFields(error)
  const domainCode = typeof data?.code === 'string' ? data.code : code
  const key = domainCode ? ERROR_KEYS[domainCode] : undefined
  if (key) return t(key)
  if (typeof data?.fallbackText === 'string' && data.fallbackText.trim()) return data.fallbackText
  return error instanceof Error ? error.message : String(error)
}

export function describeLoginFailure(state: Pick<ProviderAuthLoginState, 'errorCode' | 'errorMessage'>, t: Translate): string {
  const key = state.errorCode ? ERROR_KEYS[state.errorCode] : undefined
  if (key) return t(key)
  return state.errorMessage?.trim() || state.errorCode || t('settings.llm.unknownError')
}
