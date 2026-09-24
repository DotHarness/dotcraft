import { EventEmitter } from 'events'
import { describe, expect, it, vi } from 'vitest'

import type { AppUpdateState } from '../../shared/appUpdate'

vi.mock('electron', () => ({ app: { getVersion: () => '0.7.3', isPackaged: false } }))
vi.mock('electron-updater', () => ({ NsisUpdater: class {} }))

const { AppUpdateService } = await import('../appUpdate')

class FakeUpdater extends EventEmitter {
  autoDownload = false
  autoInstallOnAppQuit = true
  checkForUpdates = vi.fn(async () => {
    this.emit('checking-for-update')
    return null
  })
  downloadUpdate = vi.fn(async () => [])
  quitAndInstall = vi.fn()
}

function createService(updater: FakeUpdater | null) {
  const states: AppUpdateState[] = []
  const service = new AppUpdateService({
    currentVersion: '0.7.3',
    createUpdater: () => updater as never,
    onStateChanged: (state) => states.push(state)
  })
  return { service, states }
}

describe('AppUpdateService', () => {
  it('downloads a new version in the background and installs it silently only once it is ready', async () => {
    const updater = new FakeUpdater()
    const { service, states } = createService(updater)
    expect(updater.autoDownload).toBe(true)
    expect(updater.autoInstallOnAppQuit).toBe(false)

    await service.checkForUpdates()
    updater.emit('update-available', { version: '0.7.4', releaseNotes: '<p>Notes</p>' })
    updater.emit('download-progress', { transferred: 50, total: 100, percent: 50 })
    service.install()
    expect(updater.quitAndInstall).not.toHaveBeenCalled()

    updater.emit('update-downloaded', { version: '0.7.4', releaseNotes: '<p>Notes</p>' })
    service.install()

    expect(states.map((state) => state.status)).toEqual(['checking', 'available', 'downloading', 'downloaded'])
    expect(service.getState().update).toEqual({
      latestVersion: '0.7.4',
      releaseNotes: '<p>Notes</p>',
      htmlUrl: 'https://github.com/DotHarness/dotcraft/releases/tag/v0.7.4'
    })
    expect(updater.quitAndInstall).toHaveBeenCalledWith(true, true)
  })

  it('does not check again while an update is downloading or ready', async () => {
    const updater = new FakeUpdater()
    const { service } = createService(updater)
    updater.emit('update-available', { version: '0.7.4' })
    updater.emit('download-progress', { transferred: 1, total: 2, percent: 50 })
    await service.checkForUpdates()
    updater.emit('update-downloaded', { version: '0.7.4' })
    await service.checkForUpdates()

    expect(updater.checkForUpdates).not.toHaveBeenCalled()
  })

  it('reports updates as unsupported without an updater', async () => {
    const { service } = createService(null)

    expect((await service.checkForUpdates()).status).toBe('unsupported')
  })
})
