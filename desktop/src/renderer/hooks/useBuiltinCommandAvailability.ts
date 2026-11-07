import { useCallback, useEffect, useMemo, useRef, useState } from 'react'

interface UseBuiltinCommandAvailabilityArgs {
  enabled: boolean
}

type LoadStatus = 'idle' | 'loading' | 'ready' | 'error'

function includesCommand(payload: unknown, command: string): boolean {
  const typed = payload as { commands?: unknown[] }
  return Array.isArray(typed.commands) && typed.commands.some((entry) => {
    const name = typeof (entry as { name?: unknown }).name === 'string'
      ? (entry as { name: string }).name.trim()
      : ''
    return name.toLowerCase() === command
  })
}

export function useBuiltinCommandAvailability({
  enabled
}: UseBuiltinCommandAvailabilityArgs): {
  initAvailable: boolean
  reloadSkillsAvailable: boolean
  status: LoadStatus
  reload: () => Promise<void>
} {
  const [initAvailable, setInitAvailable] = useState(false)
  const [reloadSkillsAvailable, setReloadSkillsAvailable] = useState(false)
  const [status, setStatus] = useState<LoadStatus>('idle')
  const reqRef = useRef(0)

  const fetchCommands = useCallback(async () => {
    if (!enabled) {
      setInitAvailable(false)
      setReloadSkillsAvailable(false)
      setStatus('idle')
      return
    }
    const reqId = ++reqRef.current
    setStatus('loading')
    try {
      const payload = await window.api.appServer.sendRequest('command/list', {})
      if (reqId !== reqRef.current) return
      setInitAvailable(includesCommand(payload, '/init'))
      setReloadSkillsAvailable(includesCommand(payload, '/reload-skills'))
      setStatus('ready')
    } catch {
      if (reqId !== reqRef.current) return
      setInitAvailable(false)
      setReloadSkillsAvailable(false)
      setStatus('error')
    }
  }, [enabled])

  useEffect(() => {
    void fetchCommands()
  }, [fetchCommands])

  return useMemo(
    () => ({
      initAvailable,
      reloadSkillsAvailable,
      status,
      reload: fetchCommands
    }),
    [fetchCommands, initAvailable, reloadSkillsAvailable, status]
  )
}
