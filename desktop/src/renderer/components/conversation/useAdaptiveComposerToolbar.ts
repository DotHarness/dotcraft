import { useLayoutEffect, useRef, useState, type RefObject } from 'react'

type OptionalControl = 'context' | 'model' | 'goal' | 'mode'
type HiddenControls = Record<OptionalControl, boolean>

const PRIORITY: OptionalControl[] = ['context', 'model', 'goal', 'mode']
const VISIBLE: HiddenControls = { context: false, model: false, goal: false, mode: false }

export function useAdaptiveComposerToolbar(enabled: boolean, contents: readonly unknown[]): {
  toolbarRef: RefObject<HTMLDivElement | null>
  hidden: HiddenControls
} {
  const toolbarRef = useRef<HTMLDivElement>(null)
  const [hidden, setHidden] = useState<HiddenControls>(VISIBLE)

  useLayoutEffect(() => {
    if (!enabled) return
    const toolbar = toolbarRef.current
    if (!toolbar) return
    const leading = toolbar.querySelector<HTMLElement>('[data-composer-toolbar-leading]')
    const trailing = toolbar.querySelector<HTMLElement>('[data-composer-toolbar-trailing]')
    if (!leading || !trailing) return

    const items = PRIORITY.map((id) => ({
      id,
      element: toolbar.querySelector<HTMLElement>(`[data-adaptive-control="${id}"]`)
    }))
    const measure = (): void => {
      const previous = items.map(({ element }) => element?.style.getPropertyValue('display') ?? '')
      for (const { element } of items) element?.style.setProperty('display', 'inline-flex', 'important')

      const gap = parseFloat(getComputedStyle(toolbar).columnGap) || 0
      let required = leading.scrollWidth + trailing.scrollWidth + gap
      const widths = items.map(({ element }) => element
        ? element.offsetWidth + (parseFloat(getComputedStyle(element.parentElement!).columnGap) || 0)
        : 0)

      items.forEach(({ element }, index) => {
        if (!element) return
        if (previous[index]) element.style.setProperty('display', previous[index])
        else element.style.removeProperty('display')
      })

      const next = { ...VISIBLE }
      const available = toolbar.clientWidth
      items.forEach(({ id, element }, index) => {
        if (element && required > available + 1) {
          next[id] = true
          required -= widths[index]
        }
      })
      setHidden((current) => PRIORITY.every((id) => current[id] === next[id]) ? current : next)
    }

    measure()
    if (typeof ResizeObserver === 'undefined') return
    const observer = new ResizeObserver(measure)
    observer.observe(toolbar)
    observer.observe(leading)
    observer.observe(trailing)
    for (const { element } of items) if (element) observer.observe(element)
    return () => observer.disconnect()
  }, [enabled, ...contents])

  return { toolbarRef, hidden }
}
