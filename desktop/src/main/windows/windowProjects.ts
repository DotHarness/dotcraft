import { basename } from 'path'
import { normalizeSshMachines } from '../../shared/sshMachines'
import { normalizeWorkspaceProjectKey } from '../../shared/workspaceProjectKey'
import type {
  WorkspaceProjectsPayload,
  WorkspaceProjectState,
  WorkspaceProjectSummary,
  WorkspaceRemoteProjectSource
} from '../../shared/workspaceProjects'
import { resolveDefaultChatWorkspacePath } from '../defaultChatWorkspace'
import { DesktopAppServerClient } from '../DesktopAppServerClient'
import { type HubAppServerResponse } from '../desktopHub'
import { getLocalProjectDetails } from '../settings'
import {
  applyWorkspaceThreadListRequestResult,
  applyWorkspaceThreadNotificationToCache,
  noteWorkspaceReadStateChange,
  refreshWorkspaceThreadListCache,
  WORKSPACE_THREAD_LIST_RETRY_DELAYS_MS,
  type WorkspaceThreadListResult
} from '../workspaceThreadCache'
import { SECONDARY_WORKSPACE_CONNECTION_LIMIT } from './windowConstants'
import type { DesktopWindowRuntime } from './windowRuntime'
import type { WorkspaceConnectionEntry } from './windowTypes'

export function normalizeWorkspaceConnectionKey(_ctx: DesktopWindowRuntime, workspacePath: string): string {
  return normalizeWorkspaceProjectKey(workspacePath)
}

export function localProjectId(ctx: DesktopWindowRuntime, workspacePath: string): string {
  return ctx.normalizeWorkspaceConnectionKey(workspacePath)
}

export function normalizeRemoteEndpointForProjectId(_ctx: DesktopWindowRuntime, endpoint: string): string {
  const trimmed = endpoint.trim()
  try {
    const parsed = new URL(trimmed)
    parsed.username = ''
    parsed.password = ''
    parsed.search = ''
    parsed.hash = ''
    return parsed.toString().replace(/\/$/u, '').toLowerCase()
  } catch {
    return trimmed
      .replace(/[?#].*$/u, '')
      .replace(/\/$/u, '')
      .toLowerCase()
  }
}

export function remoteEndpointDisplay(_ctx: DesktopWindowRuntime, endpoint: string): string {
  const trimmed = endpoint.trim()
  try {
    const parsed = new URL(trimmed)
    parsed.username = ''
    parsed.password = ''
    parsed.search = ''
    parsed.hash = ''
    return parsed.toString().replace(/\/$/u, '')
  } catch {
    return trimmed.replace(/[?#].*$/u, '').replace(/\/$/u, '')
  }
}

export function remoteProjectId(
  ctx: DesktopWindowRuntime,
  source: WorkspaceRemoteProjectSource,
  endpoint: string
): string {
  return `remote:${source}:${ctx.normalizeRemoteEndpointForProjectId(endpoint)}`
}

export function remoteProjectNameFromEndpoint(_ctx: DesktopWindowRuntime, endpoint: string): string {
  try {
    const parsed = new URL(endpoint)
    return parsed.host || parsed.hostname || 'Remote'
  } catch {
    return endpoint.trim() || 'Remote'
  }
}

export function pinnedSettingsKey(_ctx: DesktopWindowRuntime, key: string): string {
  return normalizeWorkspaceProjectKey(key)
}

export function getWorkspaceConnection(
  ctx: DesktopWindowRuntime,
  workspacePath: string
): WorkspaceConnectionEntry | undefined {
  return ctx.workspaceConnections.get(ctx.normalizeWorkspaceConnectionKey(workspacePath))
}

export function isWorkspaceForeground(ctx: DesktopWindowRuntime, workspacePath: string): boolean {
  return (
    !ctx.activeRemoteProject &&
    Boolean(ctx.currentWorkspacePath && ctx.isSameWorkspacePath(ctx.currentWorkspacePath, workspacePath))
  )
}

export function getPinnedThreadIdsForProject(ctx: DesktopWindowRuntime, keyOrPath: string): string[] {
  const key = ctx.pinnedSettingsKey(keyOrPath)
  if (!key || !ctx.settings.pinnedThreadIdsByWorkspace) return []
  const exact = ctx.settings.pinnedThreadIdsByWorkspace[key]
  if (Array.isArray(exact)) return exact
  const match = Object.entries(ctx.settings.pinnedThreadIdsByWorkspace).find(
    ([candidate]) => normalizeWorkspaceProjectKey(candidate) === key
  )
  return match?.[1] ?? []
}

export function getPinnedThreadIdsForWorkspace(ctx: DesktopWindowRuntime, workspacePath: string): string[] {
  return ctx.getPinnedThreadIdsForProject(workspacePath)
}

export function isProjectPinned(ctx: DesktopWindowRuntime, keyOrPath: string): boolean {
  const key = ctx.pinnedSettingsKey(keyOrPath)
  return Boolean(
    key && ctx.settings.pinnedProjectIds?.some((candidate) => normalizeWorkspaceProjectKey(candidate) === key)
  )
}

export function findWorkspaceConnectionByClient(
  ctx: DesktopWindowRuntime,
  client: DesktopAppServerClient
): WorkspaceConnectionEntry | undefined {
  return [...ctx.workspaceConnections.values()].find((entry) => entry.client === client)
}

export function releaseConnectionViewAttachments(
  ctx: DesktopWindowRuntime,
  entry: WorkspaceConnectionEntry,
  reason: string
): void {
  void entry.client.releaseViewAttachments().catch((error: unknown) => {
    if (!ctx.runtimeIsStopping()) console.warn(`[desktop] failed to release view attachments during ${reason}`, error)
  })
}

export function observeAppServerRequestCompletion(
  ctx: DesktopWindowRuntime,
  client: DesktopAppServerClient,
  method: string,
  params: unknown,
  result: unknown,
  requestGeneration?: number
): void {
  const entry = ctx.findWorkspaceConnectionByClient(client)
  if (!entry) return
  if (applyWorkspaceThreadListRequestResult(entry, method, params, result, requestGeneration)) {
    ctx.emitWorkspaceProjects()
    return
  }
}

export function makeThreadListIdentity(
  _ctx: DesktopWindowRuntime,
  workspacePath: string
): Record<string, unknown> {
  return {
    channelName: 'dotcraft-desktop',
    userId: 'local',
    channelContext: `workspace:${workspacePath}`,
    workspacePath
  }
}

export function isRunningHubAppServer(
  _ctx: DesktopWindowRuntime,
  entry: HubAppServerResponse | undefined
): boolean {
  const state = entry?.state?.toLowerCase()
  return state === 'running' || state === 'healthy' || state === 'ready'
}

export function emitWorkspaceProjects(ctx: DesktopWindowRuntime): void {
  const win = ctx.mainWindow
  if (!win || win.isDestroyed()) return
  win.webContents.send('workspace:projects-changed', ctx.getWorkspaceProjectsPayload())
}

export function buildDefaultChatSummary(ctx: DesktopWindowRuntime): WorkspaceProjectSummary | undefined {
  if (
    ctx.activeRemoteProject ||
    ctx.activeRemoteWorkspace ||
    ctx.resolveConnectionMode(ctx.settings) !== 'local'
  ) {
    return undefined
  }
  const chatPath = resolveDefaultChatWorkspacePath()
  const entry = ctx.getWorkspaceConnection(chatPath)
  let state: WorkspaceProjectState = 'cold'
  if (ctx.isWorkspaceForeground(chatPath)) state = 'foreground'
  else if (entry?.connecting) state = 'connecting'
  else if (entry?.errorMessage) state = 'error'
  else if (entry?.connected) state = 'secondary'
  return {
    projectId: ctx.localProjectId(chatPath),
    kind: 'chat',
    path: chatPath,
    identityWorkspacePath: chatPath,
    name: chatPath,
    state,
    running: Boolean(entry?.connected || entry?.connecting || state === 'foreground'),
    loaded: Boolean(entry?.connected),
    threadCount: entry?.threads.length ?? 0,
    threads: entry?.threads ?? [],
    pinnedThreadIds: ctx.getPinnedThreadIdsForWorkspace(chatPath),
    pinned: false,
    ...(entry?.errorMessage ? { errorMessage: entry.errorMessage } : {})
  }
}

export function getWorkspaceProjectsPayload(ctx: DesktopWindowRuntime): WorkspaceProjectsPayload {
  const projects = ctx.app.localProjects.list().map((project): WorkspaceProjectSummary => {
    const entry = ctx.getWorkspaceConnection(project.path)
    let state: WorkspaceProjectState = 'cold'
    if (ctx.isWorkspaceForeground(project.path)) state = 'foreground'
    else if (entry?.connecting) state = 'connecting'
    else if (entry?.errorMessage) state = 'error'
    else if (entry?.connected) state = 'secondary'
    const projectId = ctx.localProjectId(project.path)
    const details = getLocalProjectDetails(ctx.settings, project.path)
    return {
      projectId,
      kind: 'local',
      path: project.path,
      identityWorkspacePath: project.path,
      primaryFolder: details?.primaryFolder ?? project.path,
      name: details?.name || project.displayName || basename(project.path),
      lastOpenedAt: project.lastOpenedAt,
      state,
      running: Boolean(entry?.connected || entry?.connecting || state === 'foreground'),
      loaded: Boolean(entry?.connected),
      threadCount: entry?.threads.length ?? 0,
      threads: entry?.threads ?? [],
      pinnedThreadIds: ctx.getPinnedThreadIdsForWorkspace(project.path),
      pinned: ctx.isProjectPinned(projectId),
      secondaryFolders: details?.secondaryFolders ?? [],
      ...(entry?.errorMessage ? { errorMessage: entry.errorMessage } : {})
    }
  })
  for (const machine of normalizeSshMachines(ctx.settings.remoteHosts)) {
    for (const remoteProject of machine.projects) {
      const saved = ctx.buildSshRemoteProject(machine, remoteProject, undefined, '')
      if (saved.projectId === ctx.activeRemoteProject?.projectId) continue
      projects.push({
        projectId: saved.projectId,
        kind: 'remote',
        path: saved.displayPath,
        identityWorkspacePath: saved.identityWorkspacePath,
        name: saved.name,
        state: 'cold',
        running: false,
        loaded: false,
        threadCount: 0,
        threads: [],
        pinnedThreadIds: ctx.getPinnedThreadIdsForProject(saved.projectId),
        pinned: ctx.isProjectPinned(saved.projectId),
        remote: saved.remote
      })
    }
  }
  for (const { computer, project: deviceProject } of ctx.app.pairedComputers.savedProjects()) {
    const saved = ctx.buildDeviceRemoteProject(computer, deviceProject, undefined, '')
    if (saved.projectId === ctx.activeRemoteProject?.projectId) continue
    const entry = ctx.workspaceConnections.get(saved.projectId)
    const state: WorkspaceProjectState = entry?.errorMessage
      ? 'error'
      : entry?.connecting
        ? 'connecting'
        : entry?.connected
          ? 'secondary'
          : 'cold'
    projects.push({
      projectId: saved.projectId,
      kind: 'remote',
      path: saved.displayPath,
      identityWorkspacePath: saved.identityWorkspacePath,
      name: saved.name,
      state,
      running: Boolean(entry?.connected || entry?.connecting),
      loaded: Boolean(entry?.connected || entry?.initializeResult),
      threadCount: entry?.threads.length ?? 0,
      threads: entry?.threads ?? [],
      pinnedThreadIds: ctx.getPinnedThreadIdsForProject(saved.projectId),
      pinned: ctx.isProjectPinned(saved.projectId),
      remote: saved.remote,
      ...(entry?.errorMessage ? { errorMessage: entry.errorMessage } : {})
    })
  }
  if (ctx.activeRemoteProject) {
    const entry = ctx.workspaceConnections.get(ctx.activeRemoteProject.projectId)
    const state: WorkspaceProjectState = entry?.errorMessage
      ? 'error'
      : entry?.connecting
        ? 'connecting'
        : 'foreground'
    projects.unshift({
      projectId: ctx.activeRemoteProject.projectId,
      kind: 'remote',
      path: ctx.activeRemoteProject.displayPath,
      identityWorkspacePath: ctx.activeRemoteProject.identityWorkspacePath,
      name: ctx.activeRemoteProject.name,
      state,
      running: Boolean(entry?.connected || entry?.connecting || state === 'foreground'),
      loaded: Boolean(entry?.connected),
      threadCount: entry?.threads.length ?? 0,
      threads: entry?.threads ?? [],
      pinnedThreadIds: ctx.getPinnedThreadIdsForProject(ctx.activeRemoteProject.projectId),
      pinned: ctx.isProjectPinned(ctx.activeRemoteProject.projectId),
      remote: ctx.activeRemoteProject.remote,
      ...(entry?.errorMessage ? { errorMessage: entry.errorMessage } : {})
    })
  }
  const chat = ctx.buildDefaultChatSummary()
  return {
    foregroundWorkspacePath: ctx.currentWorkspacePath,
    foregroundProjectId:
      ctx.activeRemoteProject?.projectId ??
      (ctx.currentWorkspacePath ? ctx.localProjectId(ctx.currentWorkspacePath) : ''),
    secondaryLimit: SECONDARY_WORKSPACE_CONNECTION_LIMIT,
    projects,
    ...(chat ? { chat } : {})
  }
}

export async function refreshConnectionThreadList(
  ctx: DesktopWindowRuntime,
  entry: WorkspaceConnectionEntry
): Promise<void> {
  if (!entry.connected) return
  const changed = await refreshWorkspaceThreadListCache(
    entry,
    () =>
      entry.client.sendRequest<WorkspaceThreadListResult>('thread/list', {
        identity: ctx.makeThreadListIdentity(entry.workspacePath),
        scope: 'workspace',
        includeSubAgents: true
      }),
    {
      retryDelaysMs: WORKSPACE_THREAD_LIST_RETRY_DELAYS_MS,
      isActive: () => entry.connected && ctx.workspaceConnections.get(entry.key) === entry,
      onRetryChanged: ctx.emitWorkspaceProjects
    }
  )
  if (changed && ctx.workspaceConnections.get(entry.key) === entry) ctx.emitWorkspaceProjects()
}

export function applyWorkspaceThreadNotification(
  ctx: DesktopWindowRuntime,
  entry: WorkspaceConnectionEntry,
  method: string,
  params: unknown
): void {
  const result = applyWorkspaceThreadNotificationToCache(entry.threads, method, params)
  entry.threads = result.threads
  const readStateThreadId =
    method === 'thread/readState/changed' && params && typeof params === 'object'
      ? (params as { threadId?: unknown }).threadId
      : undefined
  if (typeof readStateThreadId === 'string') noteWorkspaceReadStateChange(entry, readStateThreadId)
  if (result.changed) {
    ctx.emitWorkspaceProjects()
  }
  if (result.refreshThreadList) {
    void ctx.refreshConnectionThreadList(entry)
  }
}

export function disposeWorkspaceConnection(ctx: DesktopWindowRuntime, entry: WorkspaceConnectionEntry): void {
  if (ctx.workspaceConnections.get(entry.key) !== entry) return
  ctx.workspaceConnections.delete(entry.key)
  if (ctx.wireClient === entry.client) {
    ctx.wireClient = null
  }
  ctx.disposeDeviceProjectConnection(entry)
  entry.client.dispose()
  ctx.emitWorkspaceProjects()
}

export function canActivateWorkspaceInThisWindow(ctx: DesktopWindowRuntime, workspacePath: string): boolean {
  return !ctx.activeRemoteProject && workspacePath.trim().length > 0
}
