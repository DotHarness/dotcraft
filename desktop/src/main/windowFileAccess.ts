import { normalizeWorkspaceProjectKey } from '../shared/workspaceProjectKey'

export interface WindowFileAccessOptions {
  getWorkspaceRoot(): string
  getSecondaryRoots?(workspaceRoot: string): readonly string[]
  getChatFolderRoots?(workspaceRoot?: string): readonly string[]
}

export interface WindowFileAccess extends WindowFileAccessOptions {
  readonly ownerId: number
  readonly authorizedExternalFiles: Set<string>
  authorizationEpoch: number
}

interface WindowRegistration {
  ownerId: number
  options: WindowFileAccessOptions
  foreground?: WindowFileAccess
  scopes: Map<string, WindowFileAccess>
  grants: Map<string, Set<string>>
  sessions: Set<Electron.Session>
}

interface SessionBinding {
  registration: WindowRegistration
  context?: WindowFileAccess
}

const windows = new Map<number, WindowRegistration>()
const sessions = new WeakMap<Electron.Session, SessionBinding>()

export function registerWindowFileAccess(ownerId: number, options: WindowFileAccessOptions): () => void {
  if (windows.has(ownerId)) throw new Error('Window file access is already registered.')
  const registration: WindowRegistration = {
    ownerId, options, scopes: new Map(), grants: new Map(), sessions: new Set()
  }
  windows.set(ownerId, registration)
  return () => {
    if (windows.get(ownerId) !== registration) return
    revokeAllWindowFileAccess(ownerId)
    windows.delete(ownerId)
    for (const targetSession of registration.sessions) sessions.delete(targetSession)
    registration.sessions.clear()
  }
}

function createContext(registration: WindowRegistration, root: string, scoped: boolean): WindowFileAccess {
  const key = normalizeWorkspaceProjectKey(root)
  let grants = registration.grants.get(key)
  if (!grants) registration.grants.set(key, grants = new Set())
  const chatRoots = scoped ? [...registration.options.getChatFolderRoots?.(root) ?? []] : []
  return {
    ownerId: registration.ownerId,
    getWorkspaceRoot: () => root,
    getSecondaryRoots: () => registration.options.getSecondaryRoots?.(root) ?? [],
    getChatFolderRoots: scoped ? () => chatRoots : () => registration.options.getChatFolderRoots?.(root) ?? [],
    authorizedExternalFiles: grants,
    authorizationEpoch: 0
  }
}

export function getWindowFileAccess(ownerId: number | undefined): WindowFileAccess | undefined {
  if (ownerId === undefined) return undefined
  const registration = windows.get(ownerId)
  if (!registration) return undefined
  const root = registration.options.getWorkspaceRoot()
  if (registration.foreground?.getWorkspaceRoot() !== root) {
    if (registration.foreground) registration.foreground.authorizationEpoch++
    registration.foreground = createContext(registration, root, false)
  }
  return registration.foreground
}

export function scopedWindowFileAccess(ownerId: number, workspaceRoot: string): WindowFileAccess {
  const registration = windows.get(ownerId)
  if (!registration) throw new Error('Window file access is not registered.')
  if (!workspaceRoot) throw new Error('A preview requires an owning workspace.')
  const key = normalizeWorkspaceProjectKey(workspaceRoot)
  let context = registration.scopes.get(key)
  if (!context) {
    context = createContext(registration, workspaceRoot, true)
    registration.scopes.set(key, context)
  }
  return context
}

export function bindWindowFileSession(targetSession: Electron.Session, ownerId: number, workspaceRoot?: string): void {
  const registration = windows.get(ownerId)
  if (!registration) throw new Error('Window file access is not registered.')
  const context = workspaceRoot === undefined ? undefined : scopedWindowFileAccess(ownerId, workspaceRoot)
  const previous = sessions.get(targetSession)
  if (previous && (previous.registration !== registration || previous.context !== context)) {
    throw new Error('A file-serving session cannot be shared by windows or workspaces.')
  }
  sessions.set(targetSession, { registration, context })
  registration.sessions.add(targetSession)
}

export function windowFileAccessForSession(targetSession: Electron.Session): WindowFileAccess | undefined {
  const binding = sessions.get(targetSession)
  if (!binding || windows.get(binding.registration.ownerId) !== binding.registration) return undefined
  return binding.context ?? getWindowFileAccess(binding.registration.ownerId)
}

export function requireWindowFileAccess(ownerId: number): WindowFileAccess {
  const context = getWindowFileAccess(ownerId)
  if (!context) throw new Error('Window file access is unavailable.')
  return context
}

export function isCurrentWindowFileAccess(context: WindowFileAccess, workspaceRoot: string, epoch: number): boolean {
  return getWindowFileAccess(context.ownerId) === context && context.getWorkspaceRoot() === workspaceRoot
    && context.authorizationEpoch === epoch
}

export function isWindowFileAccessValid(context: WindowFileAccess, workspaceRoot: string, epoch: number): boolean {
  const registration = windows.get(context.ownerId)
  return Boolean(registration && context.getWorkspaceRoot() === workspaceRoot && context.authorizationEpoch === epoch
    && (getWindowFileAccess(context.ownerId) === context
      || registration.scopes.get(normalizeWorkspaceProjectKey(workspaceRoot)) === context))
}

export function invalidateForegroundFileAccess(ownerId: number): void {
  const context = getWindowFileAccess(ownerId)
  if (context) context.authorizationEpoch++
}

export function revokeWindowFileAccess(ownerId: number): void {
  const registration = windows.get(ownerId)
  const context = getWindowFileAccess(ownerId)
  if (!registration || !context) return
  context.authorizedExternalFiles.clear()
  context.authorizationEpoch++
  const scoped = registration.scopes.get(normalizeWorkspaceProjectKey(context.getWorkspaceRoot()))
  if (scoped) scoped.authorizationEpoch++
}

export function revokeAllWindowFileAccess(ownerId: number): void {
  const registration = windows.get(ownerId)
  if (!registration) return
  if (registration.foreground) registration.foreground.authorizationEpoch++
  for (const context of registration.scopes.values()) context.authorizationEpoch++
  for (const grants of registration.grants.values()) grants.clear()
}
