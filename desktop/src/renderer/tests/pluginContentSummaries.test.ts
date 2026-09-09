import { describe, expect, it } from 'vitest'
import type { PluginEntry } from '../stores/pluginStore'
import { getPluginContentSummaries } from '../utils/pluginContentSummaries'

const translate = (key: string): string => key

describe('getPluginContentSummaries', () => {
  it('falls back to plugin presentation metadata for an older .NET manifest', () => {
    const plugin = createPlugin({
      interface: {
        displayName: 'Legacy review',
        longDescription: 'Reviews changes without declared contribution copy.'
      }
    })

    expect(getPluginContentSummaries(plugin, translate)).toEqual([{
      key: 'dotnet',
      type: 'dotnet',
      kind: 'plugins.content.dotnet',
      title: 'Legacy review',
      description: 'Reviews changes without declared contribution copy.'
    }])
  })

  function createPlugin(overrides: Partial<PluginEntry> = {}): PluginEntry {
    return {
      id: 'legacy.review',
      displayName: 'Legacy Review',
      description: 'Reviews changes.',
      enabled: true,
      installed: true,
      installable: false,
      removable: true,
      source: 'workspace',
      rootPath: 'X:\\plugins\\legacy.review',
      functions: [],
      skills: [],
      mcpServers: [],
      lspServers: [],
      dotnet: {
        entryAssembly: './lib/Legacy.Review.dll',
        entryType: 'Legacy.Review.Plugin',
        exportedApiAssemblies: [],
        minHostVersion: '0.5.0'
      },
      ...overrides
    }
  }
})
