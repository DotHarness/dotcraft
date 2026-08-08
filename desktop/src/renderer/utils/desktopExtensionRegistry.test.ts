import { describe, expect, it } from 'vitest'

import type { PluginEntry } from '../stores/pluginStore'
import {
  buildExtensionSettingsPanelKey,
  findDesktopSettingsPanelExtension,
  getDesktopSettingsPanelExtensions
} from './desktopExtensionRegistry'

function plugin(overrides: Partial<PluginEntry> = {}): PluginEntry {
  return {
    id: 'oratorio',
    displayName: 'Oratorio',
    installed: true,
    enabled: true,
    desktopExtensions: [{
      id: 'oratorio',
      displayName: 'Oratorio',
      entryPath: 'F:/plugins/oratorio/desktop/oratorio.mjs',
      surfaces: [{
        type: 'settingsPanel',
        settingsId: 'oratorio',
        label: 'Oratorio',
        localizedLabel: { 'zh-Hans': 'Oratorio' },
        order: 45
      }]
    }],
    ...overrides
  } as PluginEntry
}

describe('desktop settings extension registry', () => {
  it('discovers an enabled settings panel and creates a stable key', () => {
    const entries = getDesktopSettingsPanelExtensions([plugin()])
    expect(entries).toHaveLength(1)
    expect(entries[0].settingsKey).toBe('extension-settings:oratorio:oratorio:oratorio')
    expect(findDesktopSettingsPanelExtension([plugin()], entries[0].settingsKey)?.settingsId).toBe('oratorio')
  })

  it('does not expose disabled or uninstalled plugin surfaces', () => {
    expect(getDesktopSettingsPanelExtensions([plugin({ enabled: false })])).toEqual([])
    expect(getDesktopSettingsPanelExtensions([plugin({ installed: false })])).toEqual([])
  })

  it('encodes descriptor identifiers in settings keys', () => {
    expect(buildExtensionSettingsPanelKey('a:b', 'c d', 'e/f')).toBe(
      'extension-settings:a%3Ab:c%20d:e%2Ff'
    )
  })
})
