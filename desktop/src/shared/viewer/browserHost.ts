export interface BrowserHostCursor {
  visible: boolean
  x?: number
  y?: number
  moveSequence?: number
  animate?: boolean
}

export const BROWSER_GUEST_BOOTSTRAP_PREFIX = 'about:blank#dotcraft-browser-host='

export interface BrowserHostDescriptor {
  tabId: string
  bootstrapToken: string
  partition: string
  visible: boolean
  automation: boolean
  viewport?: { width: number; height: number }
  captureSurfaceSize?: { width: number; height: number }
  bounds: { x: number; y: number; width: number; height: number }
  cursor?: BrowserHostCursor
}

export type BrowserHostEvent =
  | { type: 'update'; host: BrowserHostDescriptor }
  | { type: 'remove'; tabId: string }
  | { type: 'pointer-down'; tabId: string; source: 'user' | 'automation' }

export interface BrowserHostApi {
  list(): Promise<BrowserHostDescriptor[]>
  cursorArrived(params: { tabId: string; moveSequence: number }): Promise<void>
  onEvent(callback: (event: BrowserHostEvent) => void): () => void
}
