import { spawn, type StdioOptions } from 'child_process'
import { autoUpdater as electronAutoUpdater } from 'electron'
import { NsisUpdater } from 'electron-updater'

interface InstallerLaunch {
  resolve(): void
  signal: AbortSignal
}

export class PreparedNsisUpdater extends NsisUpdater {
  private launch: InstallerLaunch | null = null

  override async quitAndInstall(isSilent = false, isForceRunAfter = false): Promise<void> {
    if (this.quitAndInstallCalled) return
    const controller = new AbortController()
    let resolve!: () => void
    let reject!: (error: Error) => void
    const launched = new Promise<void>((onResolve, onReject) => {
      resolve = onResolve
      reject = onReject
    })
    this.launch = { resolve, signal: controller.signal }
    const onError = (error: Error): void => reject(error)
    this.on('error', onError)
    const timeout = setTimeout(() => reject(new Error('Update installer did not start within 10 seconds.')), 10_000)
    timeout.unref()
    try {
      if (!this.install(isSilent, isSilent ? isForceRunAfter : this.autoRunAppAfterInstall)) {
        reject(new Error('Update installer could not be started.'))
      }
      await launched
    } catch (error) {
      controller.abort()
      this.quitAndInstallCalled = false
      throw error
    } finally {
      clearTimeout(timeout)
      this.removeListener('error', onError)
      this.launch = null
    }
    setImmediate(() => {
      try {
        electronAutoUpdater.emit('before-quit-for-update')
      } catch {
        this._logger.warn('Update quit listener failed after installer launch.')
      }
      try {
        this.app.quit()
      } catch {
        this._logger.warn('Application quit failed after installer launch; waiting for exit watchdog.')
      }
    })
  }

  protected override spawnLog(
    command: string,
    args: string[] = [],
    env?: NodeJS.ProcessEnv,
    stdio: StdioOptions = 'ignore'
  ): Promise<boolean> {
    return new Promise<boolean>((resolve, reject) => {
      const launch = this.launch
      const child = spawn(command, args, { env, stdio, detached: true })
      let cancelled = false
      const onAbort = (): void => {
        cancelled = true
        try { child.kill() } catch {}
        resolve(false)
      }
      const detachAbort = (): void => launch?.signal.removeEventListener('abort', onAbort)
      child.once('error', (error: NodeJS.ErrnoException) => {
        detachAbort()
        if (cancelled) {
          resolve(false)
          return
        }
        // NSIS otherwise falls back to shell.openPath, which has no observable spawn acknowledgement.
        if (error.code === 'ENOENT') {
          reject(Object.assign(new Error(error.message, { cause: error }), { code: 'ERR_UPDATER_INSTALLER_NOT_FOUND' }))
        } else {
          reject(error)
        }
      })
      child.once('spawn', () => {
        detachAbort()
        if (cancelled) {
          try { child.kill() } catch {}
          return
        }
        child.unref()
        launch?.resolve()
        resolve(true)
      })
      launch?.signal.addEventListener('abort', onAbort, { once: true })
      if (launch?.signal.aborted) onAbort()
    })
  }
}
