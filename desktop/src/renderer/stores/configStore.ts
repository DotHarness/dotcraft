import { useCallback, useEffect } from 'react'
import { create } from 'zustand'
import type { JsonValue } from '@dotcraft/sdk/contracts'
import { useConnectionStore } from './connectionStore'
import { addToast } from './toastStore'

export type ConfigObject = Record<string, unknown>

export interface ConfigEdit {
  keyPath: string
  value: unknown
}

interface ConfigStore {
  config: ConfigObject | null
  pendingKeyPaths: string[]
  ensureLoaded(): Promise<void>
  refresh(): Promise<void>
  write(edits: ConfigEdit[]): Promise<void>
  handleConfigChanged(regions: string[]): void
  reset(): void
}

let scope = 0
let writeSeq = 0
let inFlight: Promise<void> | null = null

function isRecord(value: unknown): value is ConfigObject {
  return value != null && typeof value === 'object' && !Array.isArray(value)
}

function findKey(record: ConfigObject, segment: string): string | undefined {
  if (Object.prototype.hasOwnProperty.call(record, segment)) return segment
  const expected = segment.toLowerCase()
  return Object.keys(record).find((key) => key.toLowerCase() === expected)
}

export function splitConfigKeyPath(keyPath: string): string[] {
  const segments: string[] = []
  let segment = ''
  let quoted = false
  for (let i = 0; i < keyPath.length; i++) {
    const char = keyPath[i]
    if (quoted) {
      if (char === '\\' && i + 1 < keyPath.length) segment += keyPath[++i]
      else if (char === '"') quoted = false
      else segment += char
    } else if (char === '.') {
      segments.push(segment)
      segment = ''
    } else if (char === '"') {
      quoted = true
    } else {
      segment += char
    }
  }
  segments.push(segment)
  return segments
}

export function configKeyPath(...segments: string[]): string {
  return segments
    .map((segment) => (/["\\.]/.test(segment) ? `"${segment.replace(/["\\]/g, (char) => `\\${char}`)}"` : segment))
    .join('.')
}

export function readConfigValue(config: ConfigObject | null | undefined, keyPath: string): unknown {
  let current: unknown = config
  for (const segment of splitConfigKeyPath(keyPath)) {
    if (!isRecord(current)) return undefined
    const key = findKey(current, segment)
    if (key === undefined) return undefined
    current = current[key]
  }
  return current
}

function withConfigValue(config: ConfigObject, segments: string[], value: unknown): ConfigObject {
  const [segment, ...rest] = segments
  const key = findKey(config, segment) ?? segment
  const child = config[key]
  return {
    ...config,
    [key]: rest.length === 0 ? value : withConfigValue(isRecord(child) ? child : {}, rest, value)
  }
}

function applyEdits(config: ConfigObject, edits: ConfigEdit[]): ConfigObject {
  return edits.reduce((next, edit) => withConfigValue(next, splitConfigKeyPath(edit.keyPath), edit.value), config)
}

function removePending(pending: string[], keyPaths: string[]): string[] {
  const next = [...pending]
  for (const keyPath of keyPaths) {
    const index = next.indexOf(keyPath)
    if (index >= 0) next.splice(index, 1)
  }
  return next
}

export const useConfigStore = create<ConfigStore>((set, get) => ({
  config: null,
  pendingKeyPaths: [],

  ensureLoaded() {
    if (get().config != null) return Promise.resolve()
    return inFlight ?? get().refresh()
  },

  refresh() {
    const requestScope = scope
    const requestWriteSeq = writeSeq
    const request = window.api.appServer.sendRequest('config/read', {})
      .then((result) => {
        if (requestScope !== scope || requestWriteSeq !== writeSeq) return
        const config = (result as { config?: unknown } | null)?.config
        set({ config: isRecord(config) ? config : {} })
      }, () => undefined)
      .finally(() => {
        if (inFlight === request) inFlight = null
      })
    inFlight = request
    return request
  },

  async write(edits) {
    const keyPaths = edits.map((edit) => edit.keyPath)
    const writeScope = scope
    const previous = get().config ?? {}
    const restore = edits.map((edit) => ({ keyPath: edit.keyPath, value: readConfigValue(previous, edit.keyPath) }))
    writeSeq += 1
    set((state) => ({
      config: state.config == null ? null : applyEdits(state.config, edits),
      pendingKeyPaths: [...state.pendingKeyPaths, ...keyPaths]
    }))
    const wireEdits = edits.map((edit) => ({
      keyPath: edit.keyPath,
      value: edit.value as JsonValue,
      mergeStrategy: 'replace'
    }))
    try {
      if (wireEdits.length === 1) {
        await window.api.appServer.sendRequest('config/value/write', wireEdits[0], 20_000)
      } else {
        await window.api.appServer.sendRequest('config/batchWrite', { edits: wireEdits }, 20_000)
      }
    } catch (error) {
      if (writeScope === scope) {
        set((state) => ({ config: state.config == null ? null : applyEdits(state.config, restore) }))
      }
      throw error
    } finally {
      if (writeScope === scope) {
        set((state) => ({ pendingKeyPaths: removePending(state.pendingKeyPaths, keyPaths) }))
        await get().refresh()
      }
    }
  },

  handleConfigChanged(regions) {
    const config = get().config
    if (config == null) return
    if (regions.some((region) => findKey(config, region.split('.')[0]) !== undefined)) {
      void get().refresh()
    }
  },

  reset() {
    scope += 1
    inFlight = null
    set({ config: null, pendingKeyPaths: [] })
  }
}))

export async function writeConfig(
  edits: ConfigEdit[],
  failureMessage: (error: string) => string
): Promise<boolean> {
  try {
    await useConfigStore.getState().write(edits)
    return true
  } catch (error) {
    addToast(failureMessage(error instanceof Error ? error.message : String(error)), 'error')
    return false
  }
}

export function useConfig(): ConfigObject | null {
  const available = useConnectionStore((state) =>
    state.status === 'connected' && state.capabilities?.workspaceConfigManagement === true)
  const config = useConfigStore((state) => state.config)
  const loaded = config != null
  useEffect(() => {
    if (available && !loaded) void useConfigStore.getState().ensureLoaded()
  }, [available, loaded])
  return available ? config : null
}

export function useConfigValue(keyPath: string): unknown {
  return readConfigValue(useConfig(), keyPath)
}

export interface ConfigSetting {
  value: unknown
  pending: boolean
  set(value: unknown): Promise<boolean>
}

export function useConfigSetting(keyPath: string, failureMessage: (error: string) => string): ConfigSetting {
  const value = useConfigValue(keyPath)
  const pending = useConfigStore((state) => state.pendingKeyPaths.includes(keyPath))
  const setValue = useCallback(
    (next: unknown) => writeConfig([{ keyPath, value: next }], failureMessage),
    [failureMessage, keyPath]
  )
  return { value, pending, set: setValue }
}
