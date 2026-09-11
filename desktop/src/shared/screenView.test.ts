import { describe, expect, it } from 'vitest'
import {
  MAXIMUM_FRAME_BYTES,
  SCREEN_FRAME_HEADER_BYTES,
  SCREEN_VIEW_DETAIL_MAX,
  parseScreenViewCapability,
  readScreenFrame,
  requestedFrameWidth
} from './screenView'

function frameBytes(options: {
  sequence?: number
  width?: number
  height?: number
  capturedAtUnixMs?: number
  jpeg?: number[]
  leadingPadding?: number
}): Uint8Array {
  const jpeg = options.jpeg ?? [0xff, 0xd8, 0xff, 0xd9]
  const padding = options.leadingPadding ?? 0
  const storage = new Uint8Array(padding + SCREEN_FRAME_HEADER_BYTES + jpeg.length)
  const frame = storage.subarray(padding)
  const view = new DataView(frame.buffer, frame.byteOffset, frame.byteLength)
  view.setUint32(0, options.sequence ?? 7, true)
  view.setUint16(4, options.width ?? 1920, true)
  view.setUint16(6, options.height ?? 1080, true)
  view.setBigInt64(8, BigInt(options.capturedAtUnixMs ?? 1_700_000_000_000), true)
  frame.set(jpeg, SCREEN_FRAME_HEADER_BYTES)
  return frame
}

describe('readScreenFrame', () => {
  it('reads the little-endian header and the JPEG that follows', () => {
    const frame = readScreenFrame(frameBytes({ sequence: 42, width: 1280, height: 720 }))
    expect(frame).not.toBeNull()
    expect(frame?.sequence).toBe(42)
    expect(frame?.width).toBe(1280)
    expect(frame?.height).toBe(720)
    expect(frame?.capturedAtUnixMs).toBe(1_700_000_000_000)
    expect(Array.from(frame?.jpeg ?? [])).toEqual([0xff, 0xd8, 0xff, 0xd9])
  })

  it('honors a non-zero buffer offset', () => {
    const frame = readScreenFrame(frameBytes({ sequence: 9, leadingPadding: 13 }))
    expect(frame?.sequence).toBe(9)
    expect(frame?.width).toBe(1920)
    expect(Array.from(frame?.jpeg ?? [])).toEqual([0xff, 0xd8, 0xff, 0xd9])
  })

  it('rejects a buffer shorter than the header', () => {
    expect(readScreenFrame(new Uint8Array(SCREEN_FRAME_HEADER_BYTES - 1))).toBeNull()
  })

  it('rejects a zero width or height', () => {
    expect(readScreenFrame(frameBytes({ width: 0 }))).toBeNull()
    expect(readScreenFrame(frameBytes({ height: 0 }))).toBeNull()
  })

  it('rejects a header past the pixel ceiling', () => {
    expect(readScreenFrame(frameBytes({ width: 65535, height: 65535 }))).toBeNull()
  })

  it('rejects a message past the frame byte ceiling', () => {
    const oversized = new Uint8Array(MAXIMUM_FRAME_BYTES + SCREEN_FRAME_HEADER_BYTES + 1)
    const view = new DataView(oversized.buffer)
    view.setUint16(4, 640, true)
    view.setUint16(6, 480, true)
    expect(readScreenFrame(oversized)).toBeNull()
  })
})

describe('parseScreenViewCapability', () => {
  it('reads a bare capability record', () => {
    expect(parseScreenViewCapability('{"enabled":true,"unavailableReason":null}')).toEqual({
      enabled: true
    })
    expect(parseScreenViewCapability('{"enabled":false,"unavailableReason":"captureFailed"}')).toEqual({
      enabled: false,
      unavailableReason: 'captureFailed'
    })
  })

  it('drops an unknown reason and refuses a record without `enabled`', () => {
    expect(parseScreenViewCapability('{"enabled":false,"unavailableReason":"whatever"}')).toEqual({
      enabled: false
    })
    expect(parseScreenViewCapability('{"unavailableReason":"captureFailed"}')).toBeNull()
    expect(parseScreenViewCapability('not json')).toBeNull()
  })

  it('keeps the host detail, bounded, and omits an empty one', () => {
    expect(
      parseScreenViewCapability('{"enabled":false,"unavailableReason":"captureFailed","detail":"BitBlt failed (Win32 6)"}')
    ).toEqual({ enabled: false, unavailableReason: 'captureFailed', detail: 'BitBlt failed (Win32 6)' })

    const long = JSON.stringify({ enabled: false, unavailableReason: 'captureFailed', detail: 'x'.repeat(500) })
    expect(parseScreenViewCapability(long)?.detail).toHaveLength(SCREEN_VIEW_DETAIL_MAX)

    expect(parseScreenViewCapability('{"enabled":true,"detail":"   "}')).toEqual({ enabled: true })
    expect(parseScreenViewCapability('{"enabled":true,"detail":42}')).toEqual({ enabled: true })
  })
})

describe('requestedFrameWidth', () => {
  it('rounds the device-pixel width up to the 64 px step', () => {
    expect(requestedFrameWidth(360, 1)).toBe(384)
    expect(requestedFrameWidth(360, 2)).toBe(768)
    expect(requestedFrameWidth(384, 1)).toBe(384)
    expect(requestedFrameWidth(385, 1)).toBe(448)
    expect(requestedFrameWidth(500, 1.5)).toBe(768)
  })

  it('clamps to the host limits', () => {
    expect(requestedFrameWidth(168, 1)).toBe(320)
    expect(requestedFrameWidth(0, 1)).toBe(320)
    expect(requestedFrameWidth(2600, 2)).toBe(3840)
  })
})
