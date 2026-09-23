import { useT } from '../../contexts/LocaleContext'
import {
  useDesktopPluginSource, authorizeDesktopPlugins, revokeDesktopPlugins, retryDesktopPlugins
} from '../../plugins/desktopPluginSource'
import { usePluginStore } from '../../stores/pluginStore'
import { Button } from '../ui/Button'

export function RemoteDesktopPluginControls(): JSX.Element | null {
  const { source, errors, busy } = useDesktopPluginSource()
  const hasDesktopPlugin = usePluginStore(state => state.plugins.some(plugin => plugin.installed && plugin.enabled && plugin.desktop))
  const t = useT()
  if (!source?.remote) return null
  return <>
    {!source.supported && hasDesktopPlugin && <span role="status">{t('desktopPlugins.remote.unsupported')}</span>}
    {Object.keys(errors).length > 0 && <>
      <span role="status" title={Object.values(errors).join('\n')}>{t('desktopPlugins.remote.failed')}</span>
      <Button size="toolbar" disabled={busy} onClick={retryDesktopPlugins}>{t('desktopPlugins.remote.retry')}</Button>
    </>}
    {source.supported && <Button
      size="toolbar"
      disabled={busy}
      onClick={() => void (source.trusted ? revokeDesktopPlugins() : authorizeDesktopPlugins())}
    >{t(source.trusted ? 'desktopPlugins.remote.revoke' : 'desktopPlugins.remote.authorize')}</Button>}
  </>
}
