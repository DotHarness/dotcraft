export interface BrowserHostDescriptor {
  tabId: string
  partition: string
  visible: boolean
  automation: boolean
  bounds: { x: number; y: number; width: number; height: number }
}

export type BrowserHostEvent =
  | { type: 'update'; host: BrowserHostDescriptor }
  | { type: 'remove'; tabId: string }
  | { type: 'pointer-down'; tabId: string }

export interface BrowserHostApi {
  list(): Promise<BrowserHostDescriptor[]>
  bind(params: { tabId: string; webContentsId: number }): Promise<void>
  failed(params: { tabId: string; message: string }): Promise<void>
  onEvent(callback: (event: BrowserHostEvent) => void): () => void
}
