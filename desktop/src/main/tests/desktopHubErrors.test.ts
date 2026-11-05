import { describe, expect, it } from 'vitest'
import { DesktopHubError, desktopHubConnectionFailure } from '../desktopHubErrors'

describe('Hub connection failure boundary', () => {
  it('uses the binary error code rather than words in paths or server messages', () => {
    expect(desktopHubConnectionFailure(new DesktopHubError('hubUnavailable', 'binary not found in startup'), 'custom'))
      .toMatchObject({ errorType: 'hub-startup-failed' })
    expect(desktopHubConnectionFailure(new DesktopHubError('binary-not-found', 'Missing executable'), 'custom'))
      .toMatchObject({ errorType: 'binary-not-found', binarySource: 'custom' })
    expect(desktopHubConnectionFailure(new DesktopHubError('workspaceLocked', 'binary resource unavailable'), 'bundled').errorType)
      .toBeUndefined()
  })

  it('does not send credentials in diagnostics to the renderer', () => {
    const result = desktopHubConnectionFailure(new DesktopHubError('hubUnavailable', 'Hub failed.', {
      recentStderr: 'GET https://name:password@localhost:8000/status?token=secret Bearer bearer-value',
      lastProbe: { kind: 'timeout', token: 'probe-secret' }
    }), 'bundled')
    for (const secret of ['password', '?token', 'bearer-value', 'probe-secret']) {
      expect(result.errorDetails).not.toContain(secret)
    }
  })
})
