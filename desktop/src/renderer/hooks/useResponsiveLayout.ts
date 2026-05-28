import { useEffect } from 'react'
import { useUIStore } from '../stores/uiStore'

const BREAKPOINT_FULL = 1250       // >= 1250px: all three panels visible
const BREAKPOINT_NO_DETAIL = 900   // 900-1249px: detail panel auto-collapses
// < 900px: sidebar collapses to icon-only

/**
 * Manages responsive panel visibility based on window width.
 * Subscribes to window resize events and applies breakpoint rules from spec §8.2.
 */
export function useResponsiveLayout(): void {
  const setResponsiveLayout = useUIStore((state) => state.setResponsiveLayout)

  useEffect(() => {
    function applyBreakpoint(width: number): void {
      setResponsiveLayout(classifyWidth(width))
    }

    // Apply on mount
    applyBreakpoint(window.innerWidth)

    let debounceTimer: ReturnType<typeof setTimeout> | null = null

    function handleResize(): void {
      if (debounceTimer) clearTimeout(debounceTimer)
      debounceTimer = setTimeout(() => {
        applyBreakpoint(window.innerWidth)
      }, 100)
    }

    window.addEventListener('resize', handleResize)
    return () => {
      window.removeEventListener('resize', handleResize)
      if (debounceTimer) clearTimeout(debounceTimer)
    }
  }, [setResponsiveLayout])
}

/**
 * Returns the current breakpoint classification.
 * Useful for testing breakpoint logic in isolation.
 */
export function classifyWidth(width: number): 'full' | 'no-detail' | 'collapsed' {
  if (width >= BREAKPOINT_FULL) return 'full'
  if (width >= BREAKPOINT_NO_DETAIL) return 'no-detail'
  return 'collapsed'
}
