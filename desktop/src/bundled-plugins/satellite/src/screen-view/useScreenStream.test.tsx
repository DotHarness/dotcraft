import { act, renderHook } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { DesktopPluginScreenFrame, DesktopPluginScreenSession } from '@dotcraft/plugin'
import { useScreenStream } from './useScreenStream'

function frame(sequence: number): DesktopPluginScreenFrame {
  return { viewId: 'view-a', sequence, width: 640, height: 360, capturedAtUnixMs: 1, jpeg: new ArrayBuffer(1) }
}

function fixture() {
  let listener: ((frame: DesktopPluginScreenFrame) => void) | null = null
  const ack = vi.fn()
  const unsubscribe = vi.fn(() => { listener = null })
  const session: DesktopPluginScreenSession = {
    viewId: 'view-a', ack, close: vi.fn(), tune: vi.fn(async () => {}), onState: () => () => {},
    onFrame(callback) { listener = callback; return unsubscribe }
  }
  const emit = (sequence: number): void => { listener?.(frame(sequence)) }
  const drawImage = vi.fn()
  const canvas = document.createElement('canvas')
  vi.spyOn(canvas, 'getContext').mockReturnValue({ drawImage } as unknown as CanvasRenderingContext2D)
  let resolve!: (bitmap: ImageBitmap) => void
  const pending = new Promise<ImageBitmap>((yes) => { resolve = yes })
  const bitmap = { close: vi.fn() } as unknown as ImageBitmap
  const decode = vi.fn().mockReturnValueOnce(pending).mockResolvedValue(bitmap)
  vi.stubGlobal('createImageBitmap', decode)
  return { session, emit, ack, unsubscribe, canvas, drawImage, pending, resolve, bitmap, decode }
}

afterEach(() => {
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

describe('plugin screen frame decoding', () => {
  it('keeps only the newest pending frame and acknowledges completed decodes', async () => {
    const f = fixture()
    const hook = renderHook(() => useScreenStream(f.session, { kind: 'connecting' }))
    act(() => { hook.result.current.setCanvas(f.canvas); f.emit(1); f.emit(2); f.emit(3) })
    expect(f.decode).toHaveBeenCalledTimes(1)
    await act(async () => { f.resolve(f.bitmap); await f.pending })
    expect(f.decode).toHaveBeenCalledTimes(2)
    expect(f.ack.mock.calls).toEqual([[1], [3]])
    expect(hook.result.current.state).toEqual({ kind: 'live' })
    hook.unmount()
    expect(f.unsubscribe).toHaveBeenCalledTimes(1)
  })

  it('does not paint or acknowledge a decode after its session consumer unmounts', async () => {
    const f = fixture()
    const hook = renderHook(() => useScreenStream(f.session, { kind: 'connecting' }))
    act(() => { hook.result.current.setCanvas(f.canvas); f.emit(1) })
    hook.unmount()
    await act(async () => { f.resolve(f.bitmap); await f.pending })
    expect(f.drawImage).not.toHaveBeenCalled()
    expect(f.ack).not.toHaveBeenCalled()
    expect(f.bitmap.close).toHaveBeenCalledTimes(1)
  })
})
