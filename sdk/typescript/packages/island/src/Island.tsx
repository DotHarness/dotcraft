import { useLayoutEffect, useRef, useState, type HTMLAttributes, type Ref } from 'react'
import type { IslandTheme } from './index.js'

export interface IslandProps extends HTMLAttributes<HTMLDivElement> {
  theme?: IslandTheme
  reduceMotion?: boolean
  card?: boolean
  leaving?: boolean
  ref?: Ref<HTMLDivElement>
}

export function Island({ theme = 'dark', reduceMotion = false, card = false, leaving = false, className, ref, onPointerEnter, onPointerLeave, children, ...rest }: IslandProps) {
  const content = useRef<HTMLDivElement | null>(null)
  const [width, setWidth] = useState<number | null>(null)
  const [hovered, setHovered] = useState(false)

  // Under the pointer the island only grows, so a control never moves out from under it.
  useLayoutEffect(() => {
    const node = content.current
    if (!node || card) return undefined
    const measure = () => {
      const next = node.offsetWidth
      setWidth((value) => (hovered && value !== null ? Math.max(value, next) : next))
    }
    measure()
    const observer = new ResizeObserver(measure)
    observer.observe(node)
    return () => observer.disconnect()
  }, [card, hovered])

  return (
    <div
      {...rest}
      ref={ref}
      className={className ? `dci-island ${className}` : 'dci-island'}
      data-theme={theme}
      data-reduce-motion={reduceMotion ? 'on' : 'off'}
      data-card={card || undefined}
      data-leaving={leaving || undefined}
      style={card || width === null ? rest.style : { ...rest.style, width }}
      onPointerEnter={(event) => { setHovered(true); onPointerEnter?.(event) }}
      onPointerLeave={(event) => { setHovered(false); onPointerLeave?.(event) }}
    >
      <div ref={content} className="dci-island__content">{children}</div>
    </div>
  )
}
