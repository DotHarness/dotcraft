import { describe, expect, it } from 'vitest'
import { installDesktopApiMock } from './desktopApiMock'

describe('installDesktopApiMock', () => {
  it('reports the full path of an unimplemented nested call', () => {
    const api = installDesktopApiMock({})
    expect(() => api.workspace.viewer.readText({ absolutePath: 'missing.txt' }))
      .toThrow('Unimplemented desktop API call: window.api.workspace.viewer.readText')
  })
})
