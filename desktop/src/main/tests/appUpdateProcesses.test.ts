import { describe, expect, it, vi } from 'vitest'
import { prepareAppUpdateProcesses } from '../appUpdateProcesses'

const executable = 'C:/App/resources/bin/dotcraft.exe'
const hub = {
  pid: 42,
  apiBaseUrl: 'http://127.0.0.1:1',
  token: 'private',
  binaryPath: executable,
  startedAt: '2026-11-05T01:00:00.000Z'
}

describe('single-main update process preparation', () => {
  it('delegates the captured bundled Hub identity to its lifecycle owner', async () => {
    const stopHub = vi.fn(async () => {})
    const readHub = vi.fn(() => hub)
    await prepareAppUpdateProcesses({ runtimeExecutable: executable, readHub, stopHub })
    expect(readHub).toHaveBeenCalledOnce()
    expect(stopHub).toHaveBeenCalledExactlyOnceWith(executable, hub)
  })

  it.each([undefined, null, { ...hub, binaryPath: 'D:/Other/dotcraft.exe' }])(
    'does not stop a custom, absent or unrelated Hub: %s',
    async (discovery) => {
      const stopHub = vi.fn(async () => {})
      await prepareAppUpdateProcesses({
        runtimeExecutable: discovery === undefined ? undefined : executable,
        readHub: () => discovery ?? null,
        stopHub
      })
      expect(stopHub).not.toHaveBeenCalled()
    }
  )
})
