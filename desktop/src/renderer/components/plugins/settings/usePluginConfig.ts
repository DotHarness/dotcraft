import { useCallback, useEffect, useState } from 'react'
import type { DesktopPluginSettingsMutation } from '@dotcraft/plugin'
import { readAppServerErrorFields } from '../../../../shared/appServerError'
import {
  mutateDesktopPluginSettings,
  onDesktopPluginSettingsChange,
  readDesktopPluginSettings
} from '../../../plugins/desktopPluginSettings'
import { useConnectionStore } from '../../../stores/connectionStore'
import type { PluginConfigSnapshot } from './pluginConfigModel'

export interface PluginConfigState {
  declared: boolean
  snapshot: PluginConfigSnapshot | null
  loading: boolean
  failed: boolean
  pendingChange: { key: string; value: unknown } | null
  set: (key: string, value: unknown) => void
  reset: (key: string) => void
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

  const read = useCallback(async (): Promise<void> => {
    setLoading(true)
    try {
      const next = await readDesktopPluginSettings(pluginId)
      setSnapshot(next)
      setDeclared(next.schema.fields.length > 0)
      setFailed(false)
    } catch (error) {
      const notDeclared = readAppServerErrorFields(error).data?.code === 'PluginConfigurationNotDeclared'
      setDeclared(!notDeclared)
      setFailed(!notDeclared)
    } finally {
      setLoading(false)
    }
  }, [pluginId])

  useEffect(() => {
    setSnapshot(null)
    setDeclared(false)
    setFailed(false)
    if (!enabled) return
    void read()
    return onDesktopPluginSettingsChange(pluginId, setSnapshot)
  }, [connectionEpoch, enabled, pluginId, read])

  const mutate = useCallback((operation: DesktopPluginSettingsMutation, value: unknown) => {
    setPendingChange({ key: operation.key, value })
    setFailed(false)
    void mutateDesktopPluginSettings(pluginId, 'personal', [operation])
      .then(setSnapshot, () => setFailed(true))
      .finally(() => setPendingChange(null))
  }, [pluginId])

  const set = useCallback((key: string, value: unknown) => mutate({ op: 'set', key, value }, value), [mutate])
  const reset = useCallback((key: string) => mutate({ op: 'unset', key }, undefined), [mutate])
  const retry = useCallback(() => { void read() }, [read])

  return { declared, snapshot, loading, failed, pendingChange, set, reset, retry }
}
