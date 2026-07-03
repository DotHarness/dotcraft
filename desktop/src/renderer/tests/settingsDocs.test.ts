import { describe, expect, it } from 'vitest'

import { resolveSettingsDocsUrl } from '../components/settings/settingsDocs'

describe('resolveSettingsDocsUrl', () => {
  it('uses English docs for English locale', () => {
    expect(resolveSettingsDocsUrl('modelProviders', 'en')).toBe(
      'https://www.dotcraft.net/features/entry-points/desktop#model-providers'
    )
  })

  it('falls back to English docs for non-Chinese locales', () => {
    expect(resolveSettingsDocsUrl('mcp', 'ja')).toBe(
      'https://www.dotcraft.net/features/agent-system/plugins-tools#mcp-servers'
    )
  })

  it('prefixes Chinese docs routes with /zh', () => {
    expect(resolveSettingsDocsUrl('memory', 'zh-Hans')).toBe(
      'https://www.dotcraft.net/zh/features/agent-system/memory'
    )
  })

  it('uses Chinese anchor overrides when headings differ', () => {
    expect(resolveSettingsDocsUrl('hooks', 'zh-Hans')).toBe(
      'https://www.dotcraft.net/zh/developing/configuration#automations-goals-与-hooks'
    )
    expect(resolveSettingsDocsUrl('servers', 'zh-Hans')).toBe(
      'https://www.dotcraft.net/zh/features/self-hosted/server-deployment#从-desktop-连接'
    )
  })
})
