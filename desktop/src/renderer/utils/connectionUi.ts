import { useEffect, useState } from 'react'
import type { ConnectionErrorType, ConnectionStatus } from '../stores/connectionStore'

export const STARTUP_SLOW_CONNECTING_HINT_MS = 15_000

export function isFatalConnectionError(
  status: ConnectionStatus,
  errorType: ConnectionErrorType | null
): boolean {
  return status === 'error' && (errorType === 'binary-not-found' || errorType === 'remote-config-invalid')
}

export function useSlowConnectingHint(
  status: ConnectionStatus,
  workspacePath: string
): boolean {
  const [showSlowConnectingHint, setShowSlowConnectingHint] = useState(false)

  useEffect(() => {
    if (!workspacePath || status !== 'connecting') {
      setShowSlowConnectingHint(false)
      return
    }
    const timer = setTimeout(() => {
      setShowSlowConnectingHint(true)
    }, STARTUP_SLOW_CONNECTING_HINT_MS)
    return () => {
      clearTimeout(timer)
      setShowSlowConnectingHint(false)
    }
  }, [status, workspacePath])

  return showSlowConnectingHint
}
