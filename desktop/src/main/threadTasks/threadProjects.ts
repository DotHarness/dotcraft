import { normalizeWorkspaceProjectKey } from '../../shared/workspaceProjectKey'
import type { ThreadTaskConnection } from './childThreads'

export interface ThreadProjectLocation {
  id: string
  name: string
  path: string
}

export interface ThreadProject extends ThreadProjectLocation {
  running: boolean
  machine?: string
  primaryFolder?: string
  secondaryFolders?: string[]
}

export interface ThreadProjectHost {
  list(): Promise<ThreadProject[]>
  connect(project: ThreadProjectLocation): Promise<ThreadTaskConnection>
  disconnected?(id: string): string | undefined
}

export class ThreadProjectError extends Error {
  constructor(readonly code: 'ProjectNotFound' | 'ProjectUnavailable' | 'TargetUnsupported' | 'InvalidArguments', message: string) {
    super(message)
  }
}

export const PROJECT_PROPERTY = { type: 'string', description: 'Project id from ListProjects. Omit for a linked chat or the current workspace.' }

export function readProjectArgument(value: unknown): string | undefined {
  if (value == null) return undefined
  if (typeof value === 'string' && value.trim()) return value.trim()
  throw new ThreadProjectError('InvalidArguments', 'project must be a non-empty project id from ListProjects.')
}

export function currentThreadProject(connection: ThreadTaskConnection): ThreadProject {
  return connection.project ?? {
    id: normalizeWorkspaceProjectKey(connection.workspacePath),
    name: connection.workspacePath.split(/[\\/]/).filter(Boolean).at(-1) ?? connection.workspacePath,
    path: connection.workspacePath,
    running: true
  }
}

export function callingThread(connection: ThreadTaskConnection, threadId: string) {
  return { threadId, projectId: currentThreadProject(connection).id, workspacePath: connection.workspacePath }
}

export async function resolveThreadProject(connection: ThreadTaskConnection, id?: string): Promise<ThreadProject> {
  const source = currentThreadProject(connection)
  if (id === undefined || id === source.id) return source
  if (connection.remote) {
    throw new ThreadProjectError('TargetUnsupported', 'Thread tools in a remote project can only reach their own workspace.')
  }
  const project = (await connection.projects?.list())?.find(candidate => candidate.id === id)
  if (project) return project
  const disconnected = connection.projects?.disconnected?.(id)
  if (disconnected !== undefined) throw new ThreadProjectError('ProjectUnavailable', notConnectedMessage(disconnected))
  throw new ThreadProjectError('ProjectNotFound', `Project '${id}' was not found. Use an id from ListProjects.`)
}

export function notConnectedMessage(name: string): string {
  return `Project "${name}" is on another machine that Desktop is not connected to. Ask the person to connect it in Desktop, then try again.`
}

export async function connectThreadProject(connection: ThreadTaskConnection, project: ThreadProjectLocation): Promise<ThreadTaskConnection> {
  if (project.id === currentThreadProject(connection).id) return connection
  try {
    if (!connection.projects) throw new Error('No project connection is available.')
    return await connection.projects.connect(project)
  } catch (error) {
    if (error instanceof ThreadProjectError) throw error
    throw new ThreadProjectError('ProjectUnavailable', `Could not connect to project "${project.name}": ${error instanceof Error ? error.message : String(error)}`)
  }
}
