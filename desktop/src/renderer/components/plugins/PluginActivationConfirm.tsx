import { useMemo, type JSX } from 'react'
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
}): JSX.Element {
  const t = useT()
  const plugin = useMemo(() => normalizePlugin(request.plugin as PluginEntry), [request.plugin])
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
