import { useCallback, useRef, useState } from 'react'
import { useT } from '../../../../contexts/LocaleContext'
import { addToast } from '../../../../stores/toastStore'
import type { ImageGenerationConfig } from './imageGenerationModel'

export type ImageGenerationSettings = ReturnType<typeof useImageGenerationSettings>

export function useImageGenerationSettings(reloadWorkspaceCore: () => Promise<void>) {
  const t = useT()
  const [config, setConfig] = useState<ImageGenerationConfig>({ enabled: true, providerId: '' })
  const [pending, setPending] = useState(false)
  const pendingRef = useRef(false)

  const applyConfig = useCallback((next: ImageGenerationConfig): void => {
    if (!pendingRef.current) setConfig(next)
  }, [])

  async function save(next: ImageGenerationConfig, params: Record<string, unknown>): Promise<void> {
    if (pendingRef.current) return
    const previous = config
    pendingRef.current = true
    setPending(true)
    setConfig(next)
    try {
      await window.api.appServer.sendRequest('workspace/config/update', params)
      pendingRef.current = false
      await reloadWorkspaceCore()
    } catch (err) {
      setConfig(previous)
      addToast(t('settings.llm.imageGeneration.saveFailed', {
        error: err instanceof Error ? err.message : String(err)
      }), 'error')
    } finally {
      pendingRef.current = false
      setPending(false)
    }
  }

  return {
    config,
    pending,
    applyConfig,
    setEnabled: (enabled: boolean) =>
      save({ ...config, enabled }, { toolsImageGenerationEnabled: enabled }),
    setProvider: (providerId: string) =>
      save({ ...config, providerId }, { toolsImageGenerationProvider: providerId || null })
  }
}
