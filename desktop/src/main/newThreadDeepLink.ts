import { statSync } from 'fs'
import { isAbsolute } from 'path'
import type { NewThreadDeepLink } from '../shared/desktopDeepLink'

export interface NewThreadDeepLinkHost {
  canOpenLocalProject(): boolean
  isForegroundWorkspace(path: string): boolean
  switchWorkspace(path: string): Promise<void>
  openNewChat(prompt: string | undefined): void
}

export function isLocalDirectory(path: string): boolean {
  try {
    return isAbsolute(path) && statSync(path).isDirectory()
  } catch {
    return false
  }
}

export async function openNewThreadDeepLink(link: NewThreadDeepLink, host: NewThreadDeepLinkHost): Promise<void> {
  const path = link.workspacePath
  if (
    path &&
    host.canOpenLocalProject() &&
    !host.isForegroundWorkspace(path) &&
    isLocalDirectory(path)
  ) {
    await host.switchWorkspace(path)
  }
  host.openNewChat(link.prompt)
}
