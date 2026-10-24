import { existsSync, watch, type FSWatcher } from 'node:fs'
import { basename, isAbsolute, join, relative, resolve, sep } from 'node:path'
import type {
  AddProjectActionRequest,
  AddProjectActionResult,
  ProjectActionPreferences,
  ProjectActionRun,
  ProjectActionRunRequest,
  ProjectEnvironmentList,
  ProjectEnvironmentsResult,
  ProjectSetupRun,
  ProjectSetupRunRequest,
  ReadEnvironmentResult,
  WorktreeEnvironmentsResult,
  WriteEnvironmentRequest,
  WriteEnvironmentResult
} from '../../shared/projectActions'
import {
  DEFAULT_ENVIRONMENT_FILE,
  discoverProjectEnvironments,
  environmentFolders,
  environmentsDirectory,
  repositoryRoot,
  type DiscoveredEnvironment
} from './discovery'
import { actionDraft, emptyEnvironmentDocument, normalizeEnvironmentDocument, platformScript } from './environmentDocument'
import {
  applyProjectActionPreferences,
  applyWorktreeEnvironmentPreference,
  withRecentAction,
  withSelectedEnvironment,
  withWorktreeEnvironment
} from './preferences'
import {
  environmentFilePath,
  newEnvironmentPath,
  readEnvironmentFile,
  writeEnvironmentFile
} from './write'

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

function runFolder(workspacePath: string, environmentRoot: string, worktreePath: string | undefined): string {
  if (worktreePath === undefined) return environmentRoot
  const worktree = projectKey(worktreePath)
  const path = relative(repositoryRoot(workspacePath), environmentRoot)
  if (path === '..' || path.startsWith(`..${sep}`) || isAbsolute(path)) return worktree
  return join(worktree, path)
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

  async listProjectEnvironments(projectPath: string): Promise<ProjectEnvironmentList> {
    const key = projectKey(projectPath)
    const environments = (await this.discover(key)).map((environment) => ({
      id: environment.id,
      name: environment.name,
      fileName: basename(environment.path),
      path: environment.path,
      folder: environment.root,
      isDefault: environment.isDefault,
      inherited: environment.root !== key,
      ...(environment.error === undefined ? {} : { error: environment.error })
    }))
    return { projectPath: key, environments, newEnvironmentPath: newEnvironmentPath(key) }
  }

  async readEnvironment(params: { projectPath: string; path: string }): Promise<ReadEnvironmentResult> {
    const path = environmentPathIn(params?.projectPath, params?.path)
    return path ? readEnvironmentFile(path) : { ok: false, reason: 'failed' }
  }

  async writeEnvironment(request: WriteEnvironmentRequest): Promise<WriteEnvironmentResult> {
    const path = environmentPathIn(request?.projectPath, request?.path)
    const document = normalizeEnvironmentDocument(request?.document)
    const revision = request?.revision
    if (!path || !document || (revision !== null && typeof revision !== 'string')) return { ok: false, reason: 'failed' }
    const result = await writeEnvironmentFile(path, revision, document)
    if (result.ok) this.notify(projectKey(request.projectPath))
    return result
  }

  async select(workspacePath: string, environmentId: string | null): Promise<void> {
    const key = projectKey(workspacePath)
    await this.deps.writePreferences(key, withSelectedEnvironment(this.deps.readPreferences(key), environmentId))
    this.notify(key)
  }

  async listWorktreeEnvironments(workspacePath: string): Promise<WorktreeEnvironmentsResult> {
    const key = projectKey(workspacePath)
    return applyWorktreeEnvironmentPreference(await this.discover(key), this.deps.readPreferences(key))
  }

  async selectWorktreeEnvironment(workspacePath: string, environmentId: string | null): Promise<void> {
    if (environmentId !== null && (typeof environmentId !== 'string' || !environmentId)) {
      throw new Error('An environment id or null is required')
    }
    const key = projectKey(workspacePath)
    await this.deps.writePreferences(key, withWorktreeEnvironment(this.deps.readPreferences(key), environmentId))
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
    return {
      cwd: runFolder(key, environment.root, request.worktreePath),
      command: action.command,
      title: action.name
    }
  }

  async prepareSetup(request: ProjectSetupRunRequest): Promise<ProjectSetupRun | null> {
    const key = projectKey(request.workspacePath)
    const environment = (await this.discover(key)).find((candidate) => candidate.id === request.environmentId)
    if (!environment || environment.error !== undefined) throw new Error(`Environment not found: ${request.environmentId}`)
    const file = await readEnvironmentFile(environment.path)
    const command = file.ok && file.document ? platformScript(file.document.setup, this.deps.platform ?? process.platform) : ''
    if (!command) return null
    const executionWorkspace = request.worktreePath === undefined ? key : projectKey(request.worktreePath)
    return {
      cwd: runFolder(key, environment.root, request.worktreePath),
      command,
      env: { DOTCRAFT_SOURCE_TREE_PATH: executionWorkspace, DOTCRAFT_WORKTREE_PATH: executionWorkspace }
    }
  }

  async add(request: AddProjectActionRequest): Promise<AddProjectActionResult> {
    const key = projectKey(request.workspacePath)
    const draft = actionDraft(request.action)
    if (draft === null || draft === 'incomplete') return { ok: false, reason: 'failed' }
    const environments = await this.discover(key)
    const target = request.environmentId === undefined
      ? null
      : environments.find((environment) => environment.id === request.environmentId)
    if (target === undefined || (target === null && environments.length > 0)) return { ok: false, reason: 'conflict' }
    const path = target?.path ?? join(environmentsDirectory(key), DEFAULT_ENVIRONMENT_FILE)
    const current = await readEnvironmentFile(path)
    if (!current.ok) return current
    if (current.revision !== request.revision) return { ok: false, reason: 'conflict' }
    const document = current.document ?? (current.revision === null ? emptyEnvironmentDocument(basename(key)) : null)
    if (!document) return { ok: false, reason: 'failed' }
    const written = await writeEnvironmentFile(path, current.revision, {
      ...document,
      actions: [...document.actions, draft]
    })
    if (!written.ok) return written
    const saved = (await this.discover(key)).find((environment) => environment.path === path)
    if (!saved) {
      this.notify(key)
      return { ok: false, reason: 'failed' }
    }
    const action = draft.platform && draft.platform !== (this.deps.platform ?? process.platform)
      ? null
      : [...saved.actions].reverse().find((candidate) => candidate.name === draft.name) ?? null
    if (action) {
      await this.deps.writePreferences(key, withRecentAction(this.deps.readPreferences(key), saved.id, action.id))
    }
    this.notify(key)
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

function environmentPathIn(projectPath: unknown, path: unknown): string | null {
  if (typeof projectPath !== 'string' || !isAbsolute(projectPath)) return null
  return environmentFilePath(resolve(projectPath), path)
}

export function projectKey(workspacePath: string): string {
  if (typeof workspacePath !== 'string' || !isAbsolute(workspacePath)) throw new Error('An absolute workspace path is required')
  return resolve(workspacePath)
}
