import type { BrowserWindow } from 'electron'
import { BrowserUseManager, browserUseManager, type BrowserUseImageResult } from './browserUseManager'
import { CHROME_BACKEND_PROFILE } from './browserUseBackendProfile'
import { ChromeBrowserHost } from './chrome/chromeBrowserHost'

type PrepareParams = Parameters<BrowserUseManager['prepareNodeRepl']>[1]

export class BrowserUseManagerSet {
  constructor(private readonly managers: BrowserUseManager[]) {}

  async prepareNodeRepl(owner: BrowserWindow, params: PrepareParams): Promise<{
    display: (imageLike: unknown) => Promise<void>
    collect: () => { images: BrowserUseImageResult[]; logs: string[] }
  }> {
    const runtimes = await Promise.all(this.managers.map((manager) => manager.prepareNodeRepl(owner, params)))
    return {
      display: runtimes[0].display,
      collect: () => {
        const collected = runtimes.map((runtime) => runtime.collect())
        return {
          images: collected.flatMap((item) => item.images),
          logs: collected.flatMap((item) => item.logs)
        }
      }
    }
  }

  abortEvaluation(threadId: string, evaluationId?: string): { ok: boolean } {
    const results = this.managers.map((manager) => manager.abortEvaluation(threadId, evaluationId))
    return { ok: results.some((result) => result.ok) }
  }

  reset(threadId: string, options?: { preserveTabs?: boolean }): { ok: boolean } {
    const results = this.managers.map((manager) => manager.reset(threadId, options))
    return { ok: results.some((result) => result.ok) }
  }

  handleTurnNotification(method: string, params: unknown, workspacePath?: string): ReturnType<BrowserUseManager['handleTurnNotification']> {
    let turn: ReturnType<BrowserUseManager['handleTurnNotification']>
    for (const manager of this.managers) turn = manager.handleTurnNotification(method, params, workspacePath) ?? turn
    return turn
  }

  async handleBrowserUseElicitation(threadId: string, request: unknown): Promise<Record<string, unknown>> {
    return await this.managers[0].handleBrowserUseElicitation(threadId, request)
  }
}

export const chromeBrowserUseManager = new BrowserUseManager(new ChromeBrowserHost(), {}, CHROME_BACKEND_PROFILE)
export const browserUseManagers = new BrowserUseManagerSet([browserUseManager, chromeBrowserUseManager])
