import { useCallback, useEffect, useMemo, useState } from 'react'
import { useT } from '../../../../contexts/LocaleContext'
import { addToast } from '../../../../stores/toastStore'
import { useConfirmDialog } from '../../../ui/ConfirmDialog'
import {
  DEFAULT_DREAMS_INTERVAL,
  DEFAULT_DREAMS_THREAD_LOOKBACK_COUNT,
  DREAMS_INTERVAL_OPTIONS,
  DREAMS_THREAD_LOOKBACK_OPTIONS,
  delay,
  normalizeDreamsRunList,
  normalizeDreamsStatus,
  type DreamsRunState,
  type DreamsStatus
} from './dreamsModel'

export interface DreamsConfigValues {
  enabled: boolean
  interval: string
  threadLookbackCount: number
  autoApply: boolean
}

interface UseDreamsSettingsOptions {
  available: boolean
  personalizationActive: boolean
  dreamsPageActive: boolean
  dashboardUrl: string | null | undefined
  reloadWorkspaceCore: () => Promise<void>
}

export type DreamsSettings = ReturnType<typeof useDreamsSettings>

export function useDreamsSettings({
  available,
  personalizationActive,
  dreamsPageActive,
  dashboardUrl,
  reloadWorkspaceCore
}: UseDreamsSettingsOptions) {
  const t = useT()
  const confirm = useConfirmDialog()
  const [enabled, setEnabled] = useState(false)
  const [runInterval, setRunInterval] = useState(DEFAULT_DREAMS_INTERVAL)
  const [threadLookbackCount, setThreadLookbackCount] = useState(DEFAULT_DREAMS_THREAD_LOOKBACK_COUNT)
  const [autoApply, setAutoApply] = useState(false)
  const [status, setStatus] = useState<DreamsStatus | null>(null)
  const [runs, setRuns] = useState<DreamsRunState[]>([])
  const [runsLoading, setRunsLoading] = useState(false)
  const [archivingRunId, setArchivingRunId] = useState<string | null>(null)
  const [archivingAll, setArchivingAll] = useState(false)
  const [applying, setApplying] = useState(false)
  const [running, setRunning] = useState(false)

  const applyConfig = useCallback((values: DreamsConfigValues): void => {
    setEnabled(values.enabled)
    setRunInterval(values.interval)
    setThreadLookbackCount(values.threadLookbackCount)
    setAutoApply(values.autoApply)
  }, [])

  const applyStatusSnapshot = useCallback((next: DreamsStatus): void => {
    setStatus(next)
    applyConfig(next)
  }, [applyConfig])

  const reloadStatus = useCallback(async (): Promise<void> => {
    if (!available) {
      setStatus(null)
      return
    }

    try {
      const result = await window.api.appServer.sendRequest('dreams/status', {}, 20_000)
      applyStatusSnapshot(normalizeDreamsStatus(result))
    } catch (err) {
      addToast(t('settings.personalization.dreamsStatusFailed', {
        error: err instanceof Error ? err.message : String(err)
      }), 'error')
    }
  }, [applyStatusSnapshot, available, t])

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

  const updateConfig = useCallback(
    async <T,>(
      params: Record<string, unknown>,
      previous: T,
      set: (value: T) => void,
      reloadCore = false
    ): Promise<void> => {
      setApplying(true)
      try {
        await window.api.appServer.sendRequest('workspace/config/update', params)
        if (reloadCore) await reloadWorkspaceCore()
        await reloadStatus()
      } catch (err) {
        const msg = err instanceof Error ? err.message : String(err)
        set(previous)
        addToast(t('settings.personalization.dreamsSaveFailed', { error: msg }), 'error')
      } finally {
        setApplying(false)
      }
    },
    [reloadStatus, reloadWorkspaceCore, t]
  )

  const toggleEnabled = useCallback(async (checked: boolean): Promise<void> => {
    const previous = enabled
    setEnabled(checked)
    await updateConfig({ dreamsEnabled: checked }, previous, setEnabled)
  }, [enabled, updateConfig])

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

    const previous = autoApply
    setAutoApply(checked)
    await updateConfig({ dreamsAutoApply: checked }, previous, setAutoApply, true)
  }, [autoApply, confirm, t, updateConfig])

  const changeInterval = useCallback(async (next: string): Promise<void> => {
    const previous = runInterval
    setRunInterval(next)
    await updateConfig({ dreamsInterval: next }, previous, setRunInterval)
  }, [runInterval, updateConfig])

  const changeThreadLookback = useCallback(async (next: number): Promise<void> => {
    const previous = threadLookbackCount
    setThreadLookbackCount(next)
    await updateConfig({ dreamsThreadLookbackCount: next }, previous, setThreadLookbackCount)
  }, [threadLookbackCount, updateConfig])

  const runNow = useCallback(async (): Promise<void> => {
    if (running) return

    setRunning(true)
    try {
      const result = await window.api.appServer.sendRequest('dreams/run', {}, 20_000)
      let next = normalizeDreamsStatus(result)
      applyStatusSnapshot(next)
      for (let attempt = 0; next.running && attempt < 12; attempt++) {
        await delay(1500)
        next = normalizeDreamsStatus(await window.api.appServer.sendRequest('dreams/status', {}, 20_000))
        applyStatusSnapshot(next)
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
  }, [applyStatusSnapshot, reloadRuns, running, t])

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
    return DREAMS_INTERVAL_OPTIONS.includes(runInterval as typeof DREAMS_INTERVAL_OPTIONS[number])
      ? [...DREAMS_INTERVAL_OPTIONS]
      : [runInterval, ...DREAMS_INTERVAL_OPTIONS]
  }, [runInterval])

  const threadLookbackOptions = useMemo(() => {
    return DREAMS_THREAD_LOOKBACK_OPTIONS.includes(threadLookbackCount as typeof DREAMS_THREAD_LOOKBACK_OPTIONS[number])
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
    applyConfig,
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
