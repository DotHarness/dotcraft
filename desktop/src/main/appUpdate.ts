import { app } from 'electron'
import { NsisUpdater, type ProgressInfo, type UpdateInfo } from 'electron-updater'

import type { AppUpdateInfo, AppUpdateState } from '../shared/appUpdate'

const CHECK_INTERVAL_MS = 15 * 60_000
const RELEASE_TAG_URL = 'https://github.com/DotHarness/dotcraft/releases/tag/v'

export type AppUpdater = Pick<
  NsisUpdater,
  'autoDownload' | 'autoInstallOnAppQuit' | 'checkForUpdates' | 'downloadUpdate' | 'quitAndInstall' | 'on'
>

interface AppUpdateServiceOptions {
  currentVersion?: string
  createUpdater?: () => AppUpdater | null
  onStateChanged?: (state: AppUpdateState) => void
}

export class AppUpdateService {
  private readonly updater: AppUpdater | null
  private readonly onStateChanged?: (state: AppUpdateState) => void
  private state: AppUpdateState
  private timer: NodeJS.Timeout | null = null

  constructor(options: AppUpdateServiceOptions = {}) {
    const currentVersion = options.currentVersion ?? app.getVersion()
    this.onStateChanged = options.onStateChanged
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

  install(): void {
    if (this.state.status === 'downloaded') this.updater?.quitAndInstall(true, true)
  }

  private isBusy(): boolean {
    return this.state.status === 'downloading' || this.state.status === 'downloaded'
  }

  private observe(updater: AppUpdater): void {
    updater.autoDownload = true
    updater.autoInstallOnAppQuit = false
    updater.on('checking-for-update', () => {
      this.setState({ status: 'checking', update: this.state.update })
    })
    updater.on('update-not-available', () => {
      this.setState({ status: 'not-available' })
    })
    updater.on('update-available', (info: UpdateInfo) => {
      this.setState({ status: 'available', update: toUpdateInfo(info) })
    })
    updater.on('download-progress', (progress: ProgressInfo) => {
      this.setState({
        status: 'downloading',
        update: this.state.update,
        progress: {
          transferredBytes: progress.transferred,
          totalBytes: progress.total,
          percent: progress.percent
        }
      })
    })
    updater.on('update-downloaded', (info: UpdateInfo) => {
      this.setState({ status: 'downloaded', update: toUpdateInfo(info) })
    })
    updater.on('error', (error: Error) => {
      this.setState({ status: 'error', update: this.state.update, error: error.message })
    })
  }

  private setState(state: Omit<AppUpdateState, 'currentVersion'>): void {
    this.state = cloneUpdateState({ ...state, currentVersion: this.state.currentVersion })
    this.onStateChanged?.(this.getState())
  }
}

function createPackagedWindowsUpdater(): AppUpdater | null {
  return process.platform === 'win32' && app.isPackaged ? new NsisUpdater() : null
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
