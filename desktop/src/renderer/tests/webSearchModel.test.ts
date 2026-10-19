import { describe, expect, it } from 'vitest'

import {
  SECRET_KEY_MASK,
  canProviderSearchDeepSeek,
  isSearchProviderAuthenticated,
  isSecretStored,
  resolveSecretKeyCommit
} from '../components/settings/panels/webSearch/webSearchModel'
import { normalizeProviderList } from '../components/settings/providerInfo'

describe('search provider metadata', () => {
  it('uses server capabilities instead of guessing from provider or model names', () => {
    const [legacy, supported, unauthenticated] = normalizeProviderList({
      providers: [
        { id: 'deepseek', hasApiKey: true },
        {
          id: 'account',
          webSearchBackend: 'deepseek',
          managedBy: 'modelService',
          isAuthenticated: true
        },
        {
          id: 'expired',
          webSearchBackend: 'deepseek',
          managedBy: 'modelService',
          isAuthenticated: false
        }
      ]
    })
    expect(canProviderSearchDeepSeek(legacy)).toBe(false)
    expect(canProviderSearchDeepSeek(supported)).toBe(true)
    expect(isSearchProviderAuthenticated(supported)).toBe(true)
    expect(isSearchProviderAuthenticated(unauthenticated)).toBe(false)
  })
})

describe('isSecretStored', () => {
  it('treats the masked read value as stored and empty values as unset', () => {
    expect(isSecretStored('***')).toBe(true)
    expect(isSecretStored('')).toBe(false)
    expect(isSecretStored(undefined)).toBe(false)
  })
})

describe('resolveSecretKeyCommit', () => {
  it('does nothing when the field was not edited or still shows the mask', () => {
    expect(resolveSecretKeyCommit(null, true)).toEqual({ kind: 'none' })
    expect(resolveSecretKeyCommit(SECRET_KEY_MASK, true)).toEqual({
      kind: 'none'
    })
  })

  it('clears a stored key when the field is emptied', () => {
    expect(resolveSecretKeyCommit('  ', true)).toEqual({ kind: 'clear' })
    expect(resolveSecretKeyCommit('', false)).toEqual({ kind: 'none' })
  })

  it('saves a new trimmed key', () => {
    expect(resolveSecretKeyCommit(' exa-key ', true)).toEqual({
      kind: 'set',
      value: 'exa-key'
    })
  })
})
