import './setupPluginRuntime'
import { describe, expect, it, vi } from 'vitest'

import { activate } from '../../bundled-plugins/oratorio/src/index'
import { installDesktopApiMock } from './desktopApiMock'
import { installOratorioTestHost } from './oratorioPluginTestHost'

describe('Oratorio plugin activation', () => {
  it('resolves the service context so the server starts without opening the board', async () => {
    const getContext = vi.fn().mockResolvedValue({
      provider: 'local',
      workspacePath: null,
      connected: true,
      revision: 1
    })
    installDesktopApiMock({
      settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }) },
      oratorio: {
        getContext,
        getPendingHandoff: vi.fn().mockResolvedValue(null),
        onEvent: vi.fn(() => vi.fn())
      }
    })
    const host = installOratorioTestHost()

    const activation = await activate(host)

    expect(getContext).toHaveBeenCalledTimes(1)
    activation?.dispose?.()
  })
})
