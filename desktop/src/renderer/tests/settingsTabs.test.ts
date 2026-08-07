import { describe, expect, it } from 'vitest'

import { buildSettingsTabs } from '../components/settings/settingsTabs'

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
      'connection',
      'servers',
      'llmService',
      'sourceControl',
      'subAgents',
      'archivedThreads'
    ])
    expect(tabs.map(({ group }) => group)).toEqual([
      ...Array(6).fill('personal'),
      ...Array(3).fill('integrations'),
      ...Array(6).fill('coding'),
      'archived'
    ])
  })
})
