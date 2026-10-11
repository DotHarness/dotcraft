import { useLayoutEffect, useState, type CSSProperties, type RefObject } from 'react'
import { useConversationAsideInsets, type ConversationAsideInsets } from './conversationAside/ConversationAside'

const VISUALIZATION_WIDE_WIDTH = 1024

interface Span { left: number; width: number }
interface WideLayout { width: number; marginLeft: number; marginRight: number }

function visualizationFreeSpan(stream: Span, contentWidth: number, insets: ConversationAsideInsets): Span {
  const contentLeft = stream.left + (stream.width - contentWidth) / 2
  const left = Math.max(contentLeft, stream.left + insets.leading)
  const right = Math.min(contentLeft + contentWidth, stream.left + stream.width - insets.trailing)
  return { left, width: Math.max(0, right - left) }
}

function visualizationWideLayout(column: Span, free: Span, railWidth: number): WideLayout | null {
  const width = Math.min(VISUALIZATION_WIDE_WIDTH + railWidth, free.width)
  if (width <= column.width) return null
  const centered = column.left + (column.width - width) / 2
  const left = Math.min(Math.max(centered, free.left), free.left + free.width - width)
  const marginLeft = Math.round(left - column.left)
  return { width, marginLeft, marginRight: column.width - width - marginLeft }
}

export function useVisualizationWideLayout(
  containerRef: RefObject<HTMLElement | null>,
  wide: boolean,
  railWidth: number
): CSSProperties | undefined {
  const [layout, setLayout] = useState<WideLayout | null>(null)
  const insets = useConversationAsideInsets()

  useLayoutEffect(() => {
    const container = containerRef.current
    const parent = container?.parentElement
    const stream = container?.closest<HTMLElement>('[data-testid="message-stream"]')
    if (!wide || !parent || !stream) {
      setLayout(null)
      return
    }
    const measure = (): void => {
      const style = getComputedStyle(stream)
      const rect = stream.getBoundingClientRect()
      const contentWidth = stream.clientWidth - (parseFloat(style.paddingLeft) || 0) - (parseFloat(style.paddingRight) || 0)
      const columnRect = parent.getBoundingClientRect()
      const next = visualizationWideLayout(
        { left: columnRect.left, width: columnRect.width },
        visualizationFreeSpan({ left: rect.left, width: rect.width }, contentWidth, insets),
        railWidth
      )
      setLayout(current => current?.width === next?.width && current?.marginLeft === next?.marginLeft && current?.marginRight === next?.marginRight ? current : next)
    }
    measure()
    const observer = new ResizeObserver(measure)
    observer.observe(stream)
    observer.observe(parent)
    stream.addEventListener('transitionend', measure)
    return () => {
      observer.disconnect()
      stream.removeEventListener('transitionend', measure)
    }
  }, [containerRef, insets, railWidth, wide])

  return layout ?? undefined
}
