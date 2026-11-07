import { BrowserWindow, type IpcMainInvokeEvent } from 'electron'
import type { PairedComputerEvent, PairedComputerResult } from '../../shared/pairedComputers'
import type { PairedComputersManager } from './pairedComputersManager'

type HandleSafe = (
  channel: string,
  listener: (event: IpcMainInvokeEvent, ...args: unknown[]) => unknown
) => void

export const PAIRED_COMPUTERS_EVENT_CHANNEL = 'pairedComputers:event'

export function broadcastPairedComputerEvent(event: PairedComputerEvent): void {
  for (const window of BrowserWindow.getAllWindows()) {
    if (!window.isDestroyed()) window.webContents.send(PAIRED_COMPUTERS_EVENT_CHANNEL, event)
  }
}

export interface PairedComputersIpcDeps {
  handleSafe: HandleSafe
  manager: PairedComputersManager
  openProject: (computerId: string, projectId: string) => Promise<void>
  leaveProject: (computerId: string, projectId?: string) => Promise<void>
}

async function settle(run: () => Promise<void>): Promise<PairedComputerResult<null>> {
  try {
    await run()
    return { ok: true, value: null }
  } catch (error) {
    return { ok: false, code: 'failed', message: error instanceof Error ? error.message : String(error) }
  }
}

export function registerPairedComputersHandlers(deps: PairedComputersIpcDeps): void {
  const { handleSafe, manager } = deps

  handleSafe('pairedComputers:list', () => manager.list())

  handleSafe('pairedComputers:refresh', () => manager.refresh())

  handleSafe('pairedComputers:pair', (_event, input) => manager.pair((input as { link: string }).link))

  handleSafe('pairedComputers:cancel-pairing', () => manager.cancelPairing())

  handleSafe('pairedComputers:remove', (_event, input) => {
    const { computerId } = input as { computerId: string }
    return settle(async () => {
      await deps.leaveProject(computerId)
      await manager.remove(computerId)
    })
  })

  handleSafe('pairedComputers:add-folder', (_event, input) => {
    const { computerId, path } = input as { computerId: string; path: string }
    return manager.addFolder(computerId, path)
  })

  handleSafe('pairedComputers:remove-project', (_event, input) => {
    const { computerId, projectId } = input as { computerId: string; projectId: string }
    return settle(async () => {
      await deps.leaveProject(computerId, projectId)
      manager.removeProject(computerId, projectId)
    })
  })

  handleSafe('pairedComputers:open-project', (_event, input) => {
    const { computerId, projectId } = input as { computerId: string; projectId: string }
    return settle(() => deps.openProject(computerId, projectId))
  })
}
