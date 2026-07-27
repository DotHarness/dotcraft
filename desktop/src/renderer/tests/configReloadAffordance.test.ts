import { describe, expect, it } from 'vitest'
import { getConfigReloadAffordance } from '../utils/configReloadAffordance'

describe('getConfigReloadAffordance', () => {
  it('returns live for hot fields', () => {
    expect(
      getConfigReloadAffordance({
        field: {
          key: 'DisabledSkills',
          sectionPath: ['Skills'],
          reload: 'hot'
        }
      })
    ).toEqual({ kind: 'live' })
  })

  it('returns subsystemRestart when subsystem key is present', () => {
    expect(
      getConfigReloadAffordance({
        field: {
          key: 'Enabled',
          sectionPath: ['Tools', 'Lsp'],
          reload: 'subsystemRestart',
          subsystemKey: 'lsp'
        }
      })
    ).toEqual({ kind: 'subsystemRestart', subsystemKey: 'lsp' })
  })

  it('falls back to processRestart for unknown reload values', () => {
    expect(
      getConfigReloadAffordance({
        field: {
          key: 'FutureField',
          reload: 'futureMode'
        }
      })
    ).toEqual({ kind: 'processRestart' })
  })

  it('does not lock fields outside AppConfig root', () => {
    expect(
      getConfigReloadAffordance({
        field: {
          key: 'Credential',
          sectionPath: ['Tools', 'Sandbox'],
          reload: 'processRestart'
        }
      })
    ).toEqual({ kind: 'processRestart' })
  })
})
