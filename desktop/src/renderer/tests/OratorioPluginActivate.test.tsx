import './setupPluginRuntime'
import { describe, expect, it, vi } from 'vitest'

import { requestConductorGuide } from '../../bundled-plugins/oratorio/src/conductor'
import { activate } from '../../bundled-plugins/oratorio/src/index'
import { validateActivation } from '../plugins/desktopPluginValidation'
import { installDesktopApiMock } from './desktopApiMock'
import { installOratorioTestHost } from './oratorioPluginTestHost'

describe('Oratorio plugin activation', () => {
  it('requests the conductor guide only after three local projects were opened in the past week', async () => {
    installDesktopApiMock({})
    const host = installOratorioTestHost()
    const request = vi.spyOn(host.ui, 'requestSpaceGuide')
    const project = (path: string, daysAgo: number) => ({
      path, name: path, active: false, lastOpenedAt: new Date(Date.now() - daysAgo * 86_400_000).toISOString()
    })
    const projects = [project('one', 1), project('two', 6), project('three', 8)]
    vi.spyOn(host.workspaces, 'listLocalProjects').mockResolvedValue(projects)
    await requestConductorGuide(host)
    expect(request).not.toHaveBeenCalled()

    projects[2] = project('three', 0)
    await requestConductorGuide(host)
    expect(request).toHaveBeenCalledWith('conductor')

    vi.spyOn(host.threads, 'findOwned').mockReturnValue({ threadId: 'existing-conductor', workspacePath: 'one' })
    await requestConductorGuide(host)
    expect(request).toHaveBeenCalledTimes(1)
  })

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
        onEvent: vi.fn(() => vi.fn())
      }
    })
    const host = installOratorioTestHost()

    const activation = await activate(host)

    expect(getContext).toHaveBeenCalledTimes(1)
    expect(() => validateActivation('oratorio', '1.0.0', 'r1', host, activation)).not.toThrow()
    activation?.dispose?.()
  })
})
