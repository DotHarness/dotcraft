export interface BrowserDownloadRecord {
  id: string
  tabId: string
  threadId?: string
  filename: string
  url: string
  receivedBytes: number
  totalBytes: number
  state: 'progressing' | 'completed' | 'cancelled' | 'interrupted'
  path: string
  startedAt: number
}

export interface BrowserFindState {
  query: string
  current: number
  total: number
  open: boolean
}

export type BrowserSelectionKind = 'text' | 'element' | 'region'

export interface BrowserSelectRequest {
  tabId: string
  kind: BrowserSelectionKind
  accent?: string
}

export interface BrowserPageReference {
  id: string
  tabId: string
  threadId?: string
  kind: BrowserSelectionKind
  url: string
  title: string
  text: string
  imageDataUrl?: string
  previewDataUrl?: string
  bounds?: { x: number; y: number; width: number; height: number }
}

export type BrowserFeedbackEvent =
  | { type: 'error'; tabId: string; message: string }
  | { type: 'downloads'; records: BrowserDownloadRecord[] }
  | { type: 'find'; tabId: string; state: BrowserFindState }
  | { type: 'zoom'; tabId: string; percent: number }
  | { type: 'selection'; reference: BrowserPageReference }
