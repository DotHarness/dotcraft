import { promises as fs, watch, type FSWatcher } from 'fs'
import * as path from 'path'

const DIR_POLL_INTERVAL_MS = 500
const DIR_POLL_MAX_ATTEMPTS = 60
const QR_READ_DEBOUNCE_MS = 200

export type QrWatchPhase = 'idle' | 'waitingForDir' | 'watching' | 'loginComplete'

export interface QrUpdatePayload {
  moduleId: string
  qrDataUrl: string | null
  timestamp: number
}

interface QrWatchState {
  moduleId: string
  phase: QrWatchPhase
  lastQrDataUrl: string | null
  watcher: FSWatcher | null
  dirPollTimer: ReturnType<typeof setInterval> | null
  readDebounceTimer: ReturnType<typeof setTimeout> | null
}

function moduleQrDir(workspacePath: string, moduleId: string): string {
  return path.join(workspacePath, '.craft', 'tmp', moduleId)
}

function moduleQrPath(workspacePath: string, moduleId: string): string {
  return path.join(moduleQrDir(workspacePath, moduleId), 'qr.png')
}

export class QrFileWatcher {
  private readonly workspacePath: string
  private readonly onQrUpdate: (payload: QrUpdatePayload) => void
  private readonly states = new Map<string, QrWatchState>()

  constructor(options: {
    workspacePath: string
    onQrUpdate: (payload: QrUpdatePayload) => void
  }) {
    this.workspacePath = options.workspacePath
    this.onQrUpdate = options.onQrUpdate
  }

  async startWatching(moduleId: string): Promise<void> {
    const state = this.getOrCreateState(moduleId)
    if (state.phase === 'watching' || state.phase === 'loginComplete') {
      return
    }

    state.phase = 'waitingForDir'
    const dirPath = moduleQrDir(this.workspacePath, moduleId)
    const dirReady = await this.pathExists(dirPath)
    if (dirReady) {
      this.attachFsWatcher(state)
      await this.readAndBroadcastQr(state)
      return
    }

    this.startDirPolling(state)
  }

  stopWatching(moduleId: string): void {
    const state = this.states.get(moduleId)
    if (!state) return
    this.cleanupState(state, true)
  }

  onChannelConnected(moduleId: string): void {
    const state = this.getOrCreateState(moduleId)
    state.phase = 'loginComplete'
    this.publishQr(state, null)
  }

  onChannelDisconnected(moduleId: string): void {
    const state = this.getOrCreateState(moduleId)
    if (state.watcher) {
      state.phase = 'watching'
      void this.readAndBroadcastQr(state)
      return
    }
    void this.startWatching(moduleId)
  }

  getStatus(moduleId: string): { active: boolean; qrDataUrl: string | null } {
    const state = this.states.get(moduleId)
    if (!state) {
      return { active: false, qrDataUrl: null }
    }
    return {
      active: state.phase !== 'idle',
      qrDataUrl: state.lastQrDataUrl
    }
  }

  private getOrCreateState(moduleId: string): QrWatchState {
    const existing = this.states.get(moduleId)
    if (existing) return existing
    const created: QrWatchState = {
      moduleId,
      phase: 'idle',
      lastQrDataUrl: null,
      watcher: null,
      dirPollTimer: null,
      readDebounceTimer: null
    }
    this.states.set(moduleId, created)
    return created
  }

  private startDirPolling(state: QrWatchState): void {
    if (state.dirPollTimer) return
    let attempts = 0
    state.dirPollTimer = setInterval(() => {
      attempts += 1
      if (attempts > DIR_POLL_MAX_ATTEMPTS) {
        console.warn(
          `[module:${state.moduleId}] QR directory did not appear within ${DIR_POLL_MAX_ATTEMPTS * DIR_POLL_INTERVAL_MS}ms`
        )
        this.cleanupState(state, true)
        return
      }
      const dirPath = moduleQrDir(this.workspacePath, state.moduleId)
      void this.pathExists(dirPath).then((exists) => {
        if (!exists) return
        if (state.dirPollTimer) {
          clearInterval(state.dirPollTimer)
          state.dirPollTimer = null
        }
        this.attachFsWatcher(state)
        void this.readAndBroadcastQr(state)
      })
    }, DIR_POLL_INTERVAL_MS)
  }

  private attachFsWatcher(state: QrWatchState): void {
    if (state.watcher) return
    const dirPath = moduleQrDir(this.workspacePath, state.moduleId)
    try {
      state.watcher = watch(dirPath, (_eventType, fileName) => {
        if (fileName && fileName.toString() !== 'qr.png') return
        this.scheduleQrRead(state)
      })
      if (state.phase !== 'loginComplete') {
        state.phase = 'watching'
      }
      state.watcher.on('error', (error) => {
        console.warn(`[module:${state.moduleId}] QR watcher error`, error)
      })
    } catch (error) {
      console.warn(`[module:${state.moduleId}] failed to watch QR directory`, error)
    }
  }

  private scheduleQrRead(state: QrWatchState): void {
    if (state.readDebounceTimer) {
      clearTimeout(state.readDebounceTimer)
    }
    state.readDebounceTimer = setTimeout(() => {
      state.readDebounceTimer = null
      void this.readAndBroadcastQr(state)
    }, QR_READ_DEBOUNCE_MS)
  }

  private async readAndBroadcastQr(state: QrWatchState): Promise<void> {
    const qrPath = moduleQrPath(this.workspacePath, state.moduleId)
    try {
      const content = await fs.readFile(qrPath)
      if (content.length === 0) return
      const qrDataUrl = `data:image/png;base64,${content.toString('base64')}`
      this.publishQr(state, qrDataUrl)
    } catch (error) {
      const code = (error as NodeJS.ErrnoException | null)?.code
      if (code === 'ENOENT') return
      console.warn(`[module:${state.moduleId}] failed to read QR image`, error)
    }
  }

  private publishQr(state: QrWatchState, qrDataUrl: string | null): void {
    if (state.lastQrDataUrl === qrDataUrl) return
    state.lastQrDataUrl = qrDataUrl
    this.onQrUpdate({
      moduleId: state.moduleId,
      qrDataUrl,
      timestamp: Date.now()
    })
  }

  private cleanupState(state: QrWatchState, toIdle: boolean): void {
    if (state.watcher) {
      state.watcher.close()
      state.watcher = null
    }
    if (state.dirPollTimer) {
      clearInterval(state.dirPollTimer)
      state.dirPollTimer = null
    }
    if (state.readDebounceTimer) {
      clearTimeout(state.readDebounceTimer)
      state.readDebounceTimer = null
    }
    if (toIdle) {
      state.phase = 'idle'
      state.lastQrDataUrl = null
    }
  }

  private async pathExists(targetPath: string): Promise<boolean> {
    try {
      await fs.access(targetPath)
      return true
    } catch {
      return false
    }
  }
}
