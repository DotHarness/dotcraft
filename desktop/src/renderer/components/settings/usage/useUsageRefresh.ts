import { useEffect } from 'react'

const REFRESH_INTERVAL_MS = 60_000

export function useUsageRefresh(refresh: () => void | Promise<void>): void {
  useEffect(() => {
    const refreshVisible = (): void => {
      if (document.visibilityState !== 'hidden') void refresh()
    }
    refreshVisible()
    const interval = window.setInterval(refreshVisible, REFRESH_INTERVAL_MS)
    window.addEventListener('focus', refreshVisible)
    document.addEventListener('visibilitychange', refreshVisible)
    return () => {
      window.clearInterval(interval)
      window.removeEventListener('focus', refreshVisible)
      document.removeEventListener('visibilitychange', refreshVisible)
    }
  }, [refresh])
}
