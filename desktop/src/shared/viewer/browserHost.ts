export interface BrowserHostCursor {
  visible: boolean
  x?: number
  y?: number
  moveSequence?: number
  animate?: boolean
}

export interface BrowserHostDescriptor {
  tabId: string
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
  | { type: 'pointer-down'; tabId: string }

export interface BrowserHostApi {
  list(): Promise<BrowserHostDescriptor[]>
  bind(params: { tabId: string; webContentsId: number }): Promise<void>
  failed(params: { tabId: string; message: string }): Promise<void>
  cursorArrived(params: { tabId: string; moveSequence: number }): Promise<void>
  onEvent(callback: (event: BrowserHostEvent) => void): () => void
}
