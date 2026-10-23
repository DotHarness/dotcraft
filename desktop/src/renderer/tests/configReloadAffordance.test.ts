import { describe, expect, it } from 'vitest'
import { getConfigReloadAffordance } from '../utils/configReloadAffordance'

describe('getConfigReloadAffordance', () => {
  it('maps hot, subsystem and unknown reload modes', () => {
    expect(getConfigReloadAffordance({
      field: { key: 'DisabledSkills', sectionPath: ['Skills'], reload: 'hot' }
    })).toEqual({ kind: 'live' })
    expect(getConfigReloadAffordance({
      field: { key: 'Enabled', sectionPath: ['Tools', 'Lsp'], reload: 'subsystemRestart', subsystemKey: 'lsp' }
    })).toEqual({ kind: 'subsystemRestart', subsystemKey: 'lsp' })
    expect(getConfigReloadAffordance({
      field: { key: 'FutureField', reload: 'futureMode' }
    })).toEqual({ kind: 'processRestart' })
    expect(getConfigReloadAffordance({
      field: { key: 'Credential', sectionPath: ['Tools', 'Sandbox'], reload: 'processRestart' }
    })).toEqual({ kind: 'processRestart' })
  })
})
