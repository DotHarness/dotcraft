import { app, type WebContents } from 'electron'
import { createRequire } from 'node:module'
import { join } from 'node:path'
import { BrowserUseBackendError } from './browserUseBackendServer'

interface FocusHook {
  setIsolation(processId: number, routingId: number, enabled: boolean): boolean
  unregister(processId: number, routingId: number): boolean
}

interface Registration {
  identity?: { processId: number; routingId: number }
}

const require = createRequire(import.meta.url)

function loadHook(): FocusHook | undefined {
  if (process.platform !== 'win32' || process.arch !== 'x64') return undefined
  const root = app.isPackaged ? process.resourcesPath : join(app.getAppPath(), 'resources')
  return require(join(root, 'native', 'browser-focus.node')) as FocusHook
}

export class BrowserFocusIsolation {
  private hook?: FocusHook
  private registrations = new WeakMap<WebContents, Registration>()

  constructor(private readonly load: () => FocusHook | undefined = loadHook) {}

  arm(page: WebContents): void {
    try {
      this.hook ??= this.load()
      if (!this.hook) return
      const { processId, routingId } = page.mainFrame
      this.hook.setIsolation(processId, routingId, true)
      let registration = this.registrations.get(page)
      if (!registration) {
        registration = {}
        this.registrations.set(page, registration)
        page.once('destroyed', () => this.release(page))
      }
      registration.identity = { processId, routingId }
    } catch (cause) {
      const error = new BrowserUseBackendError(
        'IabBackendUnavailable: Windows browser input isolation is unavailable. Rebuild the Desktop native module for the pinned Electron version.',
        -32001
      )
      error.cause = cause
      throw error
    }
  }

  release(page: WebContents): void {
    const registration = this.registrations.get(page)
    if (!registration?.identity) return
    const { processId, routingId } = registration.identity
    this.hook!.unregister(processId, routingId)
    registration.identity = undefined
  }
}

export const browserFocusIsolation = new BrowserFocusIsolation()
