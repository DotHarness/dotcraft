import { useCallback, useEffect, useRef, useState, type JSX, type ReactNode } from 'react'
import { RefreshCw } from 'lucide-react'
import type {
  ImportSessionsCompletedNotification,
  ImportSettings,
  ImportSettingsSetParams,
  ImportSourceDetection
} from '@dotcraft/sdk/contracts'

import { useLocale, useT } from '../../../contexts/LocaleContext'
import { readAppServerErrorFields } from '../../../../shared/appServerError'
import { addToast, showToast } from '../../../stores/toastStore'
import { importSourceLabel } from '../../../utils/sessionImport'
import { Button } from '../../ui/Button'
import { PillSwitch } from '../../ui/PillSwitch'
import { Skeleton } from '../../ui/Skeleton'
import { SettingsGroup, SettingsRow } from '../SettingsGroup'
import { SettingsPanelShell } from '../SettingsPanelShell'
import { settingsHintStyle, settingsLabelStyle } from '../settingsTypography'
import { ImportSessionsDialog } from './ImportSessionsDialog'
import { ImportSourceIcon } from './ImportSourceIcon'
import styles from './ImportPanel.module.css'

type Translate = ReturnType<typeof useT>

interface ImportPanelProps {
  workspacePath?: string
}

/** `source` is null while another pass owns the workspace and has not reported progress yet. */
interface RunningImport {
  source: string | null
  completed: number
  total: number
}

/** Detection parses every recent transcript of each source, which can outlast the default timeout. */
const DETECT_TIMEOUT_MS = 120_000

export function ImportPanel({ workspacePath }: ImportPanelProps): JSX.Element {
  const t = useT()
  const locale = useLocale()
  const [settings, setSettings] = useState<ImportSettings | null>(null)
  const [settingsError, setSettingsError] = useState<string | null>(null)
  const [savingSync, setSavingSync] = useState(false)
  const [sources, setSources] = useState<ImportSourceDetection[] | null>(null)
  const [detecting, setDetecting] = useState(true)
  const [detectError, setDetectError] = useState<string | null>(null)
  const [running, setRunning] = useState<RunningImport | null>(null)
  const [dialogSource, setDialogSource] = useState<string | null>(null)
  // Completions and workspace switches re-run detection while a slower run may still be pending.
  const detectRequest = useRef(0)

  const loadSettings = useCallback(async () => {
    try {
      const result = await window.api.appServer.sendRequest('import/settings/get', {})
      setSettings(result.settings)
      setSettingsError(null)
    } catch (error) {
      setSettingsError(errorMessage(error))
    }
  }, [])

  const detect = useCallback(async () => {
    const request = ++detectRequest.current
    setDetecting(true)
    try {
      const result = await window.api.appServer.sendRequest('import/sessions/detect', {}, DETECT_TIMEOUT_MS)
      if (request !== detectRequest.current) return
      setSources(result.sources)
      setDetectError(null)
    } catch (error) {
      if (request === detectRequest.current) setDetectError(errorMessage(error))
    } finally {
      if (request === detectRequest.current) setDetecting(false)
    }
  }, [])

  useEffect(() => {
    void loadSettings()
    void detect()
  }, [detect, loadSettings, workspacePath])

  useEffect(() => window.api.appServer.onNotification((payload) => {
    if (payload.foreground === false) return
    if (payload.method === 'import/sessions/progress') {
      setRunning(payload.params)
    } else if (payload.method === 'import/sessions/completed') {
      setRunning(null)
      void detect()
      void loadSettings()
      announceCompletion(payload.params, t)
    }
  }), [detect, loadSettings, t])

  async function saveSettings(params: ImportSettingsSetParams): Promise<void> {
    const result = await window.api.appServer.sendRequest('import/settings/set', params)
    setSettings(result.settings)
  }

  async function handleSyncChange(next: boolean): Promise<void> {
    if (!settings) return
    const previous = settings
    setSavingSync(true)
    setSettings({ ...previous, syncEnabled: next })
    try {
      await saveSettings({ syncEnabled: next })
    } catch (error) {
      setSettings(previous)
      addToast(t('settings.import.sync.saveFailed', { error: errorMessage(error) }), 'error')
    } finally {
      setSavingSync(false)
    }
  }

  async function handleImport(source: string, total: number, keepInSync: boolean): Promise<void> {
    const update = settings && !settings.workspaceOptOut ? syncUpdate(settings, source, keepInSync) : null
    if (update) await saveSettings(update)
    const started: RunningImport = { source, completed: 0, total }
    setRunning(started)
    try {
      await window.api.appServer.sendRequest('import/sessions/run', { sources: [source] })
    } catch (error) {
      const busy = readAppServerErrorFields(error).data?.code === 'import_busy'
      const next = busy ? { source: null, completed: 0, total: 0 } : null
      // Keep a notification that arrived first; overwriting its completion would leave the panel stuck importing.
      setRunning((current) => (current === started ? next : current))
      if (!busy) throw error
    }
    setDialogSource(null)
  }

  const available = sources?.filter((entry) => entry.available) ?? []
  const totalImportable = available.reduce((sum, entry) => sum + entry.importableCount, 0)
  const runningElsewhere = running != null && !available.some((entry) => entry.source === running.source)
  const syncOn = settings?.syncEnabled === true && !settings.workspaceOptOut
  const dialogCount = available.find((entry) => entry.source === dialogSource)?.importableCount ?? 0

  let status: { text: string; error?: boolean } | null = null
  if (detecting) {
    status = { text: t('settings.import.sources.checking') }
  } else if (runningElsewhere) {
    status = { text: t('settings.import.sources.importing') }
  } else if (detectError && sources) {
    status = { text: t('settings.import.sources.loadFailed', { error: detectError }), error: true }
  } else {
    const parts: string[] = []
    if (available.length > 0 && totalImportable === 0) parts.push(t('settings.import.sources.none'))
    if (settings?.lastSyncAt) {
      const time = new Intl.DateTimeFormat(locale, { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' })
        .format(new Date(settings.lastSyncAt))
      parts.push(t('settings.import.sources.lastSynced', { time }))
    }
    if (parts.length > 0) status = { text: parts.join(' · ') }
  }

  let syncDescription: ReactNode = t('settings.import.sync.description')
  if (!settings && settingsError) {
    syncDescription = <span className={styles.error}>{t('settings.import.sync.loadFailed', { error: settingsError })}</span>
  } else if (settings?.workspaceOptOut) {
    syncDescription = t('settings.import.sync.optOut')
  }

  return (
    <SettingsPanelShell title={t('settings.tab.import')} description={t('settings.import.description')}>
      <SettingsGroup title={t('settings.import.sync.group')}>
        <SettingsRow
          label={t('settings.import.sync.label')}
          description={syncDescription}
          control={
            <PillSwitch
              checked={syncOn}
              disabled={!settings || settings.workspaceOptOut || savingSync}
              aria-busy={savingSync}
              aria-label={t('settings.import.sync.label')}
              onChange={(next) => void handleSyncChange(next)}
            />
          }
        />
      </SettingsGroup>

      <SettingsGroup
        title={t('settings.import.sources.group')}
        headerAction={
          <div className={styles.headerStatus}>
            <span
              role="status"
              className={styles.status}
              data-tone={status?.error ? 'error' : undefined}
              title={status?.text}
            >
              {status?.text}
            </span>
            <Button
              iconLeft={<RefreshCw size={15} aria-hidden />}
              disabled={detecting}
              onClick={() => void detect()}
            >
              {t('settings.import.sources.checkAgain')}
            </Button>
          </div>
        }
      >
        {sources == null && detectError ? (
          <SettingsRow>
            <div className={styles.placeholder} data-tone="error">
              {t('settings.import.sources.loadFailed', { error: detectError })}
            </div>
          </SettingsRow>
        ) : sources == null ? (
          [0, 1].map((index) => (
            <SettingsRow
              key={index}
              label={<Skeleton width={index === 0 ? '28%' : '20%'} height={13} />}
              description={<Skeleton width="40%" height={11} />}
              control={<Skeleton width={72} height={28} radius={10} />}
            />
          ))
        ) : available.length === 0 ? (
          <SettingsRow>
            <div className={styles.placeholder}>{t('settings.import.sources.empty')}</div>
          </SettingsRow>
        ) : (
          available.map((entry) => {
            const label = importSourceLabel(entry.source)
            const progress = running?.source === entry.source ? running : null
            return (
              <SettingsRow key={entry.source}>
                <div className={styles.sourceRow}>
                  <ImportSourceIcon source={entry.source} />
                  <div className={styles.sourceText}>
                    <span style={settingsLabelStyle()}>{label}</span>
                    <span style={settingsHintStyle()}>
                      {progress
                        ? t('settings.import.source.progress', { completed: progress.completed, total: progress.total })
                        : readyText(entry.importableCount, t)}
                    </span>
                  </div>
                  <Button
                    aria-label={t('settings.import.source.importFrom', { source: label })}
                    loading={progress != null}
                    disabled={entry.importableCount === 0 || running != null}
                    onClick={() => setDialogSource(entry.source)}
                  >
                    {t('settings.import.source.import')}
                  </Button>
                </div>
              </SettingsRow>
            )
          })
        )}
      </SettingsGroup>

      {dialogSource && (
        <ImportSessionsDialog
          source={dialogSource}
          sourceLabel={importSourceLabel(dialogSource)}
          count={dialogCount}
          initialKeepInSync={settings ? settings.syncEnabled || !settings.lastSyncAt : false}
          syncLocked={!settings || settings.workspaceOptOut}
          syncNote={settings?.workspaceOptOut ? t('settings.import.sync.optOut') : undefined}
          onConfirm={(keepInSync) => handleImport(dialogSource, dialogCount, keepInSync)}
          onClose={() => setDialogSource(null)}
        />
      )}
    </SettingsPanelShell>
  )
}

/** Importing a source also makes it a sync source, so the sync list covers every app imported from. */
function syncUpdate(settings: ImportSettings, source: string, keepInSync: boolean): ImportSettingsSetParams | null {
  const tracked = settings.sources.includes(source)
  if (settings.syncEnabled === keepInSync && tracked) return null
  return { syncEnabled: keepInSync, sources: tracked ? settings.sources : [...settings.sources, source] }
}

function readyText(count: number, t: Translate): string {
  if (count === 0) return t('settings.import.source.none')
  return t(count === 1 ? 'settings.import.source.ready.one' : 'settings.import.source.ready.other', { count })
}

function announceCompletion(result: ImportSessionsCompletedNotification, t: Translate): void {
  const tally = (status: string): number => result.outcomes.filter((outcome) => outcome.status === status).length
  const imported = tally('imported')
  const updated = tally('appended')
  const failed = tally('failed')
  const parts = [
    imported > 0 ? t('settings.import.toast.imported', { count: imported }) : null,
    updated > 0 ? t('settings.import.toast.updated', { count: updated }) : null,
    failed > 0 ? t('settings.import.toast.failed', { count: failed }) : null
  ].filter((part): part is string => part !== null)
  if (parts.length === 0) {
    if (result.trigger === 'manual') showToast({ message: t('settings.import.toast.nothing') })
    return
  }
  showToast({
    type: failed > 0 ? 'warning' : 'success',
    message: t(failed > 0 ? 'settings.import.toast.partial' : 'settings.import.toast.done'),
    description: parts.join(' · ')
  })
}

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error)
}
