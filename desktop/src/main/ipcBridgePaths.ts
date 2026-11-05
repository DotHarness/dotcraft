import { promises as fs } from 'fs'
import * as path from 'path'
import { translate, type AppLocale } from '../shared/locales'
import { sameWorkspaceProjectKey } from '../shared/workspaceProjectKey'
import { chatFolderFor } from './chatFolderAccess'
import { runGitCommand } from './gitCommand'
import { viewerRootsFor } from './viewerFileProtocol'

function isSameOrInsidePath(candidatePath: string, parentPath: string): boolean {
  const relativePath = path.relative(parentPath, candidatePath)
  return relativePath === '' || (!relativePath.startsWith('..') && !path.isAbsolute(relativePath))
}

/** Rejects the characters and trailing dots/spaces that are invalid in Windows path segments. */
export function sanitizeProjectFolderName(name: string): string {
  const cleaned = Array.from(name)
    .map((ch) => (ch.charCodeAt(0) < 0x20 || '<>:"/\\|?*'.includes(ch) ? ' ' : ch))
    .join('')
    .replace(/\s+/g, ' ')
    .trim()
    .replace(/[. ]+$/g, '')
    .trim()
  return cleaned || 'New project'
}

function toGitRelativeWorkspacePath(filePath: string, workspacePath: string, locale: AppLocale): string | null {
  if (typeof filePath !== 'string' || filePath.trim() === '') return null
  const wsResolved = path.resolve(workspacePath)
  const resolved = path.isAbsolute(filePath) ? path.resolve(filePath) : path.resolve(wsResolved, filePath)

  if (!isSameOrInsidePath(resolved, wsResolved)) {
    throw new Error(translate(locale, 'ipc.pathOutsideWorkspace', { path: filePath }))
  }

  const relativePath = path.relative(wsResolved, resolved)
  if (!relativePath || relativePath.startsWith('..') || path.isAbsolute(relativePath)) return null
  return relativePath.split(path.sep).join('/')
}

function parseGitStatusPorcelainZ(stdout: string): string[] {
  const paths: string[] = []
  const entries = stdout.split('\0').filter(Boolean)
  for (let i = 0; i < entries.length; i += 1) {
    const entry = entries[i]
    if (entry.length < 4) continue
    const status = entry.slice(0, 2)
    if (status === '!!') continue
    const filePath = entry.slice(3)
    if (!filePath) continue
    paths.push(filePath.replace(/\\/g, '/'))
    if (status[0] === 'R' || status[0] === 'C') {
      i += 1
    }
  }
  return paths
}

export async function resolveCommitFilePaths(
  workspacePath: string,
  files: string[],
  locale: AppLocale
): Promise<string[]> {
  const seen = new Set<string>()
  const requestedPaths: string[] = []
  for (const file of files) {
    const relativePath = toGitRelativeWorkspacePath(file, workspacePath, locale)
    if (!relativePath || seen.has(relativePath)) continue
    seen.add(relativePath)
    requestedPaths.push(relativePath)
  }
  if (requestedPaths.length === 0) return []

  const status = await runGitCommand(workspacePath, [
    'status',
    '--porcelain=v1',
    '-z',
    '--untracked-files=all',
    '--',
    ...requestedPaths
  ])
  const statusPaths = parseGitStatusPorcelainZ(status.stdout)
  return requestedPaths.filter((filePath) => statusPaths.includes(filePath))
}

export function assertPathWithinWorkspace(absPath: string, workspacePath: string, locale: AppLocale): string {
  const resolved = path.resolve(absPath)
  const wsResolved = path.resolve(workspacePath)
  if (!resolved.startsWith(wsResolved + path.sep) && resolved !== wsResolved) {
    throw new Error(translate(locale, 'ipc.pathOutsideWorkspace', { path: absPath }))
  }
  return resolved
}

export function assertPathWithinWorkspaceOrChatFolder(
  absPath: string,
  workspacePath: string,
  locale: AppLocale,
  ownerId: number
): string {
  return chatFolderFor(workspacePath, absPath, ownerId)
    ? path.resolve(absPath)
    : assertPathWithinWorkspace(absPath, workspacePath, locale)
}

export function assertPathWithinViewerRoots(
  absPath: string,
  workspacePath: string,
  locale: AppLocale,
  ownerId: number
): { resolved: string; root: string } {
  const resolved = path.resolve(absPath)
  let match: string | null = null
  for (const root of viewerRootsFor(workspacePath, resolved, ownerId)) {
    if (isSameOrInsidePath(resolved, root) && (!match || root.length > match.length)) match = root
  }
  if (!match) {
    throw new Error(translate(locale, 'ipc.pathOutsideWorkspace', { path: absPath }))
  }
  return { resolved, root: match }
}

export function assertGitWorkspacePath(requestedPath: string, workspacePath: string, locale: AppLocale): string {
  if (!workspacePath) {
    throw new Error(translate(locale, 'ipc.noWorkspaceOpen'))
  }
  if (typeof requestedPath !== 'string' || requestedPath.trim() === '') {
    throw new Error(translate(locale, 'ipc.workspacePathMismatch'))
  }

  const resolved = path.resolve(requestedPath)
  const workspaceResolved = path.resolve(workspacePath)
  if (resolved === workspaceResolved) return resolved

  const worktreesRoot = path.resolve(workspaceResolved, '.craft', 'worktrees')
  if (isSameOrInsidePath(resolved, worktreesRoot) && resolved !== worktreesRoot) {
    return resolved
  }

  throw new Error(translate(locale, 'ipc.workspacePathMismatch'))
}

export function assertGitInspectionPath(
  requestedPath: string,
  workspacePath: string,
  localProjectPaths: string[],
  locale: AppLocale,
  ownerId: number
): string {
  try {
    return assertGitWorkspacePath(requestedPath, workspacePath, locale)
  } catch {
    // Read-only inspection may target a known local project without
    // granting that path to commit, checkout, or branch-management handlers.
  }

  if (typeof requestedPath !== 'string' || requestedPath.trim() === '') {
    throw new Error(translate(locale, 'ipc.workspacePathMismatch'))
  }
  const resolved = path.resolve(requestedPath)
  if (isChatFolderRoot(workspacePath, resolved, ownerId)) return resolved
  for (const projectPath of localProjectPaths) {
    if (sameWorkspaceProjectKey(projectPath, requestedPath)) return path.resolve(projectPath)
    const worktreesRoot = path.resolve(projectPath, '.craft', 'worktrees')
    if (isSameOrInsidePath(resolved, worktreesRoot) && resolved !== worktreesRoot) return resolved
  }
  throw new Error(translate(locale, 'ipc.workspacePathMismatch'))
}

function isChatFolderRoot(workspacePath: string, resolvedPath: string, ownerId: number): boolean {
  const chatFolder = chatFolderFor(workspacePath, resolvedPath, ownerId)
  return chatFolder !== null && sameWorkspaceProjectKey(chatFolder, resolvedPath)
}

export function assertWorkspaceOrChatFolder(
  requestedPath: unknown,
  workspacePath: string,
  locale: AppLocale,
  ownerId: number
): string {
  const ws = path.resolve(workspacePath)
  const requested = typeof requestedPath === 'string' ? path.resolve(requestedPath) : ''
  if (requested === ws || (requested && isChatFolderRoot(workspacePath, requested, ownerId))) return requested
  throw new Error(translate(locale, 'ipc.workspacePathMismatch'))
}

export async function assertExistingLocalPath(targetPath: string): Promise<string> {
  if (typeof targetPath !== 'string' || targetPath.trim() === '') {
    throw new Error('Invalid local path')
  }
  const trimmed = targetPath.trim()
  const isWindowsAbsolutePath = /^[A-Za-z]:[\\/]/.test(trimmed)
  if (!isWindowsAbsolutePath && /^[A-Za-z][A-Za-z0-9+.-]*:/.test(trimmed)) {
    throw new Error('Local path must be an absolute filesystem path')
  }
  if (!path.isAbsolute(trimmed)) {
    throw new Error('Local path must be absolute')
  }

  const resolved = path.resolve(trimmed)
  await fs.stat(resolved)
  return resolved
}
