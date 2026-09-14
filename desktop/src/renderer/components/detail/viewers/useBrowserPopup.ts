import { useEffect, useLayoutEffect, useRef, useState } from 'react'

export function useBrowserPopup(tabId: string, active: boolean) {
  const [popup, setPopup] = useState<'more' | null>(null)
  const [position, setPosition] = useState({ left: 0, top: 0, maxHeight: 480 })
  const more = useRef<HTMLButtonElement>(null)
  const content = useRef<HTMLDivElement>(null)
  useEffect(() => { setPopup(null) }, [tabId, active])
  useLayoutEffect(() => {
    if (!popup) return
    const anchor = more.current
    const reposition = () => {
      if (!anchor) return
      const rect = anchor.getBoundingClientRect()
      const width = Math.min(240, window.innerWidth - 16)
      const height = Math.min(content.current?.scrollHeight ?? 480, 480)
      const below = window.innerHeight - rect.bottom - 8
      const above = rect.top - 8
      const useAbove = below < height && above > below
      setPosition({
        left: Math.max(8, Math.min(rect.right - width, window.innerWidth - width - 8)),
        top: useAbove ? Math.max(8, rect.top - height - 2) : rect.bottom + 2,
        maxHeight: Math.max(0, Math.min(480, (useAbove ? above : below) - 2))
      })
    }
    reposition()
    content.current?.querySelector<HTMLElement>('button:not(:disabled)')?.focus()
    const observer = new ResizeObserver(reposition)
    if (content.current) observer.observe(content.current)
    window.addEventListener('resize', reposition)
    const dismiss = (event: PointerEvent) => {
      const target = event.target as Node
      if (!content.current?.contains(target) && !more.current?.contains(target)) setPopup(null)
    }
    const key = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault()
        event.stopPropagation()
        setPopup(null)
        anchor?.focus()
      } else if (['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key)) {
        const buttons = Array.from(content.current?.querySelectorAll<HTMLButtonElement>('button:not(:disabled)') ?? [])
        const index = buttons.indexOf(document.activeElement as HTMLButtonElement)
        const next = event.key === 'Home' ? 0 : event.key === 'End' ? buttons.length - 1 :
          (index + (event.key === 'ArrowUp' ? -1 : 1) + buttons.length) % buttons.length
        buttons[next]?.focus()
        event.preventDefault()
      }
    }
    const blur = () => setPopup(null)
    const unsubscribe = window.api.workspace.viewer.browser.host?.onEvent(event => {
      if (event.type === 'pointer-down') setPopup(null)
    })
    document.addEventListener('pointerdown', dismiss)
    document.addEventListener('keydown', key, true)
    window.addEventListener('blur', blur)
    return () => {
      observer.disconnect()
      unsubscribe?.()
      window.removeEventListener('resize', reposition)
      document.removeEventListener('pointerdown', dismiss)
      document.removeEventListener('keydown', key, true)
      window.removeEventListener('blur', blur)
    }
  }, [popup])
  return { popup, setPopup, position, more, content }
}
