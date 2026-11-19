import { useCallback, useEffect, useRef, useState, type JSX } from 'react'
import type {
  ImportSettings,
  ImportSettingsSetParams,
  ImportSourceDetection,
  ImportSelection,
  ImportItemReference
} from '@dotcraft/sdk/contracts'
import { useT } from '../../../contexts/LocaleContext'
import { readAppServerErrorFields } from '../../../../shared/appServerError'
import { addToast } from '../../../stores/toastStore'
import { SettingsPanelShell } from '../SettingsPanelShell'
import { ImportItemsDialog } from './ImportItemsDialog'
import { ImportSyncDialog } from './ImportSyncDialog'
import { ImportHistory } from './ImportHistory'
import { ImportSources, ImportSyncControls, type RunningImport } from './ImportPanelControls'
import { announceImportCompletion } from './importOutcomes'
import { LocalImportPanel } from './LocalImportPanel'

interface ImportPanelProps {
  workspacePath?: string
  local?: boolean
}

/** Detection parses every recent transcript of each source, which can outlast the default timeout. */
const DETECT_TIMEOUT_MS = 120_000

export function ImportPanel({ workspacePath, local }: ImportPanelProps): JSX.Element {
  return local ? <LocalImportPanel /> : <WorkspaceImportPanel workspacePath={workspacePath} />
}

function WorkspaceImportPanel({ workspacePath }: Pick<ImportPanelProps, 'workspacePath'>): JSX.Element {
  const t = useT()
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
      announceImportCompletion(payload.params, t)
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

  async function handleImport(
    source: string,
    total: number,
    selection: ImportSelection,
    offered: ImportSelection,
    items: ImportItemReference[]
  ): Promise<void> {
    const generation = workspaceGeneration.current
    const started: RunningImport = { source, completed: 0, total }
    setRunning(started)
    try {
      await window.api.appServer.sendRequest('import/run', { sources: [source], selection, offered, items })
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

  const dialogEntry = sources?.find(entry => entry.available && entry.source === dialogSource)
  const dialogCount = dialogEntry?.importableCount ?? 0

  return (
    <SettingsPanelShell title={t('settings.tab.import')} description={t('settings.import.description')}>
      <ImportSyncControls
        settings={settings}
        settingsError={settingsError}
        savingSync={savingSync}
        onSyncChange={next => void handleSyncChange(next)}
        onCustomize={() => setCustomize(true)}
      />
      <ImportSources
        sources={sources}
        detecting={detecting}
        detectError={detectError}
        running={running}
        lastSyncAt={settings?.lastSyncAt}
        onRefresh={() => void detect()}
        onSelect={setDialogSource}
      />
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
          onConfirm={(selection, offered, items) => handleImport(dialogSource, dialogCount, selection, offered, items)}
          onClose={() => setDialogSource(null)}
        />
      )}
    </SettingsPanelShell>
  )
}

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error)
}
