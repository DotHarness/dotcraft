import { useCallback, useMemo, useState } from 'react'
import type { ChannelId } from './channelDefs'
import { parseJsonRecordConfig } from '../../../shared/jsonConfig'

export interface WeComRobotConfig {
  Path: string
  Token: string
  AesKey: string
}

export interface WeComChannelConfig {
  Enabled: boolean
  Host: string
  Port: number
  Robots: WeComRobotConfig[]
}

export interface ChannelsConfigState {
  wecom: WeComChannelConfig
}

const DEFAULT_CONFIGS: ChannelsConfigState = {
  wecom: { Enabled: false, Host: '0.0.0.0', Port: 9000, Robots: [] }
}

function configPath(workspacePath: string): string {
  return `${workspacePath.replace(/[\\/]+$/, '')}/.craft/config.json`
}

function isMissingFileError(err: unknown): boolean {
  const message = err instanceof Error ? err.message : String(err)
  return message.includes('ENOENT') || message.includes('not found')
}

function asRecord(v: unknown): Record<string, unknown> {
  if (v == null || typeof v !== 'object' || Array.isArray(v)) return {}
  return v as Record<string, unknown>
}

function asString(v: unknown, fallback = ''): string {
  return typeof v === 'string' ? v : fallback
}

function asNumber(v: unknown, fallback: number): number {
  return typeof v === 'number' && Number.isFinite(v) ? v : fallback
}

function asBoolean(v: unknown, fallback = false): boolean {
  return typeof v === 'boolean' ? v : fallback
}

function parseConfigs(root: Record<string, unknown>): ChannelsConfigState {
  const wecom = asRecord(root.WeComBot)
  const robotsRaw = Array.isArray(wecom.Robots) ? wecom.Robots : []
  const robots: WeComRobotConfig[] = robotsRaw
    .map((r) => asRecord(r))
    .map((r) => ({
      Path: asString(r.Path),
      Token: asString(r.Token),
      AesKey: asString(r.AesKey)
    }))

  return {
    wecom: {
      Enabled: asBoolean(wecom.Enabled, DEFAULT_CONFIGS.wecom.Enabled),
      Host: asString(wecom.Host, DEFAULT_CONFIGS.wecom.Host),
      Port: asNumber(wecom.Port, DEFAULT_CONFIGS.wecom.Port),
      Robots: robots
    }
  }
}

async function readRoot(workspacePath: string): Promise<Record<string, unknown>> {
  const path = configPath(workspacePath)
  try {
    const raw = await window.api.file.readFile(path)
    return parseJsonRecordConfig(raw)
  } catch (err) {
    if (isMissingFileError(err)) return {}
    throw err
  }
}

async function writeRoot(workspacePath: string, root: Record<string, unknown>): Promise<void> {
  const path = configPath(workspacePath)
  await window.api.file.writeFile(path, `${JSON.stringify(root, null, 2)}\n`)
}

function mergeChannelConfig(
  root: Record<string, unknown>,
  channelId: ChannelId,
  config: ChannelsConfigState
): Record<string, unknown> {
  const next = { ...root }

  if (channelId === 'wecom') {
    next.WeComBot = {
      ...asRecord(next.WeComBot),
      ...config.wecom
    }
    return next
  }

  return next
}

export function useChannelConfig(workspacePath: string): {
  loading: boolean
  savingChannelId: ChannelId | null
  error: string | null
  config: ChannelsConfigState
  setChannelConfig: (channelId: ChannelId, nextValue: unknown) => void
  reload: () => Promise<void>
  saveChannel: (channelId: ChannelId) => Promise<void>
} {
  const [config, setConfig] = useState<ChannelsConfigState>(DEFAULT_CONFIGS)
  const [loading, setLoading] = useState(true)
  const [savingChannelId, setSavingChannelId] = useState<ChannelId | null>(null)
  const [error, setError] = useState<string | null>(null)

  const reload = useCallback(async () => {
    if (!workspacePath) return
    setLoading(true)
    setError(null)
    try {
      const root = await readRoot(workspacePath)
      setConfig(parseConfigs(root))
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    } finally {
      setLoading(false)
    }
  }, [workspacePath])

  const setChannelConfig = useCallback((channelId: ChannelId, nextValue: unknown) => {
    setConfig((prev) => ({
      ...prev,
      [channelId]: nextValue
    }))
  }, [])

  const saveChannel = useCallback(
    async (channelId: ChannelId) => {
      if (!workspacePath) return
      setSavingChannelId(channelId)
      setError(null)
      try {
        const root = await readRoot(workspacePath)
        const nextRoot = mergeChannelConfig(root, channelId, config)
        await writeRoot(workspacePath, nextRoot)
      } catch (err) {
        // Save errors are surfaced by the caller (toast); do not set `error` here or the
        // inline banner would show channels.loadFailed for a save failure.
        throw err
      } finally {
        setSavingChannelId(null)
      }
    },
    [workspacePath, config]
  )

  return useMemo(
    () => ({
      loading,
      savingChannelId,
      error,
      config,
      setChannelConfig,
      reload,
      saveChannel
    }),
    [loading, savingChannelId, error, config, setChannelConfig, reload, saveChannel]
  )
}
