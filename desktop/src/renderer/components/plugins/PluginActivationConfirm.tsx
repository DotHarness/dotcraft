import { useEffect, useState, type JSX } from 'react'
import type { PluginActivationConfirmDetails } from '../../../shared/desktopToolConfirm'
import { useT } from '../../contexts/LocaleContext'
import type { PluginEntry } from '../../stores/pluginStore'
import { normalizePlugin } from '../../stores/pluginSnapshot'
import { PluginInstallDialog } from './PluginInstallDialog'

export function PluginActivationConfirm({
  request,
  submitting = false,
  onRespond
}: {
  request: PluginActivationConfirmDetails
  submitting?: boolean
  onRespond: (confirmed: boolean) => void
}): JSX.Element | null {
  const t = useT()
  const [plugin, setPlugin] = useState<PluginEntry | null>(null)

  useEffect(() => {
    let cancelled = false
    const params = { id: request.pluginId, ...(request.marketplaceName ? { marketplaceName: request.marketplaceName } : {}) }
    window.api.appServer.sendRequest('plugin/view', params)
      .then((result) => {
        const entry = (result as { plugin?: PluginEntry }).plugin
        if (cancelled) return
        if (entry) setPlugin(normalizePlugin(entry))
        else onRespond(false)
      })
      .catch(() => {
        if (!cancelled) onRespond(false)
      })
    return () => { cancelled = true }
  }, [onRespond, request.marketplaceName, request.pluginId])

  if (!plugin) return null
  return (
    <PluginInstallDialog
      plugin={plugin}
      installing={submitting}
      confirmLabel={t('pluginActivation.confirm')}
      onInstall={() => onRespond(true)}
      onClose={() => onRespond(false)}
    />
  )
}
