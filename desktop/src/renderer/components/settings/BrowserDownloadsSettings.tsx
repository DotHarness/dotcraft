import { useEffect, useState } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { useUIStore } from '../../stores/uiStore'
import { Button } from '../ui/Button'
import { SettingsGroup, SettingsRow } from './SettingsGroup'

export function BrowserDownloadsSettings(): JSX.Element {
  const t = useT()
  const [location, setLocation] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const [api] = useState(() => window.api.workspace.viewer.browser)
  useEffect(() => { void api.downloadLocation().then(setLocation).catch(error => setError(String(error))) }, [api])
  return <SettingsGroup title={t('viewer.browser.downloads')}>
    <SettingsRow label={t('browser.downloads.location')} description={location}
      control={<Button disabled={busy} onClick={async () => {
        setBusy(true); setError('')
        try { setLocation(await api.changeDownloadLocation()) } catch (error) { setError(String(error)) }
        finally { setBusy(false) }
      }}>{t('browser.downloads.change')}</Button>} />
    <SettingsRow label={t('browser.downloads.history')} description={t('browser.downloads.historyHint')}
      control={<Button onClick={() => useUIStore.setState({ browserDownloadHistoryOpen: true })}>{t('browser.downloads.manage')}</Button>} />
    {error && <SettingsRow><span role="alert">{error}</span></SettingsRow>}
  </SettingsGroup>
}
