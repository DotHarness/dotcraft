import { useCallback, useEffect, useRef, useState } from 'react'
import type { DesktopPluginSettingsMutation } from '@dotcraft/plugin'
import { readAppServerErrorFields } from '../../../../shared/appServerError'
import {
  mutateDesktopPluginSettings,
  onDesktopPluginSettingsChange,
  readDesktopPluginSettings
} from '../../../plugins/desktopPluginSettings'
import { useConnectionStore } from '../../../stores/connectionStore'
import { storedKey, type PluginConfigSnapshot } from './pluginConfigModel'

export interface PluginConfigState {
  declared: boolean
  snapshot: PluginConfigSnapshot | null
  loading: boolean
  failed: boolean
  pendingChange: { key: string | null; value: unknown } | null
  set: (key: string, value: unknown) => void
  resetAll: () => void
  retry: () => void
}

export function usePluginConfig(pluginId: string, active: boolean): PluginConfigState {
  const available = useConnectionStore((state) => state.capabilities?.pluginConfiguration === true)
  const connectionEpoch = useConnectionStore((state) => state.connectionEpoch)
  const enabled = active && available
  const [declared, setDeclared] = useState(false)
  const [snapshot, setSnapshot] = useState<PluginConfigSnapshot | null>(null)
  const [loading, setLoading] = useState(false)
  const [failed, setFailed] = useState(false)
  const [pendingChange, setPendingChange] = useState<PluginConfigState['pendingChange']>(null)
  const generation = useRef(0)

  const read = useCallback(async (): Promise<void> => {
    const current = generation.current
    setLoading(true)
    try {
      const next = await readDesktopPluginSettings(pluginId)
      if (generation.current !== current) return
      setSnapshot(next)
      setDeclared(next.schema.fields.length > 0)
      setFailed(false)
    } catch (error) {
      if (generation.current !== current) return
      const notDeclared = readAppServerErrorFields(error).data?.code === 'PluginConfigurationNotDeclared'
      setDeclared(!notDeclared)
      setFailed(!notDeclared)
    } finally {
      if (generation.current === current) setLoading(false)
    }
  }, [pluginId])

  useEffect(() => {
    setSnapshot(null)
    setDeclared(false)
    setFailed(false)
    setPendingChange(null)
    if (!enabled) return
    void read()
    const stop = onDesktopPluginSettingsChange(pluginId, setSnapshot)
    return () => {
      generation.current += 1
      stop()
    }
  }, [connectionEpoch, enabled, pluginId, read])

  const mutate = useCallback((
    operations: DesktopPluginSettingsMutation[],
    change: NonNullable<PluginConfigState['pendingChange']>
  ) => {
    const current = generation.current
    setPendingChange(change)
    setFailed(false)
    void mutateDesktopPluginSettings(pluginId, 'personal', operations)
      .then(
        (next) => { if (generation.current === current) setSnapshot(next) },
        () => { if (generation.current === current) setFailed(true) })
      .finally(() => { if (generation.current === current) setPendingChange(null) })
  }, [pluginId])

  const set = useCallback((key: string, value: unknown) => mutate([{ op: 'set', key, value }], { key, value }), [mutate])
  const resetAll = useCallback(() => {
    if (!snapshot) return
    const operations = snapshot.schema.fields
      .filter((field) => storedKey(snapshot.personal, field.key) !== undefined)
      .map((field): DesktopPluginSettingsMutation => ({ op: 'unset', key: field.key }))
    if (operations.length > 0) mutate(operations, { key: null, value: undefined })
  }, [mutate, snapshot])
  const retry = useCallback(() => { void read() }, [read])

  return { declared, snapshot, loading, failed, pendingChange, set, resetAll, retry }
}
