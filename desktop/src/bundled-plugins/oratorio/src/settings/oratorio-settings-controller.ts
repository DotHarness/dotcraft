import { useCallback, useEffect, useRef, useState } from 'react'
import { useOratorioSettingsT } from './oratorio-settings-i18n'
import { cloneSettings, createDefaultOratorioSettings, type OratorioSettingsConfig, type SourceProvider } from './oratorio-settings-model'
import { loadOratorioSettings, saveOratorioSettings, saveOratorioSyncSchedule, type LoadedOratorioSettings } from './oratorio-settings-service'

export type SettingsLoadState = 'loading' | 'ready' | 'failed'
export type NotifySettingsError = (message: string, retry?: () => void) => void
export type SettingsController = ReturnType<typeof useSettingsController>
export interface ConnectionSave {
  settings: OratorioSettingsConfig
  detectGitHubInstallations: boolean
}

const SAVE_DELAY_MS = 500

export function readPath(config: OratorioSettingsConfig, path: string): unknown {
  if (path === 'configuration') return config
  return path.split('.').reduce<unknown>((value, key) => (value as Record<string, unknown>)[key], config)
}

export function writePath(config: OratorioSettingsConfig, path: string, value: unknown): OratorioSettingsConfig {
  if (path === 'configuration') return cloneSettings(value as OratorioSettingsConfig)
  const next = cloneSettings(config)
  const keys = path.split('.')
  let target = next as unknown as Record<string, unknown>
  for (const key of keys.slice(0, -1)) target = target[key] as Record<string, unknown>
  target[keys[keys.length - 1]] = structuredClone(value)
  return next
}

function scheduleProvider(path: string): SourceProvider | null {
  if (path === 'github.syncIntervalSeconds') return 'github'
  return path === 'gitlab.syncIntervalSeconds' ? 'gitlab' : null
}

export function useSettingsController(readOnly: boolean, notifyError: NotifySettingsError) {
  const t = useOratorioSettingsT()
  const [draft, setDraft] = useState(createDefaultOratorioSettings)
  const [status, setStatus] = useState<SettingsLoadState>('loading')
  const [restartRequired, setRestartRequired] = useState(false)
  const draftRef = useRef(draft)
  const confirmedRef = useRef(cloneSettings(draft))
  const serverConfigRef = useRef<Record<string, unknown>>({})
  const statusRef = useRef<SettingsLoadState>('loading')
  const versionsRef = useRef(new Map<string, number>())
  const pendingRef = useRef(new Map<string, { timer: number; save: () => void }>())
  const queueRef = useRef<Promise<unknown>>(Promise.resolve())
  const mountedRef = useRef(false)

  const publish = useCallback((next: OratorioSettingsConfig) => {
    draftRef.current = next
    setDraft(next)
  }, [])

  const setLoadState = useCallback((next: SettingsLoadState) => {
    statusRef.current = next
    setStatus(next)
  }, [])

  const enqueue = useCallback(<T,>(task: () => Promise<T>): Promise<T> => {
    const run = queueRef.current.then(task, task)
    queueRef.current = run.catch(() => undefined)
    return run
  }, [])

  // The server answer is authoritative; only edits made after the request left survive it.
  const adopt = useCallback((loaded: LoadedOratorioSettings, sentVersions: ReadonlyMap<string, number>) => {
    loaded.settings.github.syncIntervalSeconds = confirmedRef.current.github.syncIntervalSeconds
    loaded.settings.gitlab.syncIntervalSeconds = confirmedRef.current.gitlab.syncIntervalSeconds
    confirmedRef.current = cloneSettings(loaded.settings)
    serverConfigRef.current = loaded.serverConfiguration
    let next = cloneSettings(loaded.settings)
    for (const [path, version] of versionsRef.current) {
      if (sentVersions.get(path) !== version) next = writePath(next, path, readPath(draftRef.current, path))
    }
    next.github.syncIntervalSeconds = draftRef.current.github.syncIntervalSeconds
    next.gitlab.syncIntervalSeconds = draftRef.current.gitlab.syncIntervalSeconds
    publish(next)
    setRestartRequired(loaded.restartRequired)
  }, [publish])

  const load = useCallback(() => {
    setLoadState('loading')
    loadOratorioSettings().then((loaded) => {
      if (!mountedRef.current) return
      confirmedRef.current = cloneSettings(loaded.settings)
      serverConfigRef.current = loaded.serverConfiguration
      publish(cloneSettings(loaded.settings))
      setRestartRequired(loaded.restartRequired)
      setLoadState('ready')
    }, () => {
      if (mountedRef.current) setLoadState('failed')
    })
  }, [publish, setLoadState])

  useEffect(() => {
    mountedRef.current = true
    load()
    const pending = pendingRef.current
    return () => {
      mountedRef.current = false
      // Leaving the page must not drop an edit that is still waiting out its debounce.
      for (const { timer, save } of [...pending.values()]) {
        window.clearTimeout(timer)
        save()
      }
    }
  }, [load])

  const change = useCallback((path: string, value: unknown, onSaved?: () => void) => {
    if (readOnly || statusRef.current !== 'ready') return
    publish(writePath(draftRef.current, path, value))
    const version = (versionsRef.current.get(path) ?? 0) + 1
    versionsRef.current.set(path, version)
    const previous = pendingRef.current.get(path)
    if (previous) window.clearTimeout(previous.timer)
    const save = (): void => {
      pendingRef.current.delete(path)
      void enqueue(async () => {
        if (versionsRef.current.get(path) !== version) return
        try {
          const provider = scheduleProvider(path)
          if (provider) {
            await saveOratorioSyncSchedule(provider, value as number | null)
            confirmedRef.current = writePath(confirmedRef.current, path, value)
          } else {
            const sent = new Map(versionsRef.current)
            adopt(await saveOratorioSettings(draftRef.current, serverConfigRef.current), sent)
          }
          onSaved?.()
        } catch {
          try {
            const latest = await loadOratorioSettings()
            confirmedRef.current = cloneSettings(latest.settings)
            serverConfigRef.current = latest.serverConfiguration
            setRestartRequired(latest.restartRequired)
          } catch {
            // Keep the last confirmed snapshot while the service is unavailable.
          }
          const restored = writePath(draftRef.current, path, readPath(confirmedRef.current, path))
          restored.revision = confirmedRef.current.revision
          publish(restored)
          notifyError(t('saveFailed'), () => change(path, value, onSaved))
        }
      })
    }
    pendingRef.current.set(path, { timer: window.setTimeout(save, SAVE_DELAY_MS), save })
  }, [adopt, enqueue, notifyError, publish, readOnly, t])

  /** Saves a whole connection immediately, building it from the draft that is current when its turn in the save queue comes. */
  const saveConnection = useCallback(<T extends ConnectionSave>(build: (snapshot: OratorioSettingsConfig) => T): Promise<{ loaded: LoadedOratorioSettings; built: T }> => {
    if (readOnly) return Promise.reject(new Error('oratorio.settings.read_only'))
    return enqueue(async () => {
      const built = build(draftRef.current)
      const sent = new Map(versionsRef.current)
      const loaded = await saveOratorioSettings(built.settings, serverConfigRef.current, { detectGitHubInstallations: built.detectGitHubInstallations })
      adopt(loaded, sent)
      return { loaded, built }
    })
  }, [adopt, enqueue, readOnly])

  const saveSchedule = useCallback((provider: SourceProvider, intervalSeconds: number | null) => enqueue(async () => {
    await saveOratorioSyncSchedule(provider, intervalSeconds)
    const path = `${provider}.syncIntervalSeconds`
    confirmedRef.current = writePath(confirmedRef.current, path, intervalSeconds)
    publish(writePath(draftRef.current, path, intervalSeconds))
  }), [enqueue, publish])

  return { draft, status, restartRequired, reload: load, change, saveConnection, saveSchedule, snapshot: () => draftRef.current }
}
