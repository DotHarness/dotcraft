import { BrowserUseBackendError } from './browserUseBackendServer'

export interface ViewportSize {
  width: number
  height: number
}

interface BrowserUseViewportHost<Tab> {
  send(tab: Tab, method: string, params?: Record<string, unknown>): Promise<unknown>
  present(tab: Tab, size: ViewportSize | undefined): void
}

interface TabViewport {
  size?: ViewportSize
  version: number
  chain: Promise<void>
}

const MIN_WIDTH = 240
const MIN_HEIGHT = 160
const MAX_SIZE = 4096

function clamp(value: number, min: number, max: number): number {
  return Math.round(Math.min(max, Math.max(min, value)))
}

export function normalizeViewportSize(width: unknown, height: unknown): ViewportSize {
  if (typeof width !== 'number' || typeof height !== 'number' || !Number.isFinite(width) || !Number.isFinite(height)) {
    throw BrowserUseBackendError.invalidArgument('Browser viewport requires numeric width and height.')
  }
  return { width: clamp(width, MIN_WIDTH, MAX_SIZE), height: clamp(height, MIN_HEIGHT, MAX_SIZE) }
}

export class BrowserUseViewports<Tab extends object> {
  private readonly states = new WeakMap<Tab, TabViewport>()

  constructor(private readonly host: BrowserUseViewportHost<Tab>) {}

  settled(tab: Tab): Promise<void> {
    return this.states.get(tab)?.chain ?? Promise.resolve()
  }

  set(tab: Tab, size: ViewportSize): Promise<void> {
    let state = this.states.get(tab)
    if (!state) this.states.set(tab, state = { version: 0, chain: Promise.resolve() })
    state.size = size
    this.host.present(tab, size)
    return this.sync(tab, state)
  }

  reset(tab: Tab): Promise<void> {
    const state = this.states.get(tab)
    if (!state?.size) return this.settled(tab)
    state.size = undefined
    this.host.present(tab, undefined)
    return this.sync(tab, state)
  }

  attached(tab: Tab): void {
    const state = this.states.get(tab)
    if (state?.size) this.sync(tab, state).catch(() => {})
  }

  release(tab: Tab): void {
    const state = this.states.get(tab)
    if (!state) return
    state.version += 1
    if (!state.size) return
    state.size = undefined
    this.host.present(tab, undefined)
  }

  private sync(tab: Tab, state: TabViewport): Promise<void> {
    const version = ++state.version
    const run = state.chain.then(async () => {
      if (version !== state.version) return
      const { size } = state
      if (size) {
        await this.host.send(tab, 'Emulation.setDeviceMetricsOverride', {
          deviceScaleFactor: 1, mobile: false, width: size.width, height: size.height
        })
      } else {
        await this.host.send(tab, 'Emulation.clearDeviceMetricsOverride')
      }
    })
    state.chain = run.catch(() => {})
    return run
  }
}
