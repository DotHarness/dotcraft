import { describe, expect, it } from 'vitest'
import {
  createDesktopHubClient,
  formatDesktopHubError,
  resolveDesktopBinarySource
} from '../desktopHub'

describe('Desktop Hub policy adapter', () => {
  it('preserves structured Hub details for display', () => {
    expect(formatDesktopHubError('AppServer failed.', {
      workspacePath: 'X:/fixtures/workspace',
      stage: 'stdioInitialize',
      failureKind: 'processExited',
      exitCode: 17,
      recentStderr: 'access denied'
    })).toContain('workspacePath: X:/fixtures/workspace')
    expect(formatDesktopHubError('AppServer failed.', {
      stage: 'stdioInitialize',
      failureKind: 'processExited',
      exitCode: 17
    })).toContain('failureKind: processExited')
  })

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
