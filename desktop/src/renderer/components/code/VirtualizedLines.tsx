// Offscreen rows are represented by spacers rather than absolute positioning, so
// a row wider than the viewport still extends the horizontal scroll area.
import {
  forwardRef,
  useCallback,
  useEffect,
  useImperativeHandle,
  useLayoutEffect,
  useRef,
  useState,
  type CSSProperties,
  type ReactNode
} from 'react'
import { useLineMetrics } from './useLineMetrics'

export type ScrollBlock = 'start' | 'center' | 'nearest'

export interface VirtualizedLinesHandle {
  scrollToIndex: (index: number, block?: ScrollBlock) => void
  scrollElement: () => HTMLElement | null
}

export interface RenderRangeArgs {
  start: number
  /** One past the last rendered row. */
  end: number
}

export interface VirtualizedLinesProps {
  count: number
  estimatedLineHeight: number
  /** True when rows can wrap and therefore differ in height. */
  variableHeight?: boolean
  overscan?: number
  className?: string
  style?: CSSProperties
  testId?: string
  renderRange: (args: RenderRangeArgs) => ReactNode
}

const DEFAULT_OVERSCAN = 12
const SCROLL_SETTLE_PASSES = 3

export const VirtualizedLines = forwardRef<VirtualizedLinesHandle, VirtualizedLinesProps>(
  function VirtualizedLines(props, ref) {
    const {
      count,
      estimatedLineHeight,
      variableHeight = false,
      overscan = DEFAULT_OVERSCAN,
      className,
      style,
      testId,
      renderRange
    } = props

    const viewportRef = useRef<HTMLDivElement | null>(null)
    const rangeRef = useRef({ start: 0, end: 0 })
    const [range, setRange] = useState({ start: 0, end: 0 })
    const [viewportHeight, setViewportHeight] = useState(0)

    const anchorScroll = useCallback((delta: number) => {
      const viewport = viewportRef.current
      if (viewport === null || delta === 0) return
      // Content above the viewport grew or shrank; move with it so visible rows stay put.
      viewport.scrollTop += delta
    }, [])

    const metrics = useLineMetrics({
      count,
      estimatedLineHeight,
      variableHeight,
      onShiftAbove: anchorScroll,
      firstVisibleIndex: () => rangeRef.current.start
    })
    const { offsets } = metrics

    const recompute = useCallback(() => {
      const viewport = viewportRef.current
      if (viewport === null || count === 0) {
        rangeRef.current = { start: 0, end: 0 }
        setRange({ start: 0, end: 0 })
        return
      }
      const top = viewport.scrollTop
      const height = viewport.clientHeight
      const first = Math.max(0, offsets.indexAtOffset(top) - overscan)
      const last = Math.min(count, offsets.indexAtOffset(top + height) + 1 + overscan)
      if (rangeRef.current.start === first && rangeRef.current.end === last) return
      rangeRef.current = { start: first, end: last }
      setRange({ start: first, end: last })
    }, [count, offsets, overscan])

    useLayoutEffect(() => { recompute() }, [recompute, metrics.version, viewportHeight])

    useEffect(() => {
      const viewport = viewportRef.current
      if (viewport === null) return
      const observer = new ResizeObserver(() => { setViewportHeight(viewport.clientHeight) })
      observer.observe(viewport)
      setViewportHeight(viewport.clientHeight)
      return () => { observer.disconnect() }
    }, [])

    useImperativeHandle(ref, () => ({
      scrollElement: () => viewportRef.current,
      scrollToIndex: (index: number, block: ScrollBlock = 'center') => {
        let pass = 0
        const step = (): void => {
          const viewport = viewportRef.current
          if (viewport === null) return
          const clamped = Math.max(0, Math.min(index, count - 1))
          const target = targetScrollTop(
            offsets.offsetOf(clamped),
            offsets.heightOf(clamped),
            viewport.scrollTop,
            viewport.clientHeight,
            block
          )
          if (target !== undefined) viewport.scrollTop = target
          recompute()
          // Rows above the target may still be estimates, so re-aim once measured.
          if (++pass < SCROLL_SETTLE_PASSES) requestAnimationFrame(step)
        }
        step()
      }
    }), [count, offsets, recompute])

    const topSpacer = offsets.offsetOf(range.start)
    const bottomSpacer = Math.max(0, offsets.totalHeight - offsets.offsetOf(range.end))

    return (
      <div
        ref={viewportRef}
        className={className}
        style={style}
        data-testid={testId}
        onScroll={recompute}
      >
        <div style={{ height: topSpacer }} aria-hidden />
        <div ref={metrics.observe}>
          {count > 0 && renderRange({ start: range.start, end: range.end })}
        </div>
        <div style={{ height: bottomSpacer }} aria-hidden />
      </div>
    )
  }
)

function targetScrollTop(
  offset: number,
  rowHeight: number,
  currentTop: number,
  viewportHeight: number,
  block: ScrollBlock
): number | undefined {
  if (block === 'start') return offset
  if (block === 'center') return Math.max(0, offset - Math.max(0, (viewportHeight - rowHeight) / 2))
  if (offset < currentTop) return offset
  if (offset + rowHeight > currentTop + viewportHeight) {
    return offset + rowHeight - viewportHeight
  }
  return undefined
}
