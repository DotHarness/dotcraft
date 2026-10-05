import { useCallback, useRef, useState } from 'react'
import { useT } from '../../../../contexts/LocaleContext'
import { addToast } from '../../../../stores/toastStore'

export type InstantInterruptSettings = ReturnType<typeof useInstantInterruptSettings>

export function useInstantInterruptSettings(reloadWorkspaceCore: () => Promise<void>) {
  const t = useT()
  const [enabled, setEnabled] = useState(true)
  const [pending, setPending] = useState(false)
  const pendingRef = useRef(false)

  const applyEnabled = useCallback((next: boolean): void => {
    if (!pendingRef.current) setEnabled(next)
  }, [])

  async function save(next: boolean): Promise<void> {
    if (pendingRef.current || next === enabled) return
    const previous = enabled
    pendingRef.current = true
    setPending(true)
    setEnabled(next)
    try {
      await window.api.appServer.sendRequest('workspace/config/update', { instantInterruptEnabled: next })
      pendingRef.current = false
      await reloadWorkspaceCore()
    } catch (err) {
      setEnabled(previous)
      addToast(t('settings.followUpBehavior.instantInterrupt.saveFailed', {
        error: err instanceof Error ? err.message : String(err)
      }), 'error')
    } finally {
      pendingRef.current = false
      setPending(false)
    }
  }

  return { enabled, pending, applyEnabled, setEnabled: save }
}
