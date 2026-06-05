import { describe, expect, it } from 'vitest'
import type { PluginEntry } from '../stores/pluginStore'
import {
  buildExtensionMainViewKey,
  getDesktopMainViewExtensions,
  parseExtensionMainViewKey
} from '../utils/desktopExtensionRegistry'

describe('desktopExtensionRegistry', () => {
  it('returns enabled installed main-view extensions', () => {
    const plugins: PluginEntry[] = [
      plugin('demo', true, true),
      plugin('disabled', true, false),
      plugin('catalog-only', false, false)
    ]

    const views = getDesktopMainViewExtensions(plugins)

    expect(views).toHaveLength(1)
    expect(views[0].plugin.id).toBe('demo')
    expect(views[0].viewId).toBe('board')
    expect(views[0].label).toBe('Demo Board')
  })

  it('round-trips extension main view keys', () => {
    const key = buildExtensionMainViewKey('agent-teams', 'team-card-board', 'teams')

    expect(parseExtensionMainViewKey(key)).toEqual({
      pluginId: 'agent-teams',
      extensionId: 'team-card-board',
      viewId: 'teams'
    })
  })
})

function plugin(id: string, installed: boolean, enabled: boolean): PluginEntry {
  return {
    id,
    displayName: id,
    enabled,
    installed,
    installable: !installed,
    removable: false,
    source: installed ? 'workspace' : 'builtIn',
    rootPath: installed ? `Z:/__dotcraft_fixture__/workspace/.craft/plugins/${id}` : '',
    functions: [],
    skills: [],
    apps: [],
    desktopExtensions: [
      {
        id: 'board',
        displayName: 'Board',
        entry: 'Z:/__dotcraft_fixture__/workspace/.craft/plugins/demo/desktop/board.mjs',
        styles: [],
        permissions: [],
        requiredAppIds: [],
        connectOrigins: [],
        surfaces: [
          {
            type: 'mainView',
            viewId: 'board',
            label: 'Demo Board',
            order: 10
          }
        ]
      }
    ],
    mcpServers: [],
    lspServers: []
  }
}
