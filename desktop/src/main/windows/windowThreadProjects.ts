import { basename } from 'node:path'
import { normalizeSshMachines } from '../../shared/sshMachines'
import { isRemoteProjectKey } from '../../shared/workspaceProjectKey'
import { isDefaultChatWorkspace, resolveDefaultChatWorkspacePath } from '../defaultChatWorkspace'
import { sendDesktopAppServerRequest } from '../desktopRuntimeThreadTools'
import { getLocalProjectDetails } from '../settings'
import type { LinkedChatConnection } from '../threadTasks/linkedChats'
import { notifyConductorProjectConnected, observeConductorNotification } from '../threadTasks/conductorProactive'
import { notConnectedMessage, ThreadProjectError, type ThreadProject, type ThreadProjectLocation } from '../threadTasks/threadProjects'
import type { DesktopWindowRuntime } from './windowRuntime'
import type { WorkspaceConnectionEntry } from './windowTypes'

const READY_TIMEOUT_MS = 25_000
const openings = new WeakMap<DesktopWindowRuntime, Map<string, Promise<WorkspaceConnectionEntry>>>()
const requests = new WeakMap<WorkspaceConnectionEntry, number>()
const observed = new WeakMap<WorkspaceConnectionEntry, LinkedChatConnection>()

export function threadProjectForEntry(ctx: DesktopWindowRuntime, entry: WorkspaceConnectionEntry): ThreadProject {
  const path = entry.workspacePath
  if (entry.kind === 'remote') {
    return {
      id: entry.projectId, name: entry.remoteProject?.name ?? entry.displayPath ?? path, path, running: entry.connected,
      machine: entry.remote?.serverName
    }
  }
  if (isDefaultChatWorkspace(path)) {
    return { id: entry.projectId, name: 'Chats', path, running: entry.connected }
  }
  const details = getLocalProjectDetails(ctx.settings, path)
  const name = details?.name
    || ctx.app.localProjects.list().find(project => ctx.localProjectId(project.path) === entry.projectId)?.displayName
    || basename(path)
  return {
    id: entry.projectId,
    name,
    path,
    running: entry.connected,
    ...(details?.primaryFolder ? { primaryFolder: details.primaryFolder } : {}),
    ...(details?.secondaryFolders?.length ? { secondaryFolders: details.secondaryFolders } : {})
  }
}

export async function listThreadProjects(ctx: DesktopWindowRuntime): Promise<ThreadProject[]> {
  const chatPath = resolveDefaultChatWorkspacePath()
  const [servers] = await Promise.all([
    ctx.app.createHubClient(ctx.settings).listAppServers(),
    ctx.app.localProjects.refresh()
  ])
  const chatRunning = servers.some(server => ctx.isRunningHubAppServer(server)
    && [server.workspacePath, server.canonicalWorkspacePath].some(path => path && isDefaultChatWorkspace(path)))
  return [
    { id: ctx.localProjectId(chatPath), name: 'Chats', path: chatPath, running: chatRunning },
    ...ctx.app.localProjects.list().filter(project => !isDefaultChatWorkspace(project.path)).map(project => {
      const details = getLocalProjectDetails(ctx.settings, project.path)
      return {
        id: ctx.localProjectId(project.path),
        name: details?.name || project.displayName || basename(project.path),
        path: project.path,
        running: project.running,
        ...(details?.primaryFolder ? { primaryFolder: details.primaryFolder } : {}),
        ...(details?.secondaryFolders?.length ? { secondaryFolders: details.secondaryFolders } : {})
      }
    }),
    ...[...ctx.workspaceConnections.values()].filter(isReachableRemote).map(entry => threadProjectForEntry(ctx, entry))
  ]
}

function isReachableRemote(entry: WorkspaceConnectionEntry): boolean {
  return entry.kind === 'remote' && (entry.remote?.source === 'ssh' || entry.remote?.source === 'device') &&
    (entry.connected || entry.connecting)
}

export function disconnectedThreadProjectName(ctx: DesktopWindowRuntime, id: string): string | undefined {
  for (const machine of normalizeSshMachines(ctx.settings.remoteHosts)) {
    for (const project of machine.projects) {
      if (ctx.sshRemoteProjectId(machine.id, project.id) === id) return project.label
    }
  }
  for (const { computer, project } of ctx.app.pairedComputers.savedProjects()) {
    if (ctx.deviceRemoteProjectId(computer.computerId, project.projectId) === id) return project.displayName
  }
}

export function openThreadProject(ctx: DesktopWindowRuntime, project: ThreadProjectLocation): Promise<WorkspaceConnectionEntry> {
  let pending = openings.get(ctx)
  if (!pending) {
    pending = new Map()
    openings.set(ctx, pending)
  }
  const existing = pending.get(project.id)
  if (existing) return existing
  const request = open().finally(() => pending.delete(project.id))
  pending.set(project.id, request)
  return request

  async function open(): Promise<WorkspaceConnectionEntry> {
    if (ctx.runtimeIsStopping()) throw new Error('Desktop is closing.')
    const current = ctx.workspaceConnections.get(project.id)
    if (isRemoteProjectKey(project.id)) {
      if (!current || !isReachableRemote(current)) throw new ThreadProjectError('ProjectUnavailable', notConnectedMessage(project.name))
    } else if (!current?.connected && !current?.connecting) {
      if (isDefaultChatWorkspace(project.path)) await ctx.ensureDefaultChatConnection()
      else await ctx.startLocalProjectConnection(project.path)
    }
    const entry = ctx.workspaceConnections.get(project.id)
    if (!entry || ctx.runtimeIsStopping()) throw new Error('The AppServer connection was not started.')
    await waitForProjectReady(entry)
    if (ctx.workspaceConnections.get(project.id) !== entry || ctx.runtimeIsStopping()) {
      throw new Error('The AppServer connection is no longer active.')
    }
    entry.lastUsedAt = Date.now()
    return entry
  }
}

function waitForProjectReady(entry: WorkspaceConnectionEntry): Promise<void> {
  if (entry.connected) return Promise.resolve()
  if (!entry.connecting || entry.client.isDisposed) {
    return Promise.reject(new Error(entry.errorMessage ?? 'The AppServer did not connect.'))
  }
  return new Promise((resolve, reject) => {
    const { client } = entry
    const ready = (): void => finish()
    const closed = (): void => finish(new Error(entry.errorMessage ?? 'The AppServer disconnected before it was ready.'))
    const failed = (error: unknown): void => finish(error instanceof Error ? error : new Error(String(error)))
    const timer = setTimeout(() => finish(new Error('Timed out waiting for the AppServer.')), READY_TIMEOUT_MS)
    client.once('ready', ready).once('reconnected', ready).once('close', closed).once('disposed', closed).once('reconnect-error', failed)

    function finish(error?: Error): void {
      clearTimeout(timer)
      client.off('ready', ready).off('reconnected', ready).off('close', closed).off('disposed', closed).off('reconnect-error', failed)
      if (error) reject(error)
      else resolve()
    }
  })
}

export async function holdThreadProjectRequest<T>(entry: WorkspaceConnectionEntry, request: () => Promise<T>): Promise<T> {
  entry.lastUsedAt = Date.now()
  requests.set(entry, (requests.get(entry) ?? 0) + 1)
  try {
    return await request()
  } finally {
    const count = requests.get(entry)!
    if (count === 1) requests.delete(entry)
    else requests.set(entry, count - 1)
  }
}

export function threadToolsHoldConnection(ctx: DesktopWindowRuntime, entry: WorkspaceConnectionEntry): boolean {
  const connection = observed.get(entry)
  return requests.has(entry) || openings.get(ctx)?.has(entry.projectId) === true ||
    (connection !== undefined && ctx.app?.tasks?.linkedChats?.holdsConnection(connection) === true)
}

export function linkedChatsNeedProject(ctx: DesktopWindowRuntime, projectId: string): boolean {
  return ctx.app?.tasks?.linkedChats?.needsProjectConnection(projectId) === true
}

export function connectLinkedChats(ctx: DesktopWindowRuntime, entry: WorkspaceConnectionEntry): void {
  const linkedChats = ctx.app?.tasks?.linkedChats
  if (!linkedChats) return
  let connection = observed.get(entry)
  if (!connection) {
    connection = {
      projectId: entry.projectId,
      workspacePath: entry.workspacePath,
      request: (method, params) => method === 'turn/notify'
        ? sendDesktopAppServerRequest(entry.client, method, params, undefined, {
            supportsDynamicToolRebind: entry.initializeResult?.capabilities?.dynamicToolRebind === true,
            remote: entry.kind === 'remote'
          })
        : entry.client.sendRequest(method, params)
    }
    observed.set(entry, connection)
    const wrapper = connection
    const disconnect = (): void => linkedChats.disconnected(wrapper)
    entry.client.on('close', disconnect).once('disposed', disconnect)
  }
  linkedChats.connected(connection)
  notifyConductorProjectConnected(ctx)
}

export function observeLinkedChatNotification(ctx: DesktopWindowRuntime, entry: WorkspaceConnectionEntry, method: string, params: unknown): void {
  const connection = observed.get(entry)
  if (connection) ctx.app.tasks.linkedChats?.handleNotification(connection, method, params)
  observeConductorNotification(ctx, entry, method, params)
}
