export const SCREEN_VIEW_CAPABILITY = 'screen-v1'

export const SCREEN_FRAME_HEADER_BYTES = 16
export const MAXIMUM_FRAME_BYTES = 2 * 1024 * 1024
export const MAXIMUM_FRAME_PIXELS = 8192 * 8192

export const SCREEN_VIEW_FPS = 8
export const SCREEN_VIEW_QUALITY = 82

export const SCREEN_VIEW_WIDTH_MIN = 320
export const SCREEN_VIEW_WIDTH_MAX = 3840
/** Frame widths move in steps so a slow drag does not re-encode on every pixel. */
const SCREEN_VIEW_WIDTH_STEP = 64

export const SCREEN_VIEW_DETAIL_MAX = 200

/** One undelivered frame per view; the credit resets if the renderer never acks. */
export const FRAME_ACK_TIMEOUT_MS = 1500
export const FRAME_STALL_MS = 3000

export const SCREEN_VIEW_OPEN_CHANNEL = 'screenView:open'
export const SCREEN_VIEW_CLOSE_CHANNEL = 'screenView:close'
export const SCREEN_VIEW_TUNE_CHANNEL = 'screenView:tune'
export const SCREEN_VIEW_ACK_CHANNEL = 'screenView:ack'
export const SCREEN_VIEW_FRAME_CHANNEL = 'screenView:frame'
export const SCREEN_VIEW_STATE_CHANNEL = 'screenView:state'

export type ScreenViewMode = 'dock' | 'theater'

export type ScreenUnavailableReason =
  | 'noCaptureBackend'
  | 'noInteractiveSession'
  | 'noDisplayServer'
  | 'captureFailed'
  | 'hubRejected'

const UNAVAILABLE_REASONS: readonly ScreenUnavailableReason[] = [
  'noCaptureBackend',
  'noInteractiveSession',
  'noDisplayServer',
  'captureFailed',
  'hubRejected'
]

/** A reason a retry cannot resolve: the view stops reconnecting and keeps the reason on screen. */
const TERMINAL_REASONS: readonly ScreenUnavailableReason[] = [
  'noCaptureBackend',
  'noDisplayServer',
  'hubRejected'
]

export type ScreenViewStateKind =
  | 'connecting'
  | 'live'
  | 'stalled'
  | 'reconnecting'
  | 'paused'
  | 'offline'
  | 'unavailable'

export interface ScreenViewStatus {
  kind: ScreenViewStateKind
  reason?: ScreenUnavailableReason
  /** The host's diagnostic line for `unavailable`, shown only in the tooltip. */
  detail?: string
  needsAuthorization?: boolean
}

export interface ScreenViewDockPosition {
  x: number
  y: number
}

/** viewer → host demand. Sent bare, with no envelope. */
export interface ScreenViewControl {
  watchers: number
  fps: number
  maxWidth: number
  quality: number
}

/** host → viewer capture state. Sent bare, with no envelope. */
export interface ScreenViewCapability {
  enabled: boolean
  unavailableReason?: ScreenUnavailableReason | null
  detail?: string
}

export interface ScreenFrame {
  sequence: number
  width: number
  height: number
  capturedAtUnixMs: number
  jpeg: Uint8Array
}

export interface ScreenViewOpenRequest {
  viewId: string
  peerId: string
  maxWidth: number
}

export interface ScreenViewTuneRequest {
  viewId: string
  maxWidth: number
}

export interface ScreenViewCloseRequest {
  viewId: string
}

export interface ScreenViewAckRequest {
  viewId: string
  sequence: number
}

export interface ScreenViewFramePayload {
  viewId: string
  sequence: number
  width: number
  height: number
  capturedAtUnixMs: number
  /** A standalone buffer: `ws` hands out views into a pooled buffer. */
  jpeg: ArrayBuffer
}

export interface ScreenViewStatePayload {
  viewId: string
  status: ScreenViewStatus
}

export function isTerminalUnavailableReason(reason: ScreenUnavailableReason): boolean {
  return TERMINAL_REASONS.includes(reason)
}

export function requestedFrameWidth(cssWidth: number, devicePixelRatio: number): number {
  const stepped = Math.ceil((cssWidth * devicePixelRatio) / SCREEN_VIEW_WIDTH_STEP) * SCREEN_VIEW_WIDTH_STEP
  return Math.min(SCREEN_VIEW_WIDTH_MAX, Math.max(SCREEN_VIEW_WIDTH_MIN, stepped))
}

export function screenViewControl(watchers: number, maxWidth: number): ScreenViewControl {
  return {
    watchers,
    fps: SCREEN_VIEW_FPS,
    maxWidth,
    quality: SCREEN_VIEW_QUALITY
  }
}

export function parseScreenViewCapability(text: string): ScreenViewCapability | null {
  let parsed: unknown
  try {
    parsed = JSON.parse(text)
  } catch {
    return null
  }
  if (parsed == null || typeof parsed !== 'object' || Array.isArray(parsed)) return null
  const record = parsed as { enabled?: unknown; unavailableReason?: unknown; detail?: unknown }
  if (typeof record.enabled !== 'boolean') return null
  const reason = typeof record.unavailableReason === 'string'
    ? UNAVAILABLE_REASONS.find((candidate) => candidate === record.unavailableReason)
    : undefined
  const detail = typeof record.detail === 'string'
    ? record.detail.trim().slice(0, SCREEN_VIEW_DETAIL_MAX)
    : ''
  return {
    enabled: record.enabled,
    ...(reason ? { unavailableReason: reason } : {}),
    ...(detail ? { detail } : {})
  }
}

export function readScreenFrame(bytes: Uint8Array): ScreenFrame | null {
  if (bytes.byteLength < SCREEN_FRAME_HEADER_BYTES) return null
  if (bytes.byteLength > MAXIMUM_FRAME_BYTES + SCREEN_FRAME_HEADER_BYTES) return null

  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength)
  const sequence = view.getUint32(0, true)
  const width = view.getUint16(4, true)
  const height = view.getUint16(6, true)
  if (width === 0 || height === 0) return null
  if (width * height > MAXIMUM_FRAME_PIXELS) return null

  const capturedAtUnixMs = Number(view.getBigInt64(8, true))
  return {
    sequence,
    width,
    height,
    capturedAtUnixMs,
    jpeg: bytes.subarray(SCREEN_FRAME_HEADER_BYTES)
  }
}
