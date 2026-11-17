import { useEffect, type RefObject } from 'react'
import { create } from 'zustand'

const OPEN_DELAY_MS = 100
const CLOSE_DELAY_MS = 180
/** Lets the pointer rest on the panel's shadow without leaving the sidebar's width. */
const ZONE_SLACK_PX = 12

export const useSidebarPeekStore = create<{ open: boolean }>(() => ({ open: false }))

let openTimer: ReturnType<typeof setTimeout> | null = null

export function openSidebarPeekSoon(): void {
  if (openTimer != null || useSidebarPeekStore.getState().open) return
  openTimer = setTimeout(() => {
    openTimer = null
    useSidebarPeekStore.setState({ open: true })
  }, OPEN_DELAY_MS)
}

export function cancelSidebarPeekOpen(): void {
  if (openTimer == null) return
  clearTimeout(openTimer)
  openTimer = null
}

export function closeSidebarPeek(): void {
  cancelSidebarPeekOpen()
  useSidebarPeekStore.setState({ open: false })
}

function isMenuOpen(): boolean {
  return document.querySelector('[role="menu"]') != null
}

export function useSidebarPeekDismiss(open: boolean, panelRef: RefObject<HTMLElement | null>): void {
  useEffect(() => {
    if (!open) return
    let closeTimer: ReturnType<typeof setTimeout> | null = null
    const cancelClose = (): void => {
      if (closeTimer != null) clearTimeout(closeTimer)
      closeTimer = null
    }
    const closeSoon = (): void => {
      closeTimer ??= setTimeout(closeSidebarPeek, CLOSE_DELAY_MS)
    }
    // Layout offsets ignore the slide-in transform, so the zone is the panel's final footprint.
    const zoneRight = (): number => {
      const panel = panelRef.current
      if (!panel) return 0
      const originLeft = panel.offsetParent?.getBoundingClientRect().left ?? 0
      return originLeft + panel.offsetLeft + panel.offsetWidth + ZONE_SLACK_PX
    }
    const onPointerMove = (event: PointerEvent): void => {
      if (event.clientX <= zoneRight() || isMenuOpen()) cancelClose()
      else closeSoon()
    }
    const onPointerLeaveWindow = (): void => {
      if (!isMenuOpen()) closeSoon()
    }
    const onPointerDown = (event: PointerEvent): void => {
      const target = event.target as Element | null
      if (panelRef.current?.contains(target) || target?.closest('[role="menu"], [data-sidebar-toggle]')) return
      closeSidebarPeek()
    }
    const onKeyDown = (event: KeyboardEvent): void => {
      if (event.key === 'Escape' && !event.defaultPrevented && !isMenuOpen()) closeSidebarPeek()
    }
    const root = document.documentElement
    document.addEventListener('pointermove', onPointerMove, true)
    root.addEventListener('pointerleave', onPointerLeaveWindow)
    document.addEventListener('pointerdown', onPointerDown, true)
    document.addEventListener('keydown', onKeyDown)
    return () => {
      cancelClose()
      document.removeEventListener('pointermove', onPointerMove, true)
      root.removeEventListener('pointerleave', onPointerLeaveWindow)
      document.removeEventListener('pointerdown', onPointerDown, true)
      document.removeEventListener('keydown', onKeyDown)
    }
  }, [open, panelRef])
}
