import { describe, expect, it } from 'vitest'
import { translate } from '../../shared/locales'
import {
  connectionValid,
  finalDraft,
  initialConnections,
  type SetupConnection
} from '../components/setup/SetupAccessSection'
import {
  BUILT_IN_BACKENDS,
  findProviderPreset,
  providerPresets,
  type ProviderPresetId
} from '../components/settings/providers/providerPresets'

const presets = providerPresets(BUILT_IN_BACKENDS, (key, vars) => translate('en', key, vars))
const saved = [{ id: 'openrouter', displayName: 'OpenRouter', protocol: 'auto', backendId: 'openrouter', hasApiKey: true, endPoint: '' }]
const connections = initialConnections(presets, saved)
const preset = (id: ProviderPresetId) => findProviderPreset(presets, id)!
const edit = (id: ProviderPresetId, draft: Partial<SetupConnection['draft']>): SetupConnection =>
  ({ ...connections[id]!, draft: { ...connections[id]!.draft, ...draft } })

describe('setup access presets', () => {
  it('offers OpenRouter with an unused id and validates each method', () => {
    const openRouter = preset('backend:openrouter')
    expect(connections['backend:openrouter']!.draft).toMatchObject({ id: 'openrouter-2', backendId: 'openrouter', protocol: 'auto' })
    expect(connectionValid(openRouter, edit('backend:openrouter', { apiKey: '' }), { phase: 'idle' })).toBe(false)
    expect(connectionValid(openRouter, edit('backend:openrouter', { apiKey: 'sk-or' }), { phase: 'idle' })).toBe(true)
    const signIn = edit('backend:openrouter', { authMethod: 'openrouterOAuth' })
    expect(connectionValid(openRouter, signIn, { phase: 'idle' })).toBe(false)
    expect(connectionValid(openRouter, signIn, { phase: 'signedIn' })).toBe(true)
  })

  it('requires a name and endpoint for a custom endpoint and derives its id from the name', () => {
    const custom = preset('custom')
    expect(connectionValid(custom, edit('custom', { displayName: 'Local', endPoint: '' }), { phase: 'idle' })).toBe(false)
    const local = edit('custom', { displayName: 'Local LLM', endPoint: 'http://localhost:11434/v1' })
    expect(connectionValid(custom, local, { phase: 'idle' })).toBe(true)
    expect(finalDraft(custom, local, saved)).toMatchObject({ id: 'local-llm', displayName: 'Local LLM', backendId: null })
  })
})
