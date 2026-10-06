import { useCallback, useEffect, useMemo, useState } from 'react'
import { useT } from '../../../../contexts/LocaleContext'
import { readConfigValue, useConfig, writeConfig } from '../../../../stores/configStore'
import { addToast } from '../../../../stores/toastStore'
import { useConfirmDialog } from '../../../ui/ConfirmDialog'
import {
  DREAMS_INTERVAL_OPTIONS,
  DREAMS_THREAD_LOOKBACK_OPTIONS,
  delay,
  formatDreamsIntervalForConfig,
  normalizeDreamsInterval,
  normalizeDreamsRunList,
  normalizeDreamsStatus,
  type DreamsRunState,
  type DreamsStatus
} from './dreamsModel'

interface UseDreamsSettingsOptions {
  available: boolean
  personalizationActive: boolean
  dreamsPageActive: boolean
  dashboardUrl: string | null | undefined
}

export type DreamsSettings = ReturnType<typeof useDreamsSettings>

export function useDreamsSettings({
  available,
  personalizationActive,
  dreamsPageActive,
  dashboardUrl
}: UseDreamsSettingsOptions) {
  const t = useT()
  const confirm = useConfirmDialog()
  const config = useConfig()
  const enabled = readConfigValue(config, 'Dreams.Enabled') === true
  const runInterval = normalizeDreamsInterval(readConfigValue(config, 'Dreams.Interval')) ?? ''
  const lookback = readConfigValue(config, 'Dreams.ThreadLookbackCount')
  const threadLookbackCount = typeof lookback === 'number' ? lookback : null
  const autoApply = readConfigValue(config, 'Dreams.AutoApply') === true
  const [status, setStatus] = useState<DreamsStatus | null>(null)
  const [runs, setRuns] = useState<DreamsRunState[]>([])
  const [runsLoading, setRunsLoading] = useState(false)
  const [archivingRunId, setArchivingRunId] = useState<string | null>(null)
  const [archivingAll, setArchivingAll] = useState(false)
  const [applying, setApplying] = useState(false)
  const [running, setRunning] = useState(false)

  const reloadStatus = useCallback(async (): Promise<void> => {
    if (!available) {
      setStatus(null)
      return
    }

    try {
      const result = await window.api.appServer.sendRequest('dreams/status', {}, 20_000)
      setStatus(normalizeDreamsStatus(result))
    } catch (err) {
      addToast(t('settings.personalization.dreamsStatusFailed', {
        error: err instanceof Error ? err.message : String(err)
      }), 'error')
    }
  }, [available, t])

  const reloadRuns = useCallback(async (): Promise<void> => {
    if (!available) {
      setRuns([])
      return
    }

    setRunsLoading(true)
    try {
      const result = await window.api.appServer.sendRequest('dreams/list', {}, 20_000)
      setRuns(normalizeDreamsRunList(result))
    } catch (err) {
      addToast(t('settings.dreams.loadFailed', {
        error: err instanceof Error ? err.message : String(err)
      }), 'error')
    } finally {
      setRunsLoading(false)
    }
  }, [available, t])

  useEffect(() => {
    if (available && (personalizationActive || dreamsPageActive)) {
      void reloadStatus()
    }
  }, [available, dreamsPageActive, personalizationActive, reloadStatus])

  useEffect(() => {
    if (available && dreamsPageActive) {
      void reloadRuns()
    }
  }, [available, dreamsPageActive, reloadRuns])

  const updateConfig = useCallback(async (keyPath: string, value: unknown): Promise<void> => {
    setApplying(true)
    try {
      const saved = await writeConfig([{ keyPath, value }], (error) =>
        t('settings.personalization.dreamsSaveFailed', { error }))
      if (saved) await reloadStatus()
    } finally {
      setApplying(false)
    }
  }, [reloadStatus, t])

  const toggleEnabled = useCallback(async (checked: boolean): Promise<void> => {
    await updateConfig('Dreams.Enabled', checked)
  }, [updateConfig])

  const toggleAutoApply = useCallback(async (checked: boolean): Promise<void> => {
    if (checked && !autoApply) {
      const confirmed = await confirm({
        title: t('settings.personalization.dreamsAutoApply.warningTitle'),
        message: t('settings.personalization.dreamsAutoApply.warningBody'),
        confirmLabel: t('settings.personalization.dreamsAutoApply.warningConfirm'),
        cancelLabel: t('common.cancel'),
        danger: true
      })
      if (!confirmed) return
    }

    await updateConfig('Dreams.AutoApply', checked)
  }, [autoApply, confirm, t, updateConfig])

  const changeInterval = useCallback(async (next: string): Promise<void> => {
    await updateConfig('Dreams.Interval', formatDreamsIntervalForConfig(next))
  }, [updateConfig])

  const changeThreadLookback = useCallback(async (next: number): Promise<void> => {
    await updateConfig('Dreams.ThreadLookbackCount', next)
  }, [updateConfig])

  const runNow = useCallback(async (): Promise<void> => {
    if (running) return

    setRunning(true)
    try {
      const result = await window.api.appServer.sendRequest('dreams/run', {}, 20_000)
      let next = normalizeDreamsStatus(result)
      setStatus(next)
      for (let attempt = 0; next.running && attempt < 12; attempt++) {
        await delay(1500)
        next = normalizeDreamsStatus(await window.api.appServer.sendRequest('dreams/status', {}, 20_000))
        setStatus(next)
      }
      if (next.lastRun?.status === 'failed') {
        addToast(t('settings.personalization.dreamsRunFailed', {
          error: next.lastRun.message ?? t('settings.personalization.dreamsStatus.failed')
        }), 'error')
      } else if (next.lastRun?.status === 'skipped') {
        addToast(t('settings.personalization.dreamsRunSkipped'), 'info')
      } else if (next.lastRun?.status === 'succeeded') {
        addToast(t('settings.personalization.dreamsRunSucceeded'), 'success')
      }
      await reloadRuns()
    } catch (err) {
      const msg = err instanceof Error ? err.message : String(err)
      addToast(t('settings.personalization.dreamsRunFailed', { error: msg }), 'error')
    } finally {
      setRunning(false)
    }
  }, [reloadRuns, running, t])

  const openReview = useCallback(async (runId: string): Promise<void> => {
    if (!dashboardUrl) return
    const baseUrl = dashboardUrl.replace(/#.*$/, '')
    await window.api.shell.openExternal(`${baseUrl}#dreams/run/${encodeURIComponent(runId)}`)
  }, [dashboardUrl])

  const archiveRun = useCallback(async (run: DreamsRunState): Promise<void> => {
    if (run.status === 'running' || archivingRunId != null || archivingAll) return

    const confirmed = await confirm({
      title: t('settings.dreams.archiveConfirmTitle'),
      message: t('settings.dreams.archiveConfirmMessage'),
      confirmLabel: t('settings.dreams.archive'),
      cancelLabel: t('common.cancel')
    })
    if (!confirmed) return

    setArchivingRunId(run.id)
    try {
      await window.api.appServer.sendRequest('dreams/archive', { runId: run.id }, 20_000)
      addToast(t('settings.dreams.archiveSucceeded'), 'success')
      await reloadRuns()
    } catch (err) {
      addToast(t('settings.dreams.actionFailed', {
        error: err instanceof Error ? err.message : String(err)
      }), 'error')
    } finally {
      setArchivingRunId(null)
    }
  }, [archivingAll, archivingRunId, confirm, reloadRuns, t])

  const archiveBusy = archivingRunId != null || archivingAll
  const archiveAllDisabled =
    runs.length === 0 ||
    runsLoading ||
    archiveBusy ||
    runs.some((run) => run.status === 'running')

  const archiveAll = useCallback(async (): Promise<void> => {
    if (archiveAllDisabled) return

    const confirmed = await confirm({
      title: t('settings.dreams.archiveAllConfirmTitle'),
      message: t('settings.dreams.archiveAllConfirmMessage', { count: runs.length }),
      confirmLabel: t('settings.dreams.archiveAll'),
      cancelLabel: t('common.cancel')
    })
    if (!confirmed) return

    setArchivingAll(true)
    let archivedCount = 0
    let firstError = ''
    try {
      for (const run of runs) {
        try {
          await window.api.appServer.sendRequest('dreams/archive', { runId: run.id }, 20_000)
          archivedCount += 1
        } catch (err) {
          if (!firstError) {
            firstError = err instanceof Error ? err.message : String(err)
          }
        }
      }

      if (archivedCount === runs.length) {
        addToast(t('settings.dreams.archiveAllSucceeded', { count: archivedCount }), 'success')
      } else if (archivedCount > 0) {
        addToast(t('settings.dreams.archiveAllPartial', {
          archived: archivedCount,
          total: runs.length
        }), 'warning')
      } else {
        addToast(t('settings.dreams.actionFailed', { error: firstError }), 'error')
      }
      await reloadRuns()
    } finally {
      setArchivingAll(false)
    }
  }, [archiveAllDisabled, confirm, reloadRuns, runs, t])

  const intervalOptions = useMemo(() => {
    return !runInterval || DREAMS_INTERVAL_OPTIONS.includes(runInterval as typeof DREAMS_INTERVAL_OPTIONS[number])
      ? [...DREAMS_INTERVAL_OPTIONS]
      : [runInterval, ...DREAMS_INTERVAL_OPTIONS]
  }, [runInterval])

  const threadLookbackOptions = useMemo(() => {
    return threadLookbackCount == null
      || DREAMS_THREAD_LOOKBACK_OPTIONS.includes(threadLookbackCount as typeof DREAMS_THREAD_LOOKBACK_OPTIONS[number])
      ? [...DREAMS_THREAD_LOOKBACK_OPTIONS]
      : [threadLookbackCount, ...DREAMS_THREAD_LOOKBACK_OPTIONS]
  }, [threadLookbackCount])

  return {
    enabled,
    interval: runInterval,
    threadLookbackCount,
    autoApply,
    status,
    runs,
    runsLoading,
    settingsBusy: applying || running,
    running: running || status?.running === true,
    archiveBusy,
    archiveAllDisabled,
    intervalOptions,
    threadLookbackOptions,
    reloadStatus,
    reloadRuns,
    toggleEnabled,
    toggleAutoApply,
    changeInterval,
    changeThreadLookback,
    runNow,
    openReview,
    archiveRun,
    archiveAll
  }
}
