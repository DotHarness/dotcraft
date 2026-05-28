import { useEffect, useRef, useState } from 'react'
import type { ReactNode } from 'react'

interface CollapsibleContentProps {
  expanded: boolean
  renderExpanded: boolean
  setRenderExpanded: (value: boolean) => void
  children: ReactNode
}

const COLLAPSIBLE_TRANSITION_MS = 200

export function CollapsibleContent({
  expanded,
  renderExpanded,
  setRenderExpanded,
  children
}: CollapsibleContentProps): JSX.Element | null {
  const contentRef = useRef<HTMLDivElement | null>(null)
  const animationFrameRef = useRef<number | null>(null)
  const transitionTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  const [height, setHeight] = useState<string>('0px')
  const [opacity, setOpacity] = useState(0)
  const [translateY, setTranslateY] = useState('-2px')

  useEffect(() => {
    if (animationFrameRef.current != null) {
      cancelAnimationFrame(animationFrameRef.current)
      animationFrameRef.current = null
    }
    if (transitionTimerRef.current != null) {
      clearTimeout(transitionTimerRef.current)
      transitionTimerRef.current = null
    }

    if (!renderExpanded) {
      setHeight('0px')
      setOpacity(0)
      setTranslateY('-2px')
      return
    }

    const measuredHeight = contentRef.current?.scrollHeight ?? 0

    if (expanded) {
      setHeight('0px')
      setOpacity(0)
      setTranslateY('-2px')
      animationFrameRef.current = requestAnimationFrame(() => {
        setHeight(`${measuredHeight}px`)
        setOpacity(1)
        setTranslateY('0px')
        animationFrameRef.current = null
      })
      transitionTimerRef.current = setTimeout(() => {
        setHeight('auto')
        transitionTimerRef.current = null
      }, COLLAPSIBLE_TRANSITION_MS)
      return
    }

    setHeight(`${measuredHeight}px`)
    setOpacity(1)
    setTranslateY('0px')
    animationFrameRef.current = requestAnimationFrame(() => {
      setHeight('0px')
      setOpacity(0)
      setTranslateY('-2px')
      animationFrameRef.current = null
    })
    transitionTimerRef.current = setTimeout(() => {
      setRenderExpanded(false)
      transitionTimerRef.current = null
    }, COLLAPSIBLE_TRANSITION_MS)

    return () => {
      if (animationFrameRef.current != null) {
        cancelAnimationFrame(animationFrameRef.current)
        animationFrameRef.current = null
      }
      if (transitionTimerRef.current != null) {
        clearTimeout(transitionTimerRef.current)
        transitionTimerRef.current = null
      }
    }
  }, [expanded, renderExpanded, setRenderExpanded])

  useEffect(() => {
    return () => {
      if (animationFrameRef.current != null) {
        cancelAnimationFrame(animationFrameRef.current)
        animationFrameRef.current = null
      }
      if (transitionTimerRef.current != null) {
        clearTimeout(transitionTimerRef.current)
        transitionTimerRef.current = null
      }
    }
  }, [])

  if (!renderExpanded) {
    return null
  }

  return (
    <div
      aria-hidden={!expanded}
      style={{
        overflow: 'hidden',
        height,
        opacity,
        transform: `translateY(${translateY})`,
        transition: `height ${COLLAPSIBLE_TRANSITION_MS}ms ease-out, opacity ${COLLAPSIBLE_TRANSITION_MS}ms ease-out, transform ${COLLAPSIBLE_TRANSITION_MS}ms ease-out`
      }}
    >
      <div ref={contentRef}>
        {children}
      </div>
    </div>
  )
}
