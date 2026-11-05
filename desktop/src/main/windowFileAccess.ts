export interface WindowFileAccessOptions {
  getWorkspaceRoot(): string
  getSecondaryRoots?(workspaceRoot: string): readonly string[]
  getChatFolderRoots?(): readonly string[]
}

export interface WindowFileAccess extends WindowFileAccessOptions {
  readonly ownerId: number
  readonly authorizedExternalFiles: Set<string>
  readonly sessions: Set<Electron.Session>
  authorizationRoot: string
  authorizationEpoch: number
}

const windows = new Map<number, WindowFileAccess>()
const sessions = new WeakMap<Electron.Session, WindowFileAccess>()

export function registerWindowFileAccess(ownerId: number, options: WindowFileAccessOptions): () => void {
  if (windows.has(ownerId)) throw new Error('Window file access is already registered.')
  const context: WindowFileAccess = {
    ...options, ownerId, authorizationRoot: options.getWorkspaceRoot(), authorizationEpoch: 0,
    authorizedExternalFiles: new Set(), sessions: new Set()
  }
  windows.set(ownerId, context)
  return () => {
    if (windows.get(ownerId) !== context) return
    windows.delete(ownerId)
    context.authorizedExternalFiles.clear()
    for (const targetSession of context.sessions) sessions.delete(targetSession)
    context.sessions.clear()
  }
}

export function getWindowFileAccess(ownerId: number | undefined): WindowFileAccess | undefined {
  if (ownerId === undefined) return undefined
  const context = windows.get(ownerId)
  if (context && context.authorizationRoot !== context.getWorkspaceRoot()) {
    context.authorizedExternalFiles.clear()
    context.authorizationEpoch++
    context.authorizationRoot = context.getWorkspaceRoot()
  }
  return context
}

export function bindWindowFileSession(targetSession: Electron.Session, ownerId: number): void {
  const context = getWindowFileAccess(ownerId)
  if (!context) throw new Error('Window file access is not registered.')
  const previous = sessions.get(targetSession)
  if (previous && previous !== context) throw new Error('A file-serving session cannot be shared by windows.')
  sessions.set(targetSession, context)
  context.sessions.add(targetSession)
}

export function windowFileAccessForSession(targetSession: Electron.Session): WindowFileAccess | undefined {
  const context = sessions.get(targetSession)
  return context && getWindowFileAccess(context.ownerId) === context ? context : undefined
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
