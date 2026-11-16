import { useCallback, useEffect, useRef, useState } from 'react'
import type { DesktopPluginScreenSession } from '@dotcraft/plugin'
import { FRAME_STALL_MS, type ScreenViewFramePayload, type ScreenViewStatus } from './screenViewTypes'

export interface ScreenStream {
  setCanvas: (node: HTMLCanvasElement | null) => void
  state: ScreenViewStatus
  aspect: number | null
}

async function paint(canvas: HTMLCanvasElement, payload: ScreenViewFramePayload, stale: () => boolean): Promise<void> {
  const bitmap = await createImageBitmap(new Blob([payload.jpeg], { type: 'image/jpeg' }))
  try {
    if (stale()) return
    if (canvas.width !== payload.width || canvas.height !== payload.height) {
      canvas.width = payload.width
      canvas.height = payload.height
    }
    canvas.getContext('2d')?.drawImage(bitmap, 0, 0)
  } finally {
    bitmap.close()
  }
}

export function useScreenStream(session: DesktopPluginScreenSession | null, status: ScreenViewStatus): ScreenStream {
  const canvasRef = useRef<HTMLCanvasElement | null>(null)
  const lastFrameRef = useRef<ScreenViewFramePayload | null>(null)
  const [aspect, setAspect] = useState<number | null>(null)
  const [stalled, setStalled] = useState(false)

  const setCanvas = useCallback((node: HTMLCanvasElement | null) => {
    canvasRef.current = node
    const last = lastFrameRef.current
    if (!node || !last) return
    const stale = (): boolean => lastFrameRef.current !== last || canvasRef.current !== node
    void paint(node, last, stale).catch(() => {})
  }, [])

  useEffect(() => {
    setAspect(null)
    setStalled(false)
    lastFrameRef.current = null
    if (!session) return
    let disposed = false
    let decoding = false
    let pending: ScreenViewFramePayload | null = null
    let stallTimer: ReturnType<typeof setTimeout> | null = null
    const armStallTimer = (): void => {
      if (stallTimer) clearTimeout(stallTimer)
      stallTimer = setTimeout(() => setStalled(true), FRAME_STALL_MS)
    }
    const decode = async (payload: ScreenViewFramePayload): Promise<void> => {
      decoding = true
      try {
        const canvas = canvasRef.current
        if (canvas) await paint(canvas, payload, () => disposed || canvasRef.current !== canvas)
        if (!disposed) {
          lastFrameRef.current = payload
          setAspect(payload.width / payload.height)
          setStalled(false)
          armStallTimer()
        }
      } catch {
        // A broken frame is superseded by the next capture.
      } finally {
        decoding = false
        if (!disposed) session.ack(payload.sequence)
        const next = pending
        pending = null
        if (next && !disposed) void decode(next)
      }
    }
    const unsubscribe = session.onFrame((payload) => {
      if (decoding) pending = payload
      else void decode(payload)
    })
    return () => {
      disposed = true
      pending = null
      unsubscribe()
      if (stallTimer) clearTimeout(stallTimer)
    }
  }, [session])

  const state: ScreenViewStatus = status.kind !== 'connecting'
    ? status
    : aspect !== null
      ? { kind: stalled ? 'stalled' : 'live' }
      : { kind: 'connecting' }
  return { setCanvas, state, aspect }
}
