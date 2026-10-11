import { existsSync, watch, type FSWatcher } from 'node:fs'
import { isAbsolute, join, resolve } from 'node:path'
import type {
  AddProjectActionRequest,
  AddProjectActionResult,
  ProjectActionPreferences,
  ProjectActionRun,
  ProjectActionRunRequest,
  ProjectEnvironmentsResult
} from '../../shared/projectActions'
import {
  discoverProjectEnvironments,
  environmentFolders,
  environmentsDirectory,
  type DiscoveredEnvironment
} from './discovery'
import { applyProjectActionPreferences, withRecentAction, withSelectedEnvironment } from './preferences'
import { writeProjectAction } from './write'

const WATCH_DEBOUNCE_MS = 150

export interface ProjectActionsServiceDeps {
  readPreferences(projectKey: string): ProjectActionPreferences | undefined
  writePreferences(projectKey: string, preferences: ProjectActionPreferences): Promise<void>
  notifyChanged(subscriberIds: readonly number[]): void
  platform?: NodeJS.Platform
}

interface WorkspaceWatch {
  subscribers: Map<number, number>
  watchers: FSWatcher[]
  targets: string
  timer: ReturnType<typeof setTimeout> | null
  signature: string | null
}

function watchTarget(folder: string): string {
  const environments = environmentsDirectory(folder)
  if (existsSync(environments)) return environments
  const craft = join(folder, '.craft')
  return existsSync(craft) ? craft : folder
}

export class ProjectActionsService {
  private readonly watches = new Map<string, WorkspaceWatch>()

  constructor(private readonly deps: ProjectActionsServiceDeps) {}

  async list(workspacePath: string): Promise<ProjectEnvironmentsResult> {
    const key = projectKey(workspacePath)
    const discovered = await this.discover(key)
    const { environments, selectedEnvironmentId } = applyProjectActionPreferences(
      discovered,
      this.deps.readPreferences(key)
    )
    return {
      environments: environments.map(({ root: _root, ...environment }) => environment),
      selectedEnvironmentId
    }
  }

  async select(workspacePath: string, environmentId: string): Promise<void> {
    const key = projectKey(workspacePath)
    await this.deps.writePreferences(key, withSelectedEnvironment(this.deps.readPreferences(key), environmentId))
    this.notify(key)
  }

  async prepareRun(request: ProjectActionRunRequest): Promise<ProjectActionRun> {
    const key = projectKey(request.workspacePath)
    const environment = (await this.discover(key)).find((candidate) => candidate.id === request.environmentId)
    const action = environment?.actions.find((candidate) => candidate.id === request.actionId)
    if (!environment || !action) throw new Error(`Project action not found: ${request.actionId}`)
    await this.deps.writePreferences(
      key,
      withRecentAction(this.deps.readPreferences(key), environment.id, action.id)
    )
    this.notify(key)
    return { cwd: environment.root, command: action.command, title: action.name }
  }

  async add(request: AddProjectActionRequest): Promise<AddProjectActionResult> {
    const key = projectKey(request.workspacePath)
    const environments = await this.discover(key)
    const target = request.environmentId === undefined
      ? null
      : environments.find((environment) => environment.id === request.environmentId)
    if (target === undefined || (target === null && environments.length > 0)) return { ok: false, reason: 'conflict' }
    const written = await writeProjectAction({
      workspacePath: key,
      target,
      revision: request.revision,
      draft: request.action
    })
    if (!written.ok) return written
    const saved = (await this.discover(key)).find((environment) => environment.path === written.path)
    this.notify(key)
    if (!saved) return { ok: false, reason: 'failed' }
    const platform = request.action.platform
    if (platform && platform !== (this.deps.platform ?? process.platform)) {
      return { ok: true, environmentId: saved.id, action: null }
    }
    const name = request.action.name.trim()
    const action = [...saved.actions].reverse().find((candidate) => candidate.name === name) ?? null
    return { ok: true, environmentId: saved.id, action }
  }

  subscribe(subscriberId: number, workspacePath: string): void {
    const key = projectKey(workspacePath)
    let entry = this.watches.get(key)
    if (!entry) {
      entry = { subscribers: new Map(), watchers: [], targets: '', timer: null, signature: null }
      this.watches.set(key, entry)
      this.rearm(key, entry)
      const created = entry
      void this.discover(key).then((environments) => {
        created.signature ??= JSON.stringify(environments)
      }, () => {})
    }
    entry.subscribers.set(subscriberId, (entry.subscribers.get(subscriberId) ?? 0) + 1)
  }

  unsubscribe(subscriberId: number, workspacePath?: string): void {
    for (const [key, entry] of this.watches) {
      if (workspacePath !== undefined && key !== projectKey(workspacePath)) continue
      const count = entry.subscribers.get(subscriberId) ?? 0
      if (workspacePath === undefined || count <= 1) entry.subscribers.delete(subscriberId)
      else entry.subscribers.set(subscriberId, count - 1)
      if (entry.subscribers.size === 0) this.close(key, entry)
    }
  }

  private async discover(key: string): Promise<DiscoveredEnvironment[]> {
    return discoverProjectEnvironments(key, this.deps.platform)
  }

  private notify(key: string): void {
    const entry = this.watches.get(key)
    if (entry) this.deps.notifyChanged([...entry.subscribers.keys()])
  }

  private close(key: string, entry: WorkspaceWatch): void {
    if (entry.timer) clearTimeout(entry.timer)
    for (const watcher of entry.watchers) watcher.close()
    this.watches.delete(key)
  }

  private rearm(key: string, entry: WorkspaceWatch): void {
    const targets = environmentFolders(key).map(watchTarget)
    const signature = targets.join('\n')
    if (signature === entry.targets) return
    for (const watcher of entry.watchers) watcher.close()
    entry.targets = signature
    entry.watchers = targets.flatMap((target) => {
      try {
        return [watch(target, () => this.schedule(key, entry))]
      } catch {
        return []
      }
    })
  }

  private schedule(key: string, entry: WorkspaceWatch): void {
    if (entry.timer) clearTimeout(entry.timer)
    entry.timer = setTimeout(() => {
      entry.timer = null
      void this.refresh(key, entry)
    }, WATCH_DEBOUNCE_MS)
  }

  private async refresh(key: string, entry: WorkspaceWatch): Promise<void> {
    if (this.watches.get(key) !== entry) return
    this.rearm(key, entry)
    const signature = JSON.stringify(await this.discover(key))
    if (this.watches.get(key) !== entry || signature === entry.signature) return
    entry.signature = signature
    this.notify(key)
  }
}

export function projectKey(workspacePath: string): string {
  if (typeof workspacePath !== 'string' || !isAbsolute(workspacePath)) throw new Error('An absolute workspace path is required')
  return resolve(workspacePath)
}
