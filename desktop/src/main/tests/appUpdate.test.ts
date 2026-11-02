import { EventEmitter } from 'events'
import { describe, expect, it, vi } from 'vitest'
import type { AppUpdateServiceOptions } from '../appUpdate'

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

function createService(updater: FakeUpdater | null, options: AppUpdateServiceOptions = {}) {
  const states: AppUpdateState[] = []
  const service = new AppUpdateService({
    currentVersion: '0.7.3',
    createUpdater: () => updater,
    onStateChanged: (state) => states.push(state),
    ...options
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
    updater.emit('download-progress', { transferred: 50, total: 100, percent: 50, bytesPerSecond: 25 })
    expect(service.getState().progress).toEqual({ transferredBytes: 50, totalBytes: 100, percent: 50, bytesPerSecond: 25 })
    await service.install()
    expect(updater.quitAndInstall).not.toHaveBeenCalled()

    updater.emit('update-downloaded', { version: '0.7.4', releaseNotes: '<p>Notes</p>' })
    await service.install()

    expect(states.map((state) => state.status)).toEqual(['checking', 'available', 'downloading', 'downloaded', 'preparing'])
    expect(updater.quitAndInstall).toHaveBeenCalledWith(true, true)
  })

  it('waits for preparation, coalesces installation and ignores late update checks', async () => {
    const updater = new FakeUpdater()
    let prepared!: () => void
    const beforeInstall = vi.fn(() => new Promise<void>((resolve) => { prepared = resolve }))
    const watchdog = { beginQuit: vi.fn(), cancel: vi.fn() }
    const { service } = createService(updater, { beforeInstall, createExitWatchdog: () => watchdog })
    updater.emit('update-downloaded', { version: '0.7.4' })

    const first = service.install()
    expect(service.install()).toBe(first)
    await Promise.resolve()
    expect(service.getState().status).toBe('preparing')
    expect(beforeInstall).toHaveBeenCalledOnce()
    expect(updater.quitAndInstall).not.toHaveBeenCalled()
    expect(watchdog.beginQuit).not.toHaveBeenCalled()
    updater.emit('update-not-available')
    await service.download()
    await service.checkForUpdates()
    expect(service.getState().status).toBe('preparing')
    expect(updater.downloadUpdate).not.toHaveBeenCalled()
    expect(updater.checkForUpdates).not.toHaveBeenCalled()

    prepared()
    await first
    expect(watchdog.beginQuit).toHaveBeenCalledOnce()
    expect(updater.quitAndInstall).toHaveBeenCalledOnce()
    await service.install()
    expect(updater.quitAndInstall).toHaveBeenCalledOnce()
  })

  it('keeps the downloaded update after preparation failure and retries installation without downloading', async () => {
    const updater = new FakeUpdater()
    const failure = new Error('A managed process is still running.')
    const beforeInstall = vi.fn().mockRejectedValueOnce(failure).mockResolvedValue(undefined)
    let releasePreparation!: () => void
    const onInstallFailed = vi.fn(() => new Promise<void>((resolve) => { releasePreparation = resolve }))
    const watchdog = { beginQuit: vi.fn(), cancel: vi.fn() }
    const { service } = createService(updater, { beforeInstall, onInstallFailed, createExitWatchdog: () => watchdog })
    updater.emit('update-downloaded', { version: '0.7.4', releaseNotes: '<p>Ready</p>' })

    const installing = service.install()
    await vi.waitFor(() => expect(onInstallFailed).toHaveBeenCalledOnce())
    expect(service.getState().status).toBe('preparing')
    expect(service.install()).toBe(installing)
    expect(updater.quitAndInstall).not.toHaveBeenCalled()
    releasePreparation()
    await installing
    expect(service.getState()).toMatchObject({
      status: 'error', error: failure.message, retryAction: 'install', update: { latestVersion: '0.7.4' }
    })
    expect(onInstallFailed).toHaveBeenCalledWith(failure)
    expect(updater.quitAndInstall).not.toHaveBeenCalled()
    expect(watchdog.beginQuit).not.toHaveBeenCalled()

    await service.install()
    expect(beforeInstall).toHaveBeenCalledTimes(2)
    expect(updater.quitAndInstall).toHaveBeenCalledOnce()
    expect(updater.downloadUpdate).not.toHaveBeenCalled()
    expect(service.getState()).toMatchObject({ status: 'preparing', update: { latestVersion: '0.7.4' } })
  })

  it('cancels update exit on synchronous and asynchronous installer-launch failures', async () => {
    for (const asynchronous of [false, true]) {
      const updater = new FakeUpdater()
      const watchdog = { beginQuit: vi.fn(), cancel: vi.fn() }
      const onInstallFailed = vi.fn()
      const { service } = createService(updater, { onInstallFailed, createExitWatchdog: () => watchdog })
      updater.emit('update-downloaded', { version: '0.7.4' })
      const failure = new Error('Installer launch failed.')
      let rejectLaunch!: (error: Error) => void
      updater.quitAndInstall.mockImplementationOnce(() => {
        if (!asynchronous) throw failure
        return new Promise<void>((_resolve, reject) => { rejectLaunch = reject })
      })

      const installing = service.install()
      if (asynchronous) {
        await vi.waitFor(() => expect(updater.quitAndInstall).toHaveBeenCalledOnce())
        updater.emit('error', failure)
        rejectLaunch(failure)
      }
      await installing
      await vi.waitFor(() => expect(service.getState().status).toBe('error'))
      expect(service.getState().retryAction).toBe('install')
      expect(watchdog.cancel).toHaveBeenCalledOnce()
      expect(watchdog.beginQuit).not.toHaveBeenCalled()
      expect(onInstallFailed).toHaveBeenCalledExactlyOnceWith(failure)

      await service.install()
      expect(updater.quitAndInstall).toHaveBeenCalledTimes(2)
    }
  })

  it.each(['resolves', 'rejects'])('ignores stale preparation that %s after a newer installation is accepted', async (completion) => {
    const updater = new FakeUpdater()
    let resolveStale!: () => void
    let rejectStale!: (error: Error) => void
    const beforeInstall = vi.fn().mockImplementationOnce(() => new Promise<void>((resolve, reject) => {
      resolveStale = resolve
      rejectStale = reject
    })).mockResolvedValue(undefined)
    const onInstallFailed = vi.fn()
    const watchdog = { beginQuit: vi.fn(), cancel: vi.fn() }
    const { service } = createService(updater, { beforeInstall, onInstallFailed, createExitWatchdog: () => watchdog })
    updater.emit('update-downloaded', { version: '0.7.4' })

    const stale = service.install()
    await vi.waitFor(() => expect(beforeInstall).toHaveBeenCalledOnce())
    updater.emit('error', new Error('First preparation failed.'))
    await vi.waitFor(() => expect(service.getState().status).toBe('error'))
    await service.install()
    expect(watchdog.beginQuit).toHaveBeenCalledOnce()

    if (completion === 'resolves') resolveStale()
    else rejectStale(new Error('Late preparation failure.'))
    await stale
    expect(service.getState().status).toBe('preparing')
    expect(updater.quitAndInstall).toHaveBeenCalledOnce()
    expect(onInstallFailed).toHaveBeenCalledOnce()
    expect(watchdog.beginQuit).toHaveBeenCalledOnce()
  })

  it('does not check again while an update is downloading or ready, and reports unsupported without an updater', async () => {
    const updater = new FakeUpdater()
    const { service } = createService(updater)
    updater.emit('update-available', { version: '0.7.4' })
    updater.emit('download-progress', { transferred: 1, total: 2, percent: 50, bytesPerSecond: 1 })
    await service.checkForUpdates()
    updater.emit('update-downloaded', { version: '0.7.4' })
    await service.checkForUpdates()

    expect(updater.checkForUpdates).not.toHaveBeenCalled()

    const { service: unsupported } = createService(null)

    expect((await unsupported.checkForUpdates()).status).toBe('unsupported')
  })

})
