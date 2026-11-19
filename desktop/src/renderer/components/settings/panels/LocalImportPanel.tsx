import { useCallback, useEffect, useRef, useState, type JSX } from 'react'
import type { ImportSettingsSetParams } from '@dotcraft/sdk/contracts'
import type { LocalImportOutcome, LocalImportRunParams, LocalImportSource, LocalImportState } from '../../../../shared/localImport'
import { isRemoteProjectKey, sameWorkspaceProjectKey } from '../../../../shared/workspaceProjectKey'
import { useT } from '../../../contexts/LocaleContext'
import { addToast } from '../../../stores/toastStore'
import { useWorkspaceProjectsStore } from '../../../stores/workspaceProjectsStore'
import { openThread } from '../../../utils/desktopNavigation'
import { SettingsEmptyRow, SettingsGroup } from '../SettingsGroup'
import { SettingsPanelShell } from '../SettingsPanelShell'
import { ImportHistoryContent } from './ImportHistory'
import { ImportSources, ImportSyncControls, type RunningImport } from './ImportPanelControls'
import { ImportSyncDialog } from './ImportSyncDialog'
import { LocalImportItemsDialog } from './LocalImportItemsDialog'
import { announceImportCompletion, openOutcome } from './importOutcomes'

const ACTIVE_REFRESH_MS = 750
const IDLE_REFRESH_MS = 4_000

export function LocalImportPanel(): JSX.Element {
  const t = useT()
  const [state, setState] = useState<LocalImportState | null>(null)
  const [stateError, setStateError] = useState<string | null>(null)
  const [sources, setSources] = useState<LocalImportSource[] | null>(null)
  const [detecting, setDetecting] = useState(false)
  const [detectError, setDetectError] = useState<string | null>(null)
  const [savingSync, setSavingSync] = useState(false)
  const [customize, setCustomize] = useState(false)
  const [dialog, setDialog] = useState<LocalImportSource | null>(null)
  const [starting, setStarting] = useState<RunningImport | null>(null)
  const stateRequest = useRef(0)
  const detectionRequest = useRef(0)
  const observed = useRef<{ batchId: string | null; runningId: string | null } | null>(null)
  const requested = useRef<string | null>(null)

  const refreshState = useCallback(async () => {
    const request = ++stateRequest.current
    try {
      const next = await window.api.imports.getState()
      if (request !== stateRequest.current) return
      setState(next)
      setStateError(null)
    } catch (err) {
      if (request === stateRequest.current) setStateError(errorMessage(err))
    }
  }, [])

  const detect = useCallback(async () => {
    const request = ++detectionRequest.current
    setDetecting(true)
    try {
      const result = await window.api.imports.detect()
      if (request !== detectionRequest.current) return
      setSources(result.sources)
      setDetectError(null)
    } catch (err) {
      if (request === detectionRequest.current) setDetectError(errorMessage(err))
    } finally {
      if (request === detectionRequest.current) setDetecting(false)
    }
  }, [])

  const running = state?.running ?? starting
  const isRunning = running != null
  const enabled = state?.enabled === true

  useEffect(() => {
    let cancelled = false
    let timer: number | undefined
    async function poll(): Promise<void> {
      await refreshState()
      if (!cancelled) timer = window.setTimeout(() => void poll(), isRunning ? ACTIVE_REFRESH_MS : IDLE_REFRESH_MS)
    }
    void poll()
    return () => {
      cancelled = true
      window.clearTimeout(timer)
    }
  }, [isRunning, refreshState])

  useEffect(() => {
    if (enabled) void detect()
  }, [enabled, detect])

  useEffect(() => {
    if (!state) return
    const batch = state.imports[0]
    const latest = { batchId: batch?.importId ?? null, runningId: state.running?.importId ?? null }
    const previous = observed.current
    observed.current = latest
    if (!batch || !previous || latest.batchId === previous.batchId) return
    if (enabled) void detect()
    if (requested.current === batch.importId || previous.runningId === batch.importId) {
      if (requested.current === batch.importId) requested.current = null
      announceImportCompletion(batch, t)
    }
  }, [state, t, enabled, detect])

  async function saveSettings(params: ImportSettingsSetParams): Promise<void> {
    const { settings } = await window.api.imports.setSettings(params)
    stateRequest.current++
    setState(previous => previous ? { ...previous, settings } : previous)
    void refreshState()
  }

  async function changeSync(syncEnabled: boolean): Promise<void> {
    setSavingSync(true)
    try {
      await saveSettings({ syncEnabled })
    } catch (err) {
      addToast(t('settings.import.sync.saveFailed', { error: errorMessage(err) }), 'error')
    } finally {
      setSavingSync(false)
    }
  }

  async function run(params: LocalImportRunParams): Promise<void> {
    setStarting({ source: params.source, total: params.items.length, completed: 0 })
    try {
      const { importId } = await window.api.imports.run(params)
      requested.current = importId
      setDialog(null)
    } finally {
      await refreshState()
      setStarting(null)
    }
  }

  function onRefresh(): void {
    void refreshState()
    if (enabled) void detect()
  }

  return (
    <SettingsPanelShell title={t('settings.tab.import')} description={t('settings.import.local.description')}>
      {state?.enabled === false ? (
        <SettingsGroup title={t('settings.import.sources.group')}>
          <SettingsEmptyRow>{t('settings.import.local.disabled')}</SettingsEmptyRow>
        </SettingsGroup>
      ) : (
        <>
          <ImportSyncControls
            local
            settings={state?.settings ?? null}
            settingsError={stateError}
            savingSync={savingSync}
            onSyncChange={next => void changeSync(next)}
            onCustomize={() => setCustomize(true)}
          />
          <ImportSources
            local
            sources={sources}
            detecting={detecting}
            detectError={detectError ?? stateError}
            running={running}
            lastSyncAt={state?.settings.lastSyncAt}
            onRefresh={onRefresh}
            onSelect={source => setDialog(sources?.find(entry => entry.source === source) ?? null)}
          />
          <ImportHistoryContent
            batches={state?.imports ?? null}
            attention={state?.attention ?? []}
            failed={stateError != null}
            onRetry={onRefresh}
            local
            onOpen={outcome => {
              void openLocalImportOutcome(outcome).catch(err => addToast(t('settings.import.local.openFailed', { error: errorMessage(err) }), 'error'))
            }}
          />
        </>
      )}
      {enabled && dialog && <LocalImportItemsDialog entry={dialog} blocked={isRunning} onConfirm={run} onClose={() => setDialog(null)} />}
      {customize && state?.enabled && (
        <ImportSyncDialog local selection={state.settings.selection} onSave={selection => saveSettings({ selection })} onClose={() => setCustomize(false)} />
      )}
    </SettingsPanelShell>
  )
}

async function openLocalImportOutcome(outcome: LocalImportOutcome): Promise<void> {
  const { chat, foregroundProjectId } = useWorkspaceProjectsStore.getState()
  if (outcome.threadId) {
    await openThread(outcome.threadId, outcome.projectPath || chat?.path, { includeArchived: true })
    return
  }
  if (outcome.category === 'instructions' && outcome.targetPath) {
    await window.api.shell.revealLocalPath(outcome.targetPath)
    return
  }
  const target = outcome.projectPath || (isRemoteProjectKey(foregroundProjectId) ? chat?.path : null)
  if (target && !sameWorkspaceProjectKey(target, foregroundProjectId)) await window.api.workspace.switch(target)
  openOutcome(outcome)
}

function errorMessage(err: unknown): string {
  return err instanceof Error ? err.message : String(err)
}
