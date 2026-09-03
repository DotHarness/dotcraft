import { useCallback, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { useToastStore } from '../../stores/toastStore'
import { useT } from '../../contexts/LocaleContext'
import { ToastCard } from './ToastCard'

const STACK_GAP_PX = 10
const COLLAPSED_PEEK_PX = 10
const COLLAPSED_SCALE_STEP = 0.04
const COLLAPSED_VISIBLE_DEPTH = 3

export function ToastContainer(): JSX.Element {
  const toasts = useToastStore((s) => s.toasts)
  const [expanded, setExpanded] = useState(false)
  const t = useT()

  // Below titleBarOverlay so caption buttons do not cover the stack.
  const isMac = window.api.platform === 'darwin'
  const topPx = isMac ? 16 : window.api.titleBarOverlayHeight + 16

  const ordered = [...toasts].reverse()

  const heightsRef = useRef<Map<string, number>>(new Map())
  const [, forceRender] = useState(0)
  const setToastHeight = useCallback((id: string, height: number): void => {
    const prev = heightsRef.current.get(id)
    if (prev === height) return
    heightsRef.current.set(id, height)
    forceRender((n) => n + 1)
  }, [])

  const heights = ordered.map((toast) => heightsRef.current.get(toast.id) ?? 64)
  const expandedTops: number[] = []
  let runningTop = 0
  for (let i = 0; i < ordered.length; i++) {
    expandedTops.push(runningTop)
    runningTop += heights[i] + STACK_GAP_PX
  }

  const containerHeight = expanded
    ? Math.max(0, runningTop - STACK_GAP_PX)
    : (heights[0] ?? 0) +
      Math.min(Math.max(0, ordered.length - 1), COLLAPSED_VISIBLE_DEPTH) * COLLAPSED_PEEK_PX

  return createPortal(
    <section
      className="dc-toast-stack"
      aria-label={t('toast.regionLabel')}
      aria-live="polite"
      aria-relevant="additions text"
      aria-atomic="false"
      data-expanded={expanded ? 'true' : 'false'}
      data-empty={ordered.length === 0 ? 'true' : 'false'}
      onMouseEnter={() => setExpanded(true)}
      onMouseLeave={() => setExpanded(false)}
      onFocus={() => setExpanded(true)}
      onBlur={(e) => {
        if (!e.currentTarget.contains(e.relatedTarget as Node | null)) setExpanded(false)
      }}
      style={{ top: `${topPx}px`, height: `${containerHeight}px` }}
    >
      {ordered.map((toast, index) => {
        const depth = Math.min(index, COLLAPSED_VISIBLE_DEPTH)
        return (
          <ToastCard
            key={toast.id}
            toast={toast}
            index={index}
            expanded={expanded}
            expandedTop={expandedTops[index]}
            collapsedY={depth * COLLAPSED_PEEK_PX}
            collapsedScale={1 - depth * COLLAPSED_SCALE_STEP}
            collapsedOpacity={
              index === 0 ? 1 : index >= COLLAPSED_VISIBLE_DEPTH ? 0 : 1 - index * 0.25
            }
            onMeasure={setToastHeight}
          />
        )
      })}
    </section>,
    document.body
  ) as JSX.Element
}
