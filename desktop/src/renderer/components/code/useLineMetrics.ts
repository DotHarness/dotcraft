// Rows are located by attribute rather than by ref registration, so the surface
// that draws them does not have to thread refs back up.
import { useCallback, useEffect, useRef, useState } from 'react'
import { LineOffsets } from './lineOffsets'

export const ROW_MEASURE_ATTRIBUTE = 'data-vrow-measure'

export interface LineMetrics {
  offsets: LineOffsets
  version: number
}

export interface UseLineMetricsOptions {
  count: number
  estimatedLineHeight: number
  variableHeight: boolean
  /** Called with the height added above the viewport, so the caller can anchor the scroll. */
  onShiftAbove: (deltaPixels: number) => void
  firstVisibleIndex: () => number
}

export interface UseLineMetricsResult extends LineMetrics {
  observe: (container: HTMLElement | null) => void
}

export function useLineMetrics(options: UseLineMetricsOptions): UseLineMetricsResult {
  const { count, estimatedLineHeight, variableHeight, onShiftAbove, firstVisibleIndex } = options

  const stateRef = useRef<{ offsets: LineOffsets; measured: Map<number, number> } | undefined>(undefined)
  const [version, setVersion] = useState(0)
  const signature = `${count}:${estimatedLineHeight}:${variableHeight}`
  const signatureRef = useRef(signature)

  if (stateRef.current === undefined || signatureRef.current !== signature) {
    signatureRef.current = signature
    stateRef.current = { offsets: new LineOffsets(count, estimatedLineHeight), measured: new Map() }
  }
  const state = stateRef.current

  const containerRef = useRef<HTMLElement | null>(null)
  const observerRef = useRef<ResizeObserver | undefined>(undefined)
  const observedRef = useRef(new Set<Element>())
  const shiftRef = useRef(onShiftAbove)
  shiftRef.current = onShiftAbove
  const firstVisibleRef = useRef(firstVisibleIndex)
  firstVisibleRef.current = firstVisibleIndex

  const record = useCallback((index: number, height: number): boolean => {
    if (height <= 0) return false
    const previous = state.measured.get(index)
    // Sub-pixel churn from fractional layout would otherwise re-render forever.
    if (previous !== undefined && Math.abs(previous - height) < 0.5) return false
    state.measured.set(index, height)
    const delta = state.offsets.setHeight(index, height)
    if (delta === 0) return false
    if (index < firstVisibleRef.current()) shiftRef.current(delta)
    return true
  }, [state])

  const sync = useCallback(() => {
    const container = containerRef.current
    const observer = observerRef.current
    if (container === null || observer === undefined) return

    const rows = container.querySelectorAll<HTMLElement>(`[${ROW_MEASURE_ATTRIBUTE}]`)
    const present = new Set<Element>()
    let changed = false
    // A split row contributes two elements for one index; the taller one wins.
    const tallest = new Map<number, number>()

    for (const row of rows) {
      present.add(row)
      if (!observedRef.current.has(row)) {
        observer.observe(row)
        observedRef.current.add(row)
      }
      const index = Number(row.getAttribute(ROW_MEASURE_ATTRIBUTE))
      if (!Number.isInteger(index)) continue
      const height = row.getBoundingClientRect().height
      tallest.set(index, Math.max(tallest.get(index) ?? 0, height))
    }

    for (const [index, height] of tallest) {
      if (record(index, height)) changed = true
    }

    for (const row of observedRef.current) {
      if (present.has(row)) continue
      observer.unobserve(row)
      observedRef.current.delete(row)
    }

    if (changed) setVersion((value) => value + 1)
  }, [record])

  useEffect(() => {
    if (!variableHeight) return
    const observer = new ResizeObserver(() => { sync() })
    observerRef.current = observer
    sync()
    return () => {
      observer.disconnect()
      observerRef.current = undefined
      observedRef.current.clear()
    }
  }, [variableHeight, sync, signature])

  // Rows change on every scroll frame, so re-scan after each commit rather than
  // waiting for the observer to notice elements it has never seen.
  useEffect(() => {
    if (variableHeight) sync()
  })

  const observe = useCallback((container: HTMLElement | null) => {
    containerRef.current = container
    if (variableHeight) sync()
  }, [sync, variableHeight])

  return { offsets: state.offsets, version, observe }
}
