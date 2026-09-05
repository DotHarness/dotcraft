import { describe, expect, it } from 'vitest'

import { buildSettingsTabs } from '../components/settings/settingsTabs'
import { normalizeSettingsTab } from '../types/settings'

describe('buildSettingsTabs', () => {
  it('keeps every settings group contiguous when all optional tabs are available', () => {
    const tabs = buildSettingsTabs((key) => key, {
      personalizationAvailable: true,
      sourceControlEnabled: true,
      mcpEnabled: true,
      hooksEnabled: true,
      subAgentEnabled: true
    })

    expect(tabs.map(({ id }) => id)).toEqual([
      'general',
      'profile',
      'appearance',
      'personalization',
      'voice',
      'usage',
      'mcp',
      'browserUse',
      'computerControl',
      'hooks',
      'connections',
      'llmService',
      'sourceControl',
      'subAgents',
      'archivedThreads'
    ])
    expect(tabs.map(({ group }) => group)).toEqual([
      ...Array(6).fill('personal'),
      ...Array(3).fill('integrations'),
      ...Array(5).fill('coding'),
      'archived'
    ])
  })
})

describe('normalizeSettingsTab', () => {
  it('resolves the tabs that were merged into Connections', () => {
    expect(normalizeSettingsTab('servers')).toBe('connections')
    expect(normalizeSettingsTab('connection')).toBe('connections')
  })

  it('leaves every other tab id alone', () => {
    expect(normalizeSettingsTab('general')).toBe('general')
    expect(normalizeSettingsTab('desktop-plugin-settings:acme:general')).toBe(
      'desktop-plugin-settings:acme:general'
    )
  })
})
