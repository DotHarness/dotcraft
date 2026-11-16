import { useCallback, useEffect, useState } from 'react'

import { satelliteHost } from '../runtime'
import type { SharePcStatus } from '../types'

export interface SharePcView {
  status: SharePcStatus | null
  loaded: boolean
  reload: () => void
}

export function useSharePcStatus(): SharePcView {
  const [status, setStatus] = useState<SharePcStatus | null>(null)
  const [loaded, setLoaded] = useState(false)
  const [token, setToken] = useState(0)

  useEffect(() => {
    let cancelled = false
    satelliteHost().satellites
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
