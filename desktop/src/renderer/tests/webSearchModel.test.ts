import { describe, expect, it } from 'vitest'

import {
  EXA_KEY_MASK,
  isSecretStored,
  resolveExaKeyCommit
} from '../components/settings/panels/webSearch/webSearchModel'

describe('isSecretStored', () => {
  it('treats the masked read value as stored and empty values as unset', () => {
    expect(isSecretStored('***')).toBe(true)
    expect(isSecretStored('')).toBe(false)
    expect(isSecretStored(undefined)).toBe(false)
  })
})

describe('resolveExaKeyCommit', () => {
  it('does nothing when the field was not edited or still shows the mask', () => {
    expect(resolveExaKeyCommit(null, true)).toEqual({ kind: 'none' })
    expect(resolveExaKeyCommit(EXA_KEY_MASK, true)).toEqual({ kind: 'none' })
  })

  it('clears a stored key when the field is emptied', () => {
    expect(resolveExaKeyCommit('  ', true)).toEqual({ kind: 'clear' })
    expect(resolveExaKeyCommit('', false)).toEqual({ kind: 'none' })
  })

  it('saves a new trimmed key', () => {
    expect(resolveExaKeyCommit(' exa-key ', true)).toEqual({ kind: 'set', value: 'exa-key' })
  })
})
