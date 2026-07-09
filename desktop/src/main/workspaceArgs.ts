import { existsSync } from 'fs'
import type { AppSettings } from './settings'
import { findWorkspaceOpenDeepLink } from './desktopDeepLink'

export const NO_WORKSPACE_ARG = '--no-workspace'

export function hasNoWorkspaceArg(argv: readonly string[] = process.argv): boolean {
  return argv.includes(NO_WORKSPACE_ARG)
}

export function hasRemoteEndpointArg(argv: readonly string[] = process.argv): boolean {
  const remoteIdx = argv.indexOf('--remote')
  return remoteIdx !== -1 && Boolean(argv[remoteIdx + 1])
}

function resolveExplicitWorkspacePathFromArgs(argv: readonly string[]): string | null {
  const workspaceOpen = findWorkspaceOpenDeepLink(argv)
  if (workspaceOpen) {
    return workspaceOpen.workspacePath
  }

  const argIdx = argv.indexOf('--workspace')
  if (argIdx !== -1 && argv[argIdx + 1]) {
    return argv[argIdx + 1]
  }

  return null
}

export function resolveWorkspacePathFromArgs(
  settings: AppSettings,
  argv: readonly string[] = process.argv,
  pathExists: (path: string) => boolean = existsSync
): string | null {
  const explicitWorkspacePath = resolveExplicitWorkspacePathFromArgs(argv)
  if (explicitWorkspacePath) return explicitWorkspacePath

  if (hasNoWorkspaceArg(argv)) {
    return null
  }

  if (settings.lastWorkspacePath && pathExists(settings.lastWorkspacePath)) {
    return settings.lastWorkspacePath
  }

  return null
}

export function resolveStartupWorkspacePath(
  settings: AppSettings,
  argv: readonly string[] = process.argv,
  pathExists: (path: string) => boolean = existsSync,
  defaultChatWorkspacePath = '',
  connectionMode: 'local' | 'remote' = 'local'
): string | null {
  const explicitWorkspacePath = resolveExplicitWorkspacePathFromArgs(argv)
  if (explicitWorkspacePath) return explicitWorkspacePath

  if (hasNoWorkspaceArg(argv)) {
    return null
  }

  if (
    connectionMode === 'local' &&
    !hasRemoteEndpointArg(argv) &&
    settings.lastForegroundEntry === 'chats' &&
    defaultChatWorkspacePath.trim()
  ) {
    return defaultChatWorkspacePath
  }

  if (settings.lastForegroundEntry === 'welcome') {
    return null
  }

  if (settings.lastWorkspacePath && pathExists(settings.lastWorkspacePath)) {
    return settings.lastWorkspacePath
  }

  return null
}
