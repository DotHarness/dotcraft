import { DesktopHubError, type DesktopHubClient, type HubProject } from './desktopHub'
import { sameWorkspaceProjectKey } from '../shared/workspaceProjectKey'

const RESUBSCRIBE_DELAY_MS = 5_000
const SNAPSHOT_RETRY_DELAYS_MS = [2_000, 5_000, 15_000, 30_000]

export interface LocalProjectListOptions {
  isActive?: () => boolean
}

interface SnapshotRequest {
  rerun: boolean
  promise: Promise<boolean>
}

type ProjectHubClient = Pick<DesktopHubClient, 'listProjects' | 'openProject' | 'removeProject' | 'subscribeEvents'>

export class LocalProjectList {
  private projects: HubProject[] = []
  private watcher: AbortController | null = null
  private request: SnapshotRequest | null = null
  private retryTimer: ReturnType<typeof setTimeout> | null = null
  private retryAttempt = 0

  constructor(
    private readonly getHubClient: () => ProjectHubClient,
    private readonly onChanged: () => void,
    private readonly options: LocalProjectListOptions = {}
  ) {}

  list(): readonly HubProject[] {
    return this.projects
  }

  has(path: string): boolean {
    return this.projects.some((project) => sameWorkspaceProjectKey(project.path, path))
  }

  refresh(): Promise<boolean> {
    this.cancelRetry()
    if (!this.isActive()) return Promise.resolve(false)
    if (this.request) {
      this.request.rerun = true
      return this.request.promise
    }

    const request: SnapshotRequest = {
      rerun: false,
      promise: Promise.resolve(false)
    }
    this.request = request
    request.promise = this.refreshSnapshot(request).finally(() => {
      if (this.request === request) this.request = null
    })
    return request.promise
  }

  private async refreshSnapshot(request: SnapshotRequest): Promise<boolean> {
    for (;;) {
      request.rerun = false
      const next = await this.fetchProjects()
      if (this.request !== request || !this.isActive()) return false
      if (request.rerun) continue

      this.request = null
      if (next === null) {
        this.scheduleRetry()
        return false
      }
      this.cancelRetry()
      this.retryAttempt = 0
      this.projects = [...next].sort((left, right) => Date.parse(left.addedAt) - Date.parse(right.addedAt))
      this.onChanged()
      return true
    }
  }

  async open(path: string): Promise<void> {
    await this.getHubClient().openProject(path)
    await this.refresh()
  }

  async remove(path: string): Promise<void> {
    try {
      await this.getHubClient().removeProject(path)
    } catch (error) {
      if (!(error instanceof DesktopHubError && error.code === 'projectNotFound')) throw error
    }
    await this.refresh()
  }

  start(): void {
    if (this.watcher || !this.isActive()) return
    this.request = null
    const controller = new AbortController()
    this.watcher = controller
    void this.watch(controller.signal).finally(() => {
      if (this.watcher === controller) {
        this.watcher = null
        this.cancelRetry()
      }
    })
  }

  stop(): void {
    this.request = null
    this.cancelRetry()
    this.retryAttempt = 0
    this.watcher?.abort()
    this.watcher = null
  }

  private isActive(): boolean {
    return this.options.isActive?.() !== false
  }

  private isWatching(signal: AbortSignal): boolean {
    return !signal.aborted && this.watcher?.signal === signal && this.isActive()
  }

  private cancelRetry(): void {
    if (this.retryTimer !== null) clearTimeout(this.retryTimer)
    this.retryTimer = null
  }

  private scheduleRetry(): void {
    const signal = this.watcher?.signal
    if (!signal || !this.isWatching(signal)) return
    this.cancelRetry()
    const delayMs = SNAPSHOT_RETRY_DELAYS_MS[Math.min(this.retryAttempt, SNAPSHOT_RETRY_DELAYS_MS.length - 1)]
    this.retryAttempt += 1
    this.retryTimer = setTimeout(() => {
      this.retryTimer = null
      if (this.isWatching(signal)) void this.refresh()
    }, delayMs)
  }

  private async fetchProjects(): Promise<HubProject[] | null> {
    try {
      return await this.getHubClient().listProjects()
    } catch (error) {
      console.warn('[desktop] failed to list Hub projects', error)
      return null
    }
  }

  private async watch(signal: AbortSignal): Promise<void> {
    while (this.isWatching(signal)) {
      await this.refresh()
      if (!this.isWatching(signal)) return
      try {
        await this.getHubClient().subscribeEvents((event) => {
          if (this.isWatching(signal) && event.kind === 'projects.changed') void this.refresh()
        }, signal)
      } catch (error) {
        if (this.isWatching(signal)) console.warn('[desktop] Hub project subscription ended', error)
      }
      if (this.isWatching(signal)) await delay(RESUBSCRIBE_DELAY_MS, signal)
    }
  }
}

function delay(ms: number, signal: AbortSignal): Promise<void> {
  if (signal.aborted) return Promise.resolve()
  return new Promise((resolve) => {
    const finish = (): void => {
      clearTimeout(timer)
      signal.removeEventListener('abort', finish)
      resolve()
    }
    const timer = setTimeout(finish, ms)
    signal.addEventListener('abort', finish, { once: true })
  })
}
