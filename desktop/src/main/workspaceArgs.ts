import { existsSync } from 'fs'
import type { AppSettings } from './settings'
import { findWorkspaceOpenDeepLink } from './desktopDeepLink'

export const NO_WORKSPACE_ARG = '--no-workspace'

export function resolveWorkspacePathFromArgs(
  settings: AppSettings,
  argv: readonly string[] = process.argv,
  pathExists: (path: string) => boolean = existsSync
): string | null {
  const workspaceOpen = findWorkspaceOpenDeepLink(argv)
  if (workspaceOpen) {
    return workspaceOpen.workspacePath
  }

  const argIdx = argv.indexOf('--workspace')
  if (argIdx !== -1 && argv[argIdx + 1]) {
    return argv[argIdx + 1]
  }

  if (argv.includes(NO_WORKSPACE_ARG)) {
    return null
  }

  if (settings.lastWorkspacePath && pathExists(settings.lastWorkspacePath)) {
    return settings.lastWorkspacePath
  }

  return null
}
