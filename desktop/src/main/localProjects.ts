import { DesktopHubError, type DesktopHubClient, type HubProject } from './desktopHub'
import { sameWorkspaceProjectKey } from '../shared/workspaceProjectKey'

const RESUBSCRIBE_DELAY_MS = 5_000

type ProjectHubClient = Pick<DesktopHubClient, 'listProjects' | 'openProject' | 'removeProject' | 'subscribeEvents'>

export class LocalProjectList {
  private projects: HubProject[] = []
  private generation = 0
  private watcher: AbortController | null = null

  constructor(
    private readonly getHubClient: () => ProjectHubClient,
    private readonly onChanged: () => void
  ) {}

  list(): readonly HubProject[] {
    return this.projects
  }

  has(path: string): boolean {
    return this.projects.some((project) => sameWorkspaceProjectKey(project.path, path))
  }

  async refresh(): Promise<void> {
    const generation = ++this.generation
    const next = await this.fetchProjects()
    if (generation !== this.generation) return
    this.projects = next.sort((left, right) => Date.parse(left.addedAt) - Date.parse(right.addedAt))
    this.onChanged()
  }

  async open(path: string): Promise<void> {
    try {
      await this.getHubClient().openProject(path)
    } catch (error) {
      console.warn('[desktop] failed to add project to Hub', error)
      return
    }
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
    if (this.watcher) return
    const controller = new AbortController()
    this.watcher = controller
    void this.watch(controller.signal)
  }

  stop(): void {
    this.watcher?.abort()
    this.watcher = null
  }

  private async fetchProjects(): Promise<HubProject[]> {
    try {
      return await this.getHubClient().listProjects()
    } catch (error) {
      console.warn('[desktop] failed to list Hub projects', error)
      return []
    }
  }

  private async watch(signal: AbortSignal): Promise<void> {
    while (!signal.aborted) {
      await this.refresh()
      try {
        await this.getHubClient().subscribeEvents((event) => {
          if (event.kind === 'projects.changed') void this.refresh()
        }, signal)
      } catch (error) {
        if (!signal.aborted) console.warn('[desktop] Hub project subscription ended', error)
      }
      if (!signal.aborted) await delay(RESUBSCRIBE_DELAY_MS, signal)
    }
  }
}

function delay(ms: number, signal: AbortSignal): Promise<void> {
  return new Promise((resolve) => {
    const timer = setTimeout(resolve, ms)
    signal.addEventListener('abort', () => {
      clearTimeout(timer)
      resolve()
    }, { once: true })
  })
}
