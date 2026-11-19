import type { ConfigReadResult } from '@dotcraft/sdk/contracts'
import { describe, expect, it } from 'vitest'
import { draftEdits, readPolicyState, type PolicyValues } from './policy'
import { matchingPreset, PRESETS, presetValues, replacesLayerValues } from './presets'

const DEFAULTS: PolicyValues = {
  types: [{ name: 'user', description: 'who the user is.' }, { name: 'feedback', description: 'how to work.' }],
  saveGuidance: 'Save durable knowledge.',
  extractionGuidance: 'Save what was missed.',
  consolidationGuidance: 'Merge duplicates.',
  extractionInterval: 7
}

const preset = (id: string) => PRESETS.find((candidate) => candidate.id === id)!

function read(user: Record<string, unknown>, workspace: Record<string, unknown>): ConfigReadResult {
  return {
    config: { Memory: { Enabled: true, AutoExtract: false, ...user, ...workspace }, Dreams: { Enabled: true } },
    origins: {},
    layers: [
      { name: { type: 'user', file: 'user.json' }, version: 'sha256:u', config: { Memory: user } },
      { name: { type: 'workspace', file: 'ws.json' }, version: 'sha256:w', config: { memory: workspace } }
    ]
  }
}

describe('memory policy presets', () => {
  it('names the preset the effective policy matches across layers', () => {
    const profile = presetValues(preset('profile-and-notes'), DEFAULTS)
    const state = readPolicyState(DEFAULTS, read(
      { Types: profile.types.map((type) => ({ Name: type.name, Description: type.description })), SaveGuidance: profile.saveGuidance },
      { extractionGuidance: profile.extractionGuidance, ConsolidationGuidance: `${profile.consolidationGuidance}\n` }
    ))
    expect(state.origins).toMatchObject({ types: 'user', saveGuidance: 'user', extractionGuidance: 'workspace', extractionInterval: 'default' })
    expect(matchingPreset(state.effective, DEFAULTS)).toBe('profile-and-notes')
    expect(matchingPreset({ ...state.effective, extractionInterval: 3 }, DEFAULTS)).toBeNull()
    expect(matchingPreset(DEFAULTS, DEFAULTS)).toBe('dotcraft')
  })

  it('asks before replacing values the chosen layer holds', () => {
    const state = readPolicyState(DEFAULTS, read({ ExtractionInterval: 3 }, {}))
    expect(replacesLayerValues(preset('dotcraft'), state.layers.user, DEFAULTS)).toBe(true)
    expect(replacesLayerValues(preset('dotcraft'), state.layers.workspace, DEFAULTS)).toBe(false)
  })

  it('edits only the settings a draft changes', () => {
    const draft = { ...DEFAULTS, saveGuidance: 'Save less.\r\n', extractionInterval: 7 }
    expect(draftEdits(draft, DEFAULTS)).toEqual([{ keyPath: 'Memory.SaveGuidance', value: 'Save less.', mergeStrategy: 'replace' }])
  })
})
