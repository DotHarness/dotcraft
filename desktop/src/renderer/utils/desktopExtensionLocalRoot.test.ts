import { describe, expect, it } from 'vitest'

import type { DesktopMainViewExtension } from './desktopExtensionRegistry'
import { remapDesktopExtensionToLocalRoot } from './desktopExtensionLocalRoot'

function remoteEntry(): DesktopMainViewExtension {
  return {
    plugin: {
      id: 'sample-plugin',
      displayName: 'Sample Plugin',
      rootPath: '/test-fixtures/remote/sample-plugin'
    },
    extension: {
      id: 'sample-board',
      displayName: 'Sample Board',
      entry: '/test-fixtures/remote/sample-plugin/desktop/board.mjs',
      styles: ['/test-fixtures/remote/sample-plugin/desktop/board.css']
    }
  } as DesktopMainViewExtension
}

describe('remote Desktop extension local root mapping', () => {
  it('maps remote entry and style paths into the authorized local bundled root', () => {
    const entry = remapDesktopExtensionToLocalRoot(
      remoteEntry(),
      '/test-fixtures/local/sample-plugin'
    )

    expect(entry.plugin.rootPath).toBe('/test-fixtures/local/sample-plugin')
    expect(entry.extension.entry).toBe(
      '/test-fixtures/local/sample-plugin/desktop/board.mjs'
    )
    expect(entry.extension.styles).toEqual([
      '/test-fixtures/local/sample-plugin/desktop/board.css'
    ])
  })

  it('rejects extension paths outside the reported plugin root', () => {
    const entry = remoteEntry()
    entry.extension.entry = '/test-fixtures/remote/other-plugin/desktop/other.mjs'

    expect(() => remapDesktopExtensionToLocalRoot(entry, '/test-fixtures/local/sample-plugin'))
      .toThrow('must stay inside')
  })
})
