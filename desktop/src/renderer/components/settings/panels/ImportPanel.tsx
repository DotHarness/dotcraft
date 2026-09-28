import { useCallback, useEffect, useRef, useState, type JSX, type ReactNode } from 'react'
import { RefreshCw } from 'lucide-react'
import type {
  ImportCompletedNotification,
  ImportSettings,
  ImportSettingsSetParams,
  ImportSourceDetection,
  ImportSelection,
  ImportItemReference
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
import { ImportItemsDialog } from './ImportItemsDialog'
import { ImportSourceIcon } from './ImportSourceIcon'
import { ImportSyncDialog } from './ImportSyncDialog'
import { ImportHistory } from './ImportHistory'
import { syncSelectionSummary } from './importPresentation'
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
  const workspaceGeneration = useRef(0)
  const [customize, setCustomize] = useState(false)
  const [historyRevision, setHistoryRevision] = useState(0)

  const loadSettings = useCallback(async () => {
    const generation = workspaceGeneration.current
    try {
      const result = await window.api.appServer.sendRequest('import/settings/get', {})
      if (generation !== workspaceGeneration.current) return
      setSettings(result.settings)
      setSettingsError(null)
    } catch (error) {
      if (generation === workspaceGeneration.current) setSettingsError(errorMessage(error))
    }
  }, [])

  const detect = useCallback(async () => {
    const request = ++detectRequest.current
    setDetecting(true)
    try {
      const result = await window.api.appServer.sendRequest('import/detect', {}, DETECT_TIMEOUT_MS)
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
    workspaceGeneration.current++
    setDialogSource(null)
    setCustomize(false)
    setRunning(null)
    setSources(null)
    setSettings(null)
    setHistoryRevision(value => value + 1)
    void loadSettings()
    void detect()
  }, [detect, loadSettings, workspacePath])

  useEffect(() => window.api.appServer.onNotification((payload) => {
    if (payload.foreground === false) return
    if (payload.method === 'import/progress') {
      setRunning(payload.params)
    } else if (payload.method === 'import/completed') {
      setRunning(null)
      setHistoryRevision(value => value + 1)
      void detect()
      void loadSettings()
      announceCompletion(payload.params, t)
    }
  }), [detect, loadSettings, t])

  async function saveSettings(params: ImportSettingsSetParams): Promise<void> {
    const generation = workspaceGeneration.current
    const result = await window.api.appServer.sendRequest('import/settings/set', params)
    if (generation === workspaceGeneration.current) setSettings(result.settings)
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

  async function handleImport(source: string, total: number, selection: ImportSelection, items: ImportItemReference[]): Promise<void> {
    const generation = workspaceGeneration.current
    if (settings && !settings.sources.includes(source)) {
      await saveSettings({ sources: [...settings.sources, source] })
    }
    if (generation !== workspaceGeneration.current) return
    const started: RunningImport = { source, completed: 0, total }
    setRunning(started)
    try {
      await window.api.appServer.sendRequest('import/run', { sources: [source], selection, items })
    } catch (error) {
      if (generation !== workspaceGeneration.current) return
      const busy = readAppServerErrorFields(error).data?.code === 'import_busy'
      const next = busy ? { source: null, completed: 0, total: 0 } : null
      // Keep a notification that arrived first; overwriting its completion would leave the panel stuck importing.
      setRunning((current) => (current === started ? next : current))
      if (!busy) throw error
    }
    if (generation === workspaceGeneration.current) setDialogSource(null)
  }

  const available = sources?.filter((entry) => entry.available) ?? []
  const totalImportable = available.reduce((sum, entry) => sum + entry.importableCount, 0)
  const runningElsewhere = running != null && !available.some((entry) => entry.source === running.source)
  const syncOn = settings?.syncEnabled === true && !settings.workspaceOptOut
  const dialogEntry = available.find((entry) => entry.source === dialogSource)
  const dialogCount = dialogEntry?.importableCount ?? 0

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
  } else if (settings?.hasImported && !settings.syncEnabled) {
    syncDescription = t('settings.import.sync.paused')
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
        <SettingsRow
          label={t('settings.import.setup.syncContent')}
          description={settings ? syncSelectionSummary(settings, locale, t) : undefined}
          control={
            <Button disabled={!settings?.hasImported} onClick={() => setCustomize(true)}>
              {t('settings.import.setup.customize')}
            </Button>
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
                    disabled={entry.items.length === 0 || running != null}
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

      <ImportHistory revision={historyRevision} />
      {customize && settings && (
        <ImportSyncDialog
          selection={settings.selection}
          onSave={selection => saveSettings({ selection })}
          onClose={() => setCustomize(false)}
        />
      )}
      {dialogSource && (
        <ImportItemsDialog
          source={dialogSource}
          items={dialogEntry?.items ?? []}
          workspaceName={workspacePath?.split(/[\\/]/).filter(Boolean).at(-1) ?? ''}
          workspacePath={workspacePath}
          onConfirm={(selection, items) => handleImport(dialogSource, dialogCount, selection, items)}
          onClose={() => setDialogSource(null)}
        />
      )}
    </SettingsPanelShell>
  )
}

function readyText(count: number, t: Translate): string {
  if (count === 0) return t('settings.import.source.none')
  return t(count === 1 ? 'settings.import.source.ready.one' : 'settings.import.source.ready.other', { count })
}

function announceCompletion(result: ImportCompletedNotification, t: Translate): void {
  const tally = (status: string): number => result.outcomes.filter((outcome) => outcome.status === status).length
  const imported = tally('imported') + tally('attention')
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
