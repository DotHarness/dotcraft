import { useCallback, useEffect, useState } from 'react'

import type { SharePcStatus } from '../../../../../shared/satellites'

export interface SharePcView {
  status: SharePcStatus | null
  loaded: boolean
  reload: () => void
}

/** Read by the Connections shell, not the segment: an absent runtime hides the segment. */
export function useSharePcStatus(): SharePcView {
  const [status, setStatus] = useState<SharePcStatus | null>(null)
  const [loaded, setLoaded] = useState(false)
  const [token, setToken] = useState(0)

  useEffect(() => {
    let cancelled = false
    window.api.satellites
      .shareStatus()
      .then((next) => {
        if (!cancelled) setStatus(next)
      })
      .catch(() => undefined)
      .finally(() => {
        if (!cancelled) setLoaded(true)
      })
    return () => {
      cancelled = true
    }
  }, [token])

  const reload = useCallback(() => setToken((value) => value + 1), [])
  return { status, loaded, reload }
}
