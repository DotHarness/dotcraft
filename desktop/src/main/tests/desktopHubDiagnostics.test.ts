import { describe, expect, it, vi } from 'vitest'
import { HubClient } from '@dotcraft/sdk/hub'
import { DesktopHubClient, DesktopHubError } from '../desktopHub'
import { logDesktopHubFailure, logLiveDesktopHub } from '../desktopHubDiagnostics'

describe('Desktop Hub diagnostics', () => {
  it('observes the actual live build without ensuring a Hub or exposing its bearer', async () => {
    const inner = new HubClient()
    vi.spyOn(inner, 'tryGetLiveHub').mockResolvedValue({
      pid: 42, version: '0.9.10+actual', binaryPath: 'C:/DotCraft/resources/bin/dotcraft.exe',
      startedAt: '2026-11-02T10:00:00Z', apiBaseUrl: 'http://127.0.0.1:9999', token: 'secret-bearer'
    })
    const ensure = vi.spyOn(inner, 'ensureHub')
    const info = vi.spyOn(console, 'info').mockImplementation(() => {})
    try {
      logLiveDesktopHub(new DesktopHubClient(inner))
      await vi.waitFor(() => expect(info).toHaveBeenCalled())
      expect(info.mock.calls[0][1]).toMatchObject({ pid: 42, hubVersion: '0.9.10+actual' })
      expect(JSON.stringify(info.mock.calls)).not.toContain('secret-bearer')
      expect(ensure).not.toHaveBeenCalled()
    } finally { info.mockRestore() }
  })

  it('logs safe error context without copying discovery credentials or authenticated URLs', () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    try {
      logDesktopHubFailure('prewarm', new DesktopHubError('hubUnavailable', 'Failed https://name:password@host/status?token=secret Bearer bearer-value', {
        stage: 'ready', elapsedMs: 15_000, token: 'discovery-secret',
        lastProbe: { kind: 'timeout', token: 'probe-secret', status: 503 }
      }), Date.now(), '/work')
      const payload = warn.mock.calls[0][1]
      expect(payload).toMatchObject({ code: 'hubUnavailable', stage: 'prewarm', details: { stage: 'ready', lastProbe: { kind: 'timeout', status: 503 } } })
      const serialized = JSON.stringify(payload)
      for (const secret of ['password', '?token', 'bearer-value', 'discovery-secret', 'probe-secret']) expect(serialized).not.toContain(secret)
    } finally { warn.mockRestore() }
  })
})
