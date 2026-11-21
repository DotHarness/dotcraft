import { useEffect, useState } from 'react'
import type { ChannelLoginPhase } from './ChannelLoginDialog'
import { channelRequest } from './channelPluginRequests'

export function useChannelLogin(pluginId: string | null, active: boolean, connected: boolean, failed: boolean) {
  const [open, setOpen] = useState(false)
  const [dismissed, setDismissed] = useState(false)
  const [qrDataUrl, setQrDataUrl] = useState<string | null>(null)
  const [hasShownCode, setHasShownCode] = useState(false)

  useEffect(() => {
    if (!pluginId || !active || connected || failed || dismissed) return
    let disposed = false
    let timer: ReturnType<typeof setTimeout> | undefined
    async function poll(id: string): Promise<void> {
      try {
        const { artifact } = await channelRequest('channel/setup/read', { pluginId: id })
        if (disposed) return
        const url = artifact?.mimeType === 'image/png' ? `data:image/png;base64,${artifact.data}` : null
        setQrDataUrl(url)
        if (url) {
          setHasShownCode(true)
          setOpen(true)
        }
      } catch {
        if (!disposed) setQrDataUrl(null)
      } finally {
        if (!disposed) timer = setTimeout(() => void poll(id), 1200)
      }
    }
    void poll(pluginId)
    return () => {
      disposed = true
      clearTimeout(timer)
    }
  }, [active, connected, dismissed, failed, pluginId])

  const phase: ChannelLoginPhase = connected ? 'connected' : failed ? 'failed' : hasShownCode && !qrDataUrl ? 'expired' : 'qr'

  return {
    open: Boolean(pluginId) && open,
    phase,
    qrDataUrl,
    show() {
      setDismissed(false)
      setHasShownCode(false)
      setQrDataUrl(null)
      setOpen(true)
    },
    hide() {
      setDismissed(true)
      setOpen(false)
      setQrDataUrl(null)
    },
  }
}
