import { useCallback, useRef, useState } from 'react'
import { useT } from '../../../../contexts/LocaleContext'
import { addToast } from '../../../../stores/toastStore'

export type CodeModeMode = 'off' | 'on' | 'only'

export type CodeModeSettings = ReturnType<typeof useCodeModeSettings>

export function useCodeModeSettings(reloadWorkspaceCore: () => Promise<void>) {
  const t = useT()
  const [mode, setMode] = useState<CodeModeMode>('only')
  const [pending, setPending] = useState(false)
  const pendingRef = useRef(false)

  const applyMode = useCallback((next: CodeModeMode): void => {
    if (!pendingRef.current) setMode(next)
  }, [])

  async function save(next: CodeModeMode): Promise<void> {
    if (pendingRef.current || next === mode) return
    const previous = mode
    pendingRef.current = true
    setPending(true)
    setMode(next)
    try {
      await window.api.appServer.sendRequest('workspace/config/update', { toolsCodeModeMode: next })
      pendingRef.current = false
      await reloadWorkspaceCore()
    } catch (err) {
      setMode(previous)
      addToast(t('settings.codeMode.saveFailed', {
        error: err instanceof Error ? err.message : String(err)
      }), 'error')
    } finally {
      pendingRef.current = false
      setPending(false)
    }
  }

  return { mode, pending, applyMode, setMode: save }
}
