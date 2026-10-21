import { useEffect, useState } from 'react'

/**
 * True once `value` has stayed true for `delayMs`; false again as soon as it turns false.
 * Keeps loading placeholders from flashing during transitions that settle quickly.
 */
export function useDelayedFlag(value: boolean, delayMs: number): boolean {
  const [settled, setSettled] = useState(false)
  useEffect(() => {
    if (!value) {
      setSettled(false)
      return undefined
    }
    const timer = window.setTimeout(() => setSettled(true), delayMs)
    return () => window.clearTimeout(timer)
  }, [value, delayMs])
  return value && settled
}

/** Grace period before a pending workspace connection shows opening placeholders. */
export const WORKSPACE_OPENING_PLACEHOLDER_DELAY_MS = 300
