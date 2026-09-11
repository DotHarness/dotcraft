import { useCallback, useEffect, useRef, useState } from 'react'
import {
  FRAME_STALL_MS,
  type ScreenViewFramePayload,
  type ScreenViewStatus
} from '../../../../shared/screenView'

export interface ScreenStream {
  setCanvas: (node: HTMLCanvasElement | null) => void
  state: ScreenViewStatus
  aspect: number | null
}

async function paint(
  canvas: HTMLCanvasElement,
  payload: ScreenViewFramePayload,
  stale?: () => boolean
): Promise<void> {
  const bitmap = await createImageBitmap(new Blob([payload.jpeg], { type: 'image/jpeg' }))
  try {
    if (stale?.()) return
    if (canvas.width !== payload.width || canvas.height !== payload.height) {
      canvas.width = payload.width
      canvas.height = payload.height
    }
    canvas.getContext('2d')?.drawImage(bitmap, 0, 0)
  } finally {
    bitmap.close()
  }
}

export function useScreenStream(viewId: string | null, status: ScreenViewStatus): ScreenStream {
  const canvasRef = useRef<HTMLCanvasElement | null>(null)
  const pendingRef = useRef<ScreenViewFramePayload | null>(null)
  const lastFrameRef = useRef<ScreenViewFramePayload | null>(null)
  const decodingRef = useRef(false)
  const stallTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  const [aspect, setAspect] = useState<number | null>(null)
  const [hasFrame, setHasFrame] = useState(false)
  const [stalled, setStalled] = useState(false)

  // Repaint the last frame when switching between dock and theater canvases; a frame
  // that arrives while this decodes supersedes it.
  const setCanvas = useCallback((node: HTMLCanvasElement | null) => {
    canvasRef.current = node
    const last = lastFrameRef.current
    if (!node || !last) return
    const stale = (): boolean => lastFrameRef.current !== last || canvasRef.current !== node
    void paint(node, last, stale).catch(() => {})
  }, [])

  useEffect(() => {
    if (!viewId) return
    setAspect(null)
    setHasFrame(false)
    setStalled(false)
    pendingRef.current = null
    lastFrameRef.current = null
    decodingRef.current = false

    let disposed = false

    const armStallTimer = (): void => {
      if (stallTimerRef.current) clearTimeout(stallTimerRef.current)
      stallTimerRef.current = setTimeout(() => setStalled(true), FRAME_STALL_MS)
    }

    const decode = async (payload: ScreenViewFramePayload): Promise<void> => {
      decodingRef.current = true
      try {
        const canvas = canvasRef.current
        if (canvas) await paint(canvas, payload)
        if (!disposed) {
          lastFrameRef.current = payload
          setAspect(payload.width / payload.height)
          setHasFrame(true)
          setStalled(false)
          armStallTimer()
        }
      } catch {
        // A frame that will not decode is superseded by the next capture.
      } finally {
        decodingRef.current = false
        // Main holds a credit of one: the ack is what lets the next frame through.
        if (!disposed) window.api.screenView.ack({ viewId, sequence: payload.sequence })
        const next = pendingRef.current
        pendingRef.current = null
        if (next && !disposed) void decode(next)
      }
    }

    const unsubscribe = window.api.screenView.onFrame((payload) => {
      if (payload.viewId !== viewId) return
      if (decodingRef.current) {
        pendingRef.current = payload
        return
      }
      void decode(payload)
    })

    return () => {
      disposed = true
      unsubscribe()
      if (stallTimerRef.current) {
        clearTimeout(stallTimerRef.current)
        stallTimerRef.current = null
      }
    }
  }, [viewId])

  const state: ScreenViewStatus = status.kind !== 'connecting'
    ? status
    : hasFrame
      ? { kind: stalled ? 'stalled' : 'live' }
      : { kind: 'connecting' }

  return { setCanvas, state, aspect }
}
