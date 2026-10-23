import { describe, expect, it } from 'vitest'
import {
  createDesktopHubClient,
  resolveDesktopBinarySource
} from '../desktopHub'

describe('Desktop Hub policy adapter', () => {
  it('does not infer a binary source from an executable path', () => {
    expect(resolveDesktopBinarySource({ appServerBinaryPath: 'X:/fixtures/dotcraft.exe' })).toBe('bundled')
  })

  it('fails before Hub I/O when the configured custom executable is missing', async () => {
    const client = createDesktopHubClient({
      binarySource: 'custom',
      appServerBinaryPath: 'X:/fixtures/missing/dotcraft.exe'
    })
    await expect(client.ensureAppServer('X:/fixtures/workspace')).rejects.toMatchObject({
      code: 'binary-not-found'
    })
  })
})
