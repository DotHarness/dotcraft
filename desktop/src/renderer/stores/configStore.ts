import { useCallback, useEffect } from 'react'
import { create } from 'zustand'
import type { JsonValue } from '@dotcraft/sdk/contracts'
import { useConnectionStore } from './connectionStore'
import { addToast } from './toastStore'
import { requestWorkspaceResource, workspaceResourceKey, type WorkspaceResourceScope } from './workspaceResourceScope'

export type ConfigObject = Record<string, unknown>

export interface ConfigEdit {
  keyPath: string
  value: unknown
}

interface ConfigSnapshot {
  config: ConfigObject | null
  pendingKeyPaths: string[]
  status: 'idle' | 'loading' | 'ready' | 'error'
  error: string | null
  confirmed: boolean
}

interface ConfigStore extends ConfigSnapshot {
  scopeKey: string | null
  revision: number
  activate(scope: WorkspaceResourceScope | null): void
  prune(liveKeys: ReadonlySet<string>): void
  ensureLoaded(): Promise<void>
  refresh(): Promise<void>
  write(edits: ConfigEdit[]): Promise<void>
  handleConfigChanged(regions: string[]): void
  reset(): void
}

interface ConfigEntry extends ConfigSnapshot {
  scope: WorkspaceResourceScope
  baseConfig: ConfigObject | null
  writes: Map<number, ConfigEdit[]>
  committedPaths: Map<string, number>
  writeSeq: number
  readSeq: number
  activation: number
  inFlight: Promise<void> | null
  inFlightActivation: number
  refreshAgain: boolean
}

const emptySnapshot: ConfigSnapshot = {
  config: null, pendingKeyPaths: [], status: 'idle', error: null, confirmed: false
}
const entries = new Map<string, ConfigEntry>()

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

function currentEntry(): ConfigEntry | undefined {
  const key = useConfigStore.getState().scopeKey
  return key == null ? undefined : entries.get(key)
}

function live(entry: ConfigEntry): boolean {
  return entries.get(workspaceResourceKey(entry.scope)) === entry
}

function pathKey(path: string): string {
  return JSON.stringify(splitConfigKeyPath(path).map((segment) => segment.toLowerCase()))
}

function hasNewerCommit(entry: ConfigEntry, edit: ConfigEdit, sequence: number): boolean {
  const segments = splitConfigKeyPath(edit.keyPath).map((segment) => segment.toLowerCase())
  return [...entry.committedPaths].some(([key, committed]) => {
    if (committed <= sequence) return false
    const other = JSON.parse(key) as string[]
    return segments.slice(0, Math.min(segments.length, other.length)).every((segment, index) => segment === other[index])
  })
}

function publish(entry: ConfigEntry): void {
  if (!live(entry)) return
  let config = entry.baseConfig
  if (config != null) {
    for (const [sequence, edits] of entry.writes) {
      config = applyEdits(config, edits.filter((edit) => !hasNewerCommit(entry, edit, sequence)))
    }
  }
  entry.config = config
  entry.pendingKeyPaths = [...entry.writes.values()].flatMap((edits) => edits.map((edit) => edit.keyPath))
  if (useConfigStore.getState().scopeKey !== workspaceResourceKey(entry.scope)) return
  const { status, error, confirmed, pendingKeyPaths } = entry
  useConfigStore.setState({ config, status, error, confirmed, pendingKeyPaths })
}

function refreshEntry(entry: ConfigEntry): Promise<void> {
  if (!live(entry)) return Promise.resolve()
  if (entry.inFlight) {
    if (entry.inFlightActivation !== entry.activation) entry.refreshAgain = true
    return entry.inFlight
  }
  if (entry.writes.size > 0) {
    entry.refreshAgain = true
    return Promise.resolve()
  }
  const requestReadSeq = ++entry.readSeq
  const requestWriteSeq = entry.writeSeq
  const requestActivation = entry.activation
  entry.inFlightActivation = requestActivation
  entry.refreshAgain = false
  entry.status = entry.config == null ? 'loading' : 'ready'
  entry.error = null
  publish(entry)
  const request = requestWorkspaceResource(entry.scope, 'config/read', {})
    .then((result) => {
      if (!live(entry) || requestReadSeq !== entry.readSeq || requestWriteSeq !== entry.writeSeq) return
      const config = (result as { config?: unknown } | null)?.config
      entry.baseConfig = isRecord(config) ? config : {}
      entry.committedPaths.clear()
      entry.status = 'ready'
      entry.confirmed = requestActivation === entry.activation
    }, (error: unknown) => {
      if (!live(entry) || requestReadSeq !== entry.readSeq || requestWriteSeq !== entry.writeSeq) return
      entry.status = entry.config == null ? 'error' : 'ready'
      entry.error = error instanceof Error ? error.message : String(error)
    })
    .finally(async () => {
      if (!live(entry) || entry.inFlight !== request) return
      entry.inFlight = null
      publish(entry)
      if (entry.refreshAgain && entry.writes.size === 0) await refreshEntry(entry)
    })
  entry.inFlight = request
  return request
}

export const useConfigStore = create<ConfigStore>((set, get) => ({
  ...emptySnapshot,
  scopeKey: null,
  revision: 0,

  activate(scope) {
    const key = scope == null ? null : workspaceResourceKey(scope)
    if (key === get().scopeKey) return
    if (scope != null && key != null) {
      for (const [oldKey, old] of entries) {
        if (old.scope.connectionKey === scope.connectionKey && old.scope.connectionId !== scope.connectionId) entries.delete(oldKey)
      }
      let entry = entries.get(key)
      if (!entry) {
        entry = {
          ...emptySnapshot, scope, baseConfig: null, writes: new Map(), committedPaths: new Map(),
          writeSeq: 0, readSeq: 0, activation: 0, inFlight: null, inFlightActivation: 0, refreshAgain: false
        }
        entries.set(key, entry)
      }
      entry.activation += 1
      entry.confirmed = false
      const { config, pendingKeyPaths, status, error, confirmed } = entry
      set({ scopeKey: key, revision: get().revision + 1, config, pendingKeyPaths, status, error, confirmed })
    } else {
      set({ ...emptySnapshot, scopeKey: null, revision: get().revision + 1 })
    }
  },

  prune(liveKeys) {
    for (const key of entries.keys()) {
      if (key !== get().scopeKey && !liveKeys.has(key)) entries.delete(key)
    }
  },

  ensureLoaded() {
    const entry = currentEntry()
    if (!entry || entry.config != null) return Promise.resolve()
    return refreshEntry(entry)
  },

  refresh() {
    const entry = currentEntry()
    return entry ? refreshEntry(entry) : Promise.resolve()
  },

  async write(edits) {
    const entry = currentEntry()
    if (!entry) throw new Error('Workspace configuration is not available.')
    if (edits.length === 0) return
    const sequence = ++entry.writeSeq
    entry.confirmed = false
    entry.writes.set(sequence, edits)
    entry.inFlight = null
    publish(entry)
    const wireEdits = edits.map((edit) => ({
      keyPath: edit.keyPath, value: edit.value as JsonValue, mergeStrategy: 'replace' as const
    }))
    try {
      if (wireEdits.length === 1) {
        await requestWorkspaceResource(entry.scope, 'config/value/write', wireEdits[0], 20_000)
      } else {
        await requestWorkspaceResource(entry.scope, 'config/batchWrite', { edits: wireEdits }, 20_000)
      }
      if (live(entry)) {
        const applicable = edits.filter((edit) => !hasNewerCommit(entry, edit, sequence))
        if (entry.baseConfig != null) entry.baseConfig = applyEdits(entry.baseConfig, applicable)
        for (const edit of applicable) entry.committedPaths.set(pathKey(edit.keyPath), sequence)
      }
    } finally {
      if (live(entry)) {
        entry.writes.delete(sequence)
        publish(entry)
        if (entry.writes.size === 0) await refreshEntry(entry)
      }
    }
  },

  handleConfigChanged(regions) {
    const entry = currentEntry()
    const nonConfigRegions = new Set(['providers', 'skills', 'plugins', 'hooks', 'memory'])
    if (!entry || !regions.some((region) => {
      const root = splitConfigKeyPath(region)[0]
      return (entry.config != null && findKey(entry.config, root) !== undefined) || !nonConfigRegions.has(root.toLowerCase())
    })) return
    entry.confirmed = false
    entry.readSeq += 1
    entry.refreshAgain = true
    publish(entry)
    void refreshEntry(entry)
  },

  reset() {
    entries.clear()
    set({ ...emptySnapshot, scopeKey: null, revision: get().revision + 1 })
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
  const scopeKey = useConfigStore((state) => state.scopeKey)
  const scope = scopeKey == null ? undefined : entries.get(scopeKey)?.scope
  const available = useConnectionStore((state) =>
    state.status === 'connected' && state.capabilities?.workspaceConfigManagement === true &&
    scope != null && state.connectionKey === scope.connectionKey && state.connectionId === scope.connectionId)
  const config = useConfigStore((state) => state.config)
  const status = useConfigStore((state) => state.status)
  const revision = useConfigStore((state) => state.revision)
  useEffect(() => {
    if (available && status === 'idle') void useConfigStore.getState().ensureLoaded()
  }, [available, revision, status])
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
