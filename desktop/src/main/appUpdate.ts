import { app } from 'electron'
import type { NsisUpdater, ProgressInfo, UpdateInfo } from 'electron-updater'

import type { AppUpdateInfo, AppUpdateState } from '../shared/appUpdate'
import { createAppUpdateExitWatchdog, type AppUpdateExitWatchdog } from './appUpdateExit'
import { PreparedNsisUpdater } from './preparedNsisUpdater'

const CHECK_INTERVAL_MS = 15 * 60_000
const RELEASE_TAG_URL = 'https://github.com/DotHarness/dotcraft/releases/tag/v'

export type AppUpdater = Pick<
  NsisUpdater,
  'autoDownload' | 'autoInstallOnAppQuit' | 'checkForUpdates' | 'downloadUpdate' | 'on'
> & { quitAndInstall(isSilent?: boolean, isForceRunAfter?: boolean): void | Promise<void> }

export interface AppUpdateServiceOptions {
  currentVersion?: string
  createUpdater?: () => AppUpdater | null
  beforeInstall?: () => Promise<void>
  onInstallFailed?: (error: Error) => Promise<void> | void
  createExitWatchdog?: () => AppUpdateExitWatchdog
  onStateChanged?: (state: AppUpdateState) => void
}

interface InstallAttempt {
  promise: Promise<void>
  failure: Promise<void> | null
  accepted: boolean
}

export class AppUpdateService {
  private readonly updater: AppUpdater | null
  private state: AppUpdateState
  private timer: NodeJS.Timeout | null = null
  private downloadedUpdate: AppUpdateInfo | null = null
  private installAttempt: InstallAttempt | null = null
  private exitWatchdog: AppUpdateExitWatchdog | null = null

  constructor(private readonly options: AppUpdateServiceOptions = {}) {
    const currentVersion = options.currentVersion ?? app.getVersion()
    this.updater = (options.createUpdater ?? createPackagedWindowsUpdater)()
    this.state = { status: this.updater ? 'idle' : 'unsupported', currentVersion }
    if (this.updater) this.observe(this.updater)
  }

  getState(): AppUpdateState {
    return cloneUpdateState(this.state)
  }

  start(): void {
    if (!this.updater || this.timer) return
    void this.checkForUpdates()
    this.timer = setInterval(() => void this.checkForUpdates(), CHECK_INTERVAL_MS)
    this.timer.unref()
  }

  async checkForUpdates(): Promise<AppUpdateState> {
    if (!this.updater || this.isBusy()) return this.getState()
    await this.updater.checkForUpdates().then(
      (result) => { void result?.downloadPromise?.catch(() => {}) },
      () => {}
    )
    return this.getState()
  }

  async download(): Promise<AppUpdateState> {
    if (!this.updater || this.isBusy()) return this.getState()
    await this.updater.downloadUpdate().catch(() => {})
    return this.getState()
  }

  install(): Promise<void> {
    if (this.installAttempt) return this.installAttempt.promise
    if (!this.updater || !this.downloadedUpdate) return Promise.resolve()
    const attempt: InstallAttempt = {
      failure: null,
      accepted: false,
      promise: Promise.resolve().then(async () => {
        await this.options.beforeInstall?.()
        if (this.installAttempt !== attempt || attempt.failure) return
        this.exitWatchdog ??= this.options.createExitWatchdog?.()
          ?? (app.isPackaged ? createAppUpdateExitWatchdog({ app }) : null)
        console.info('[desktop-update] installing prepared update', { version: this.downloadedUpdate?.latestVersion })
        await this.updater!.quitAndInstall(true, true)
        if (this.installAttempt === attempt && !attempt.failure) {
          attempt.accepted = true
          this.exitWatchdog?.beginQuit()
        }
      }).catch((error: unknown) => this.failInstall(error, attempt))
    }
    this.installAttempt = attempt
    this.setState({ status: 'preparing', update: this.downloadedUpdate })
    return attempt.promise
  }

  private failInstall(error: unknown, attempt: InstallAttempt): Promise<void> {
    if (attempt.failure) return attempt.failure
    if (this.installAttempt !== attempt || attempt.accepted) return Promise.resolve()
    this.exitWatchdog?.cancel()
    const failure = error instanceof Error ? error : new Error(String(error))
    console.warn('[desktop-update] update preparation or launch failed', { errorName: failure.name })
    attempt.failure = Promise.resolve().then(() => this.options.onInstallFailed?.(failure)).catch(() => {
      console.warn('[desktop-update] failed to release update preparation')
    }).finally(() => {
      if (this.installAttempt !== attempt) return
      this.installAttempt = null
      this.setState({
        status: 'error',
        update: this.downloadedUpdate ?? this.state.update,
        error: failure.message,
        retryAction: 'install'
      })
    })
    return attempt.failure
  }

  private isBusy(): boolean {
    return this.installAttempt !== null || this.downloadedUpdate !== null || this.state.status === 'downloading'
  }

  private observe(updater: AppUpdater): void {
    updater.autoDownload = true
    updater.autoInstallOnAppQuit = false
    updater.on('checking-for-update', () => {
      if (this.isBusy()) return
      this.setState({ status: 'checking', update: this.state.update })
    })
    updater.on('update-not-available', () => {
      if (this.isBusy()) return
      this.setState({ status: 'not-available' })
    })
    updater.on('update-available', (info: UpdateInfo) => {
      if (this.isBusy()) return
      this.setState({ status: 'available', update: toUpdateInfo(info) })
    })
    updater.on('download-progress', (progress: ProgressInfo) => {
      if (this.installAttempt || this.downloadedUpdate) return
      this.setState({
        status: 'downloading',
        update: this.state.update,
        progress: {
          transferredBytes: progress.transferred,
          totalBytes: progress.total,
          percent: progress.percent,
          bytesPerSecond: progress.bytesPerSecond
        }
      })
    })
    updater.on('update-downloaded', (info: UpdateInfo) => {
      this.downloadedUpdate = toUpdateInfo(info)
      if (!this.installAttempt) this.setState({ status: 'downloaded', update: this.downloadedUpdate })
    })
    updater.on('error', (error: Error) => {
      if (this.installAttempt?.accepted) {
        console.warn('[desktop-update] updater error after installer acceptance', { errorName: error.name })
        return
      }
      if (this.installAttempt) {
        void this.failInstall(error, this.installAttempt)
        return
      }
      this.setState({
        status: 'error',
        update: this.state.update,
        error: error.message,
        retryAction: this.downloadedUpdate ? 'install' : 'download'
      })
    })
  }

  private setState(state: Omit<AppUpdateState, 'currentVersion'>): void {
    this.state = cloneUpdateState({ ...state, currentVersion: this.state.currentVersion })
    this.options.onStateChanged?.(this.getState())
  }
}

function createPackagedWindowsUpdater(): AppUpdater | null {
  return process.platform === 'win32' && app.isPackaged ? new PreparedNsisUpdater() : null
}

function toUpdateInfo(info: UpdateInfo): AppUpdateInfo {
  return {
    latestVersion: info.version,
    releaseNotes: typeof info.releaseNotes === 'string' && info.releaseNotes.trim()
      ? info.releaseNotes
      : undefined,
    htmlUrl: `${RELEASE_TAG_URL}${info.version}`
  }
}

function cloneUpdateState(state: AppUpdateState): AppUpdateState {
  return {
    ...state,
    update: state.update ? { ...state.update } : undefined,
    progress: state.progress ? { ...state.progress } : undefined
  }
}
