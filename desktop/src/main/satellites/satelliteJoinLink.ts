import { spawn } from 'child_process'
import type { BrowserWindow } from 'electron'
import {
  findSatelliteJoinDeepLink,
  findWorkspaceOpenDeepLink,
  type SatelliteJoinDeepLink
} from '../../shared/desktopDeepLink'
import type { SatelliteJoinLink } from '../../shared/satellites'
import { NO_WORKSPACE_ARG } from '../workspaceArgs'
import { readSatelliteExecutablePath } from './satelliteRuntime'

/**
 * Desktop owns the `dotcraft://` registration but never completes a pairing: the
 * consent window belongs to the Satellite runtime that holds the credentials.
 */

type ResolveWindow = () => BrowserWindow | null

let pending: SatelliteJoinLink | null = null

function send(link: SatelliteJoinLink, resolveWindow: ResolveWindow): void {
  const win = resolveWindow()
  if (!win || win.isDestroyed()) {
    pending = link
    return
  }
  pending = null
  const deliver = (): void => {
    if (!win.isDestroyed()) win.webContents.send('satellites:join-link', link)
  }
  if (win.webContents.isLoading()) {
    win.webContents.once('did-finish-load', deliver)
  } else {
    deliver()
  }
}

/** Replays a link that arrived before any window could receive it. */
export function flushPendingSatelliteJoinLink(resolveWindow: ResolveWindow): void {
  if (pending) send(pending, resolveWindow)
}

export async function forwardSatelliteJoinDeepLink(
  link: SatelliteJoinDeepLink,
  resolveWindow: ResolveWindow
): Promise<boolean> {
  const executable = await readSatelliteExecutablePath()
  let forwarded = false
  if (executable) {
    try {
      const child = spawn(executable, ['--url', link.link], {
        detached: true,
        stdio: 'ignore',
        windowsHide: true
      })
      child.unref()
      forwarded = true
    } catch (error) {
      console.warn('[desktop] failed to forward satellite join link', error)
    }
  }
  send({ url: link.link, forwarded }, resolveWindow)
  return forwarded
}

/** Launch arguments that mean the user asked for a Desktop window, not just a link. */
const WINDOW_INTENT_ARGS: readonly string[] = ['--tray', '--workspace', '--remote', NO_WORKSPACE_ARG]

/**
 * A protocol click launches a whole process on Windows; once Satellite has the link
 * that process has nothing to show. A failed forward still opens a window, to explain.
 */
export function shouldQuitAfterSatelliteJoin(
  argv: readonly string[],
  forwarded: boolean
): boolean {
  if (!forwarded || !findSatelliteJoinDeepLink(argv)) return false
  if (findWorkspaceOpenDeepLink(argv)) return false
  return !argv.some((arg) => WINDOW_INTENT_ARGS.includes(arg))
}
