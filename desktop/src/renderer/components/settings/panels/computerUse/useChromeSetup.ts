import { useCallback, useState } from 'react'

import type { ChromeSetupStatus } from '../../../../../preload/api'
import { useT } from '../../../../contexts/LocaleContext'
import { addToast } from '../../../../stores/toastStore'
import { setupResultOk, setupResultText } from './chromeSetup'

export interface ChromeSetup {
  status: ChromeSetupStatus | null
  loading: boolean
  reload: () => Promise<void>
  nativeHostInstalling: boolean
  installNativeHost: () => Promise<void>
  opening: boolean
  openChrome: (url?: string) => Promise<void>
}

export function useChromeSetup(): ChromeSetup {
  const t = useT()
  const [status, setStatus] = useState<ChromeSetupStatus | null>(null)
  const [loading, setLoading] = useState(false)
  const [nativeHostInstalling, setNativeHostInstalling] = useState(false)
  const [opening, setOpening] = useState(false)

  const reload = useCallback(async (): Promise<void> => {
    if (!window.api.chrome?.checkSetup) return
    setLoading(true)
    try {
      setStatus(await window.api.chrome.checkSetup())
    } catch (err) {
      addToast(t('settings.chrome.checkFailed', { error: errorMessage(err) }), 'error')
    } finally {
      setLoading(false)
    }
  }, [t])

  async function installNativeHost(): Promise<void> {
    if (!window.api.chrome?.installNativeHost) return
    setNativeHostInstalling(true)
    try {
      const result = await window.api.chrome.installNativeHost()
      if (!setupResultOk(result)) {
        throw new Error(setupResultText(result, 'error') || setupResultText(result, 'stderr') || 'Chrome connection component install failed.')
      }
      addToast(t('settings.chrome.nativeHostInstalled'), 'success')
      await reload()
    } catch (err) {
      addToast(t('settings.chrome.nativeHostInstallFailed', { error: errorMessage(err) }), 'error')
    } finally {
      setNativeHostInstalling(false)
    }
  }

  async function openChrome(url?: string): Promise<void> {
    if (!window.api.chrome?.openChrome) return
    setOpening(true)
    try {
      const result = await window.api.chrome.openChrome({ url })
      if (!setupResultOk(result)) {
        throw new Error(setupResultText(result, 'error') || 'Google Chrome was not found.')
      }
      addToast(t('settings.chrome.opened'), 'success')
      await reload()
    } catch (err) {
      addToast(t('settings.chrome.openFailed', { error: errorMessage(err) }), 'error')
    } finally {
      setOpening(false)
    }
  }

  return { status, loading, reload, nativeHostInstalling, installNativeHost, opening, openChrome }
}

function errorMessage(err: unknown): string {
  return err instanceof Error ? err.message : String(err)
}
