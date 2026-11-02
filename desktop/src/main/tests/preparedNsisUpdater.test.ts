import { EventEmitter } from 'events'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { DownloadedUpdateHelper } from 'electron-updater/out/DownloadedUpdateHelper'
import type { AppAdapter } from 'electron-updater/out/AppAdapter'

const mocks = vi.hoisted(() => ({ spawn: vi.fn(), openPath: vi.fn() }))
vi.mock('child_process', async (importOriginal) => ({
  ...await importOriginal<typeof import('child_process')>(), spawn: mocks.spawn
}))
vi.mock('electron', async () => {
  const { EventEmitter } = await import('events')
  return { autoUpdater: new EventEmitter(), shell: { openPath: mocks.openPath } }
})

const { autoUpdater } = await import('electron')
const { PreparedNsisUpdater } = await import('../preparedNsisUpdater')
const { AppUpdateService } = await import('../appUpdate')

class StagedUpdater extends PreparedNsisUpdater {
  async stage(): Promise<void> {
    this.downloadedUpdateHelper = new DownloadedUpdateHelper('X:/fixtures/cache')
    await this.downloadedUpdateHelper.setDownloadedFile(
      'X:/fixtures/Setup.exe', null, { version: '0.9.11', files: [], path: 'Setup.exe', sha512: 'fixture' },
      { url: new URL('https://example.test/Setup.exe'), info: { url: 'Setup.exe', sha512: 'fixture' } },
      'Setup.exe', false
    )
  }
}

let children: Array<EventEmitter & { unref: ReturnType<typeof vi.fn>; kill: ReturnType<typeof vi.fn> }>
const originalResourcesPath = Object.getOwnPropertyDescriptor(process, 'resourcesPath')

beforeEach(() => {
  children = []
  mocks.spawn.mockReset().mockImplementation(() => {
    const child = Object.assign(new EventEmitter(), { unref: vi.fn(), kill: vi.fn() })
    children.push(child)
    return child
  })
  mocks.openPath.mockReset()
  autoUpdater.removeAllListeners()
  Object.defineProperty(process, 'resourcesPath', { configurable: true, value: 'X:/fixtures/resources' })
})

afterEach(() => {
  vi.useRealTimers()
  if (originalResourcesPath) Object.defineProperty(process, 'resourcesPath', originalResourcesPath)
  else Reflect.deleteProperty(process, 'resourcesPath')
})

async function fixture() {
  const quit = vi.fn()
  const adapter: AppAdapter = {
    version: '0.9.10', name: 'DotCraft', isPackaged: true,
    appUpdateConfigPath: 'X:/fixtures/app-update.yml', userDataPath: 'X:/fixtures/user', baseCachePath: 'X:/fixtures/cache',
    whenReady: async () => {}, relaunch: vi.fn(), quit, onQuit: vi.fn()
  }
  const updater = new StagedUpdater(null, adapter)
  updater.autoInstallOnAppQuit = false
  updater.logger = { info: vi.fn(), warn: vi.fn(), error: vi.fn() }
  await updater.stage()
  const updateQuit = vi.fn()
  autoUpdater.on('before-quit-for-update', updateQuit)
  return { updater, quit, updateQuit }
}

describe('prepared NSIS update launch', () => {
  it('waits for actual spawn acknowledgement before emitting update quit and quitting once', async () => {
    const { updater, quit, updateQuit } = await fixture()
    const order: string[] = []
    updateQuit.mockImplementation(() => order.push('update-quit'))
    quit.mockImplementation(() => order.push('quit'))
    const first = updater.quitAndInstall(true, true)
    void first.then(() => order.push('accepted'))
    expect(quit).not.toHaveBeenCalled()
    expect(updateQuit).not.toHaveBeenCalled()
    expect(mocks.spawn.mock.calls[0][1]).toEqual(['--updated', '/S', '--force-run'])
    children[0].emit('spawn')
    await first
    expect(order).toEqual(['accepted'])
    expect(updateQuit).not.toHaveBeenCalled()
    expect(quit).not.toHaveBeenCalled()
    await vi.waitFor(() => expect(quit).toHaveBeenCalledOnce())
    expect(order).toEqual(['accepted', 'update-quit', 'quit'])
  })

  it('retains the caller on missing installer and allows a launch retry without shell fallback', async () => {
    const { updater, quit, updateQuit } = await fixture()
    const first = updater.quitAndInstall(true, true)
    children[0].emit('error', Object.assign(new Error('Installer not found.'), { code: 'ENOENT' }))
    await expect(first).rejects.toThrow('Installer not found.')
    expect(quit).not.toHaveBeenCalled()
    expect(updateQuit).not.toHaveBeenCalled()
    expect(mocks.openPath).not.toHaveBeenCalled()

    const retry = updater.quitAndInstall(true, true)
    children[1].emit('spawn')
    await retry
    await vi.waitFor(() => expect(quit).toHaveBeenCalledOnce())
  })

  it('preserves elevation retry and waits for the elevation helper to spawn', async () => {
    const { updater, quit, updateQuit } = await fixture()
    const pending = updater.quitAndInstall(true, true)
    children[0].emit('error', Object.assign(new Error('Access denied.'), { code: 'EACCES' }))
    await vi.waitFor(() => expect(children).toHaveLength(2))
    expect(mocks.spawn.mock.calls[1][0]).toMatch(/elevate\.exe$/)
    expect(quit).not.toHaveBeenCalled()
    expect(updateQuit).not.toHaveBeenCalled()
    children[1].emit('spawn')
    await pending
    await vi.waitFor(() => expect(quit).toHaveBeenCalledOnce())
  })

  it('does not quit when elevation also fails', async () => {
    const { updater, quit, updateQuit } = await fixture()
    const pending = updater.quitAndInstall(true, true)
    children[0].emit('error', Object.assign(new Error('Access denied.'), { code: 'EACCES' }))
    await vi.waitFor(() => expect(children).toHaveLength(2))
    children[1].emit('error', new Error('Elevation rejected.'))
    await expect(pending).rejects.toThrow('Elevation rejected.')
    expect(quit).not.toHaveBeenCalled()
    expect(updateQuit).not.toHaveBeenCalled()
  })

  it.each(['quit', 'update-event'] as const)('keeps an accepted installer running when %s throws', async (failure) => {
    const { updater, quit } = await fixture()
    if (failure === 'quit') quit.mockImplementationOnce(() => { throw new Error('Quit callback failed.') })
    else autoUpdater.on('before-quit-for-update', () => { throw new Error('Listener failed.') })
    const pending = updater.quitAndInstall(true, true)
    children[0].emit('spawn')
    await pending
    await vi.waitFor(() => expect(quit).toHaveBeenCalledOnce())
    expect(children[0].kill).not.toHaveBeenCalled()
    expect(mocks.spawn).toHaveBeenCalledOnce()
  })

  it('lets the service retain accepted state before update-quit listeners synchronously emit updater errors', async () => {
    const { updater, quit, updateQuit } = await fixture()
    const watchdog = { beginQuit: vi.fn(), cancel: vi.fn() }
    const onInstallFailed = vi.fn()
    const service = new AppUpdateService({
      currentVersion: '0.9.10', createUpdater: () => updater,
      createExitWatchdog: () => watchdog, onInstallFailed
    })
    updater.emit('update-downloaded', { version: '0.9.11' })
    autoUpdater.on('before-quit-for-update', () => {
      expect(watchdog.beginQuit).toHaveBeenCalledOnce()
      updater.emit('error', new Error('Exit listener failed.'))
    })
    const pending = service.install()
    expect(service.install()).toBe(pending)
    await vi.waitFor(() => expect(children).toHaveLength(1))
    children[0].emit('spawn')
    await pending
    expect(updateQuit).not.toHaveBeenCalled()
    await vi.waitFor(() => expect(quit).toHaveBeenCalledOnce())
    expect(service.getState().status).toBe('preparing')
    expect(watchdog.cancel).not.toHaveBeenCalled()
    expect(onInstallFailed).not.toHaveBeenCalled()
    await service.install()
    expect(mocks.spawn).toHaveBeenCalledOnce()
  })

  it('bounds a spawn that never acknowledges startup without beginning update quit', async () => {
    vi.useFakeTimers()
    const { updater, quit, updateQuit } = await fixture()
    const pending = updater.quitAndInstall(true, true)
    const rejected = expect(pending).rejects.toThrow('did not start')
    await vi.advanceTimersByTimeAsync(10_000)
    await rejected
    expect(children[0].kill).toHaveBeenCalledOnce()
    expect(quit).not.toHaveBeenCalled()
    expect(updateQuit).not.toHaveBeenCalled()
  })
})
