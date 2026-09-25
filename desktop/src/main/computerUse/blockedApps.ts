import { win32 } from 'node:path'

const OWN_EXECUTABLES = new Set(['dotcraft', 'cua-driver'])

export function isBlockedApp(appId: string, exePath?: string, ownExecutable = process.execPath): boolean {
  const path = exePath ?? (appId.includes('!') ? undefined : appId)
  if (!path) return false
  const lower = path.toLowerCase()
  return lower === ownExecutable.toLowerCase()
    || OWN_EXECUTABLES.has(win32.basename(lower).replace(/\.exe$/, ''))
}
