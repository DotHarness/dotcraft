import { useEffect, useState } from 'react'
import { Download, FileText, Search, Trash2, X } from 'lucide-react'
import type { BrowserDownloadRecord } from '../../../shared/viewer/browserFeedback'
import { useT } from '../../contexts/LocaleContext'
import { useUIStore } from '../../stores/uiStore'
import { Input } from '../ui/Input'
import { Button } from '../ui/Button'
import { IconButton } from '../ui/IconButton'
import { SettingsPanelShell } from './SettingsPanelShell'
import { SettingsBreadcrumb } from './SettingsBreadcrumb'
import { SettingsGroup } from './SettingsGroup'
import css from './BrowserDownloadHistory.module.css'

export function BrowserDownloadHistory(): JSX.Element {
  const t = useT()
  const [api] = useState(() => window.api.workspace.viewer.browser)
  const [records, setRecords] = useState<BrowserDownloadRecord[]>([])
  const [query, setQuery] = useState('')
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  useEffect(() => {
    let disposed = false
    let updated = false
    const unsubscribe = api.onFeedback(event => {
      if (event.type === 'downloads') { updated = true; setRecords(event.records) }
    })
    void api.downloads().then(value => { if (!disposed && !updated) setRecords(value) })
      .catch(error => { if (!disposed) setError(String(error)) })
      .finally(() => { if (!disposed) setLoading(false) })
    return () => { disposed = true; unsubscribe() }
  }, [api])
  async function run(operation: () => Promise<unknown>): Promise<void> {
    setError('')
    try { await operation(); setRecords(await api.downloads()) } catch (error) { setError(String(error)) }
  }
  const search = query.trim().toLocaleLowerCase()
  const filtered = records.filter(record => `${record.filename} ${record.url}`.toLocaleLowerCase().includes(search))
  return <SettingsPanelShell title={t('browser.downloads.history')}
    breadcrumb={<SettingsBreadcrumb parentLabel={t('settings.browserUse.pageTitle')} currentLabel={t('browser.downloads.history')}
      onBack={() => useUIStore.setState({ browserDownloadHistoryOpen: false })} />}>
    <h1 className={css.title}>{t('browser.downloads.history')}</h1>
    <div className={css.search}><Search size={16} /><Input bare value={query} aria-label={t('browser.downloads.search')}
      placeholder={t('browser.downloads.search')} onChange={event => setQuery(event.target.value)} /></div>
    {error && <p role="alert">{error}</p>}
    <SettingsGroup title={t('browser.downloads.allTime')} flush
      headerAction={<Button size="sm" disabled={loading || !records.some(record => record.state !== 'progressing')}
        onClick={() => void run(() => api.removeDownload({}))}>{t('browser.downloads.clearAll')}</Button>}>
      {loading ? <div className={css.empty} role="status">{t('common.loading')}</div> : filtered.length === 0 ?
        <div className={css.empty}><Download size={28} /><strong>{t(search ? 'browser.downloads.noResults' : 'viewer.browser.noDownloads')}</strong>
          {!search && <span>{t('browser.downloads.emptyHint')}</span>}</div> : filtered.map(record =>
          <div key={record.id} className={css.record}>
            <FileText size={22} />
            <div className={css.file}>
              <strong title={record.filename}>{record.filename}</strong>
              <span title={record.url}>{record.url}</span>
              <span>{t(`viewer.browser.download.${record.state}`)} · {formatBytes(record.receivedBytes)}
                {record.totalBytes > 0 && ` / ${formatBytes(record.totalBytes)}`} · {new Date(record.startedAt).toLocaleDateString()}</span>
              {record.state === 'progressing' && <progress aria-label={record.filename}
                max={record.totalBytes || undefined} value={record.totalBytes ? record.receivedBytes : undefined} />}
            </div>
            {record.state === 'completed' && <Button size="sm" onClick={() => void run(() => api.openDownload({ id: record.id }))}>{t('viewer.browser.openDownload')}</Button>}
            {record.state === 'progressing' ? <IconButton icon={<X size={16} />} label={t('common.cancel')} tooltipLabel={t('common.cancel')}
              onClick={() => void run(() => api.cancelDownload({ id: record.id }))} /> :
              <IconButton icon={<Trash2 size={14} />} label={t('browser.downloads.remove')} tooltipLabel={t('browser.downloads.remove')}
                onClick={() => void run(() => api.removeDownload({ id: record.id }))} />}
          </div>)}
    </SettingsGroup>
  </SettingsPanelShell>
}

function formatBytes(value: number): string {
  if (value < 1024) return `${value} B`
  if (value < 1024 ** 2) return `${(value / 1024).toFixed(1)} KB`
  return `${(value / 1024 ** 2).toFixed(1)} MB`
}
