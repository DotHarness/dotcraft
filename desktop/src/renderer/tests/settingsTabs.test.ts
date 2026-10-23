import { describe, expect, it } from 'vitest'
import { normalizeSettingsTab } from '../types/settings'

describe('normalizeSettingsTab', () => {
  it('resolves tabs merged into Connections and leaves every other id alone', () => {
    expect(normalizeSettingsTab('servers')).toBe('connections')
    expect(normalizeSettingsTab('connection')).toBe('connections')
    expect(normalizeSettingsTab('general')).toBe('general')
    expect(normalizeSettingsTab('desktop-plugin-settings:acme:general')).toBe('desktop-plugin-settings:acme:general')
  })
})
