import { app, dialog } from 'electron'
import { promises as fs } from 'fs'
import * as path from 'path'
import { findMissingRequiredConfigFields } from '../shared/channelModuleConfig'
import type { ModuleConfigStatusMap } from '../shared/channelModules'
import { parseJsonObjectConfig } from '../shared/jsonConfig'
import { resolveRemoteWebSocketConfig } from '../shared/remoteConnection'
import { readModuleConfigStatusMap } from './channelModuleConfigStatus'
import { ChannelModuleManager, type ModuleStatusMap } from './channelModuleManager'
import { type WindowIpcContext } from './ipcBridgeContracts'
import { groupModulesByChannel, scanModules, type DiscoveredModule } from './moduleScanner'
import type { AppSettings } from './settings'

interface ModulesRescanSummaryPayload {
  addedModuleIds: string[]
  removedModuleIds: string[]
  changedModuleIds: string[]
  changedRunningModuleIds: string[]
}
function isSafeConfigFileName(configFileName: string): boolean {
  if (configFileName.trim() === '') return false
  if (configFileName.includes('..')) return false
  return !configFileName.includes('/') && !configFileName.includes('\\')
}

function ensureObjectConfig(config: unknown): Record<string, unknown> {
  if (config == null || typeof config !== 'object' || Array.isArray(config)) {
    throw new Error('Config payload must be a JSON object')
  }
  return config as Record<string, unknown>
}

function resolveConnectionMode(settings: AppSettings): 'stdio' | 'websocket' | 'stdioAndWebSocket' | 'remote' {
  const mode = settings.connectionMode
  return mode === 'remote' ? 'remote' : 'stdioAndWebSocket'
}

function resolveModuleWsConfig(
  settings: AppSettings,
  runtime?: { wsUrl: string; token?: string } | null
): { wsUrl: string; token?: string } {
  const mode = resolveConnectionMode(settings)
  if (mode === 'remote') {
    if (settings.activeRemoteStack || settings.activeRemoteProject) {
      if (!runtime?.wsUrl?.trim()) {
        throw new Error('Remote AppServer SSH forward is not connected.')
      }
      return runtime.token?.trim()
        ? { wsUrl: runtime.wsUrl.trim(), token: runtime.token.trim() }
        : { wsUrl: runtime.wsUrl.trim() }
    }

    const resolved = resolveRemoteWebSocketConfig(settings.remote)
    if (!resolved.ok) {
      throw new Error(resolved.message)
    }
    return resolved.token ? { wsUrl: resolved.wsUrl, token: resolved.token } : { wsUrl: resolved.wsUrl }
  }

  if (runtime?.wsUrl?.trim()) {
    return runtime.token?.trim()
      ? { wsUrl: runtime.wsUrl.trim(), token: runtime.token.trim() }
      : { wsUrl: runtime.wsUrl.trim() }
  }

  const host = settings.webSocket?.host?.trim() || '127.0.0.1'
  const candidatePort = settings.webSocket?.port
  const port =
    typeof candidatePort === 'number' && Number.isInteger(candidatePort) && candidatePort > 0 && candidatePort <= 65535
      ? candidatePort
      : 9100
  return { wsUrl: `ws://${host}:${port}/ws` }
}

function injectModuleDotcraftConfig(
  config: Record<string, unknown>,
  wsConfig: { wsUrl: string; token?: string }
): Record<string, unknown> {
  const next: Record<string, unknown> = { ...config }
  const dotcraftRaw = next.dotcraft
  const dotcraft =
    dotcraftRaw != null && typeof dotcraftRaw === 'object' && !Array.isArray(dotcraftRaw)
      ? { ...(dotcraftRaw as Record<string, unknown>) }
      : {}
  dotcraft.wsUrl = wsConfig.wsUrl
  if (wsConfig.token !== undefined) {
    dotcraft.token = wsConfig.token
  }
  next.dotcraft = dotcraft
  return next
}
function normalizeChannelName(channelName: string): string {
  return channelName.trim().toLowerCase()
}

function isRunningProcessState(state: ModuleStatusMap[string]['processState'] | undefined): boolean {
  return state === 'starting' || state === 'running'
}

function areModulesEquivalent(previous: DiscoveredModule, next: DiscoveredModule): boolean {
  return JSON.stringify(previous) === JSON.stringify(next)
}
export interface WindowChannelModules {
  manager: ChannelModuleManager
  restore(enabledChannelNames: string[]): Promise<void>
  dispose(): void
}

export function registerChannelModuleHandlers(context: WindowIpcContext): WindowChannelModules {
  const { handleSafe, workspacePath, getWireClient, callbacks, owner } = context
  let cachedModules: DiscoveredModule[] | null = null
  const configWriteQueues = new Map<string, Promise<void>>()
  const scanAndCacheModules = async (options?: { emitSummary?: boolean }): Promise<DiscoveredModule[]> => {
    const previousModules = cachedModules ?? []
    const nextModules = await scanModules(callbacks.getSettings(), !app.isPackaged)
    const previousById = new Map(previousModules.map((module) => [module.moduleId, module] as const))
    const nextById = new Map(nextModules.map((module) => [module.moduleId, module] as const))

    const addedModuleIds: string[] = []
    const removedModuleIds: string[] = []
    const changedModuleIds: string[] = []
    for (const module of nextModules) {
      const previous = previousById.get(module.moduleId)
      if (!previous) {
        addedModuleIds.push(module.moduleId)
        continue
      }
      if (!areModulesEquivalent(previous, module)) {
        changedModuleIds.push(module.moduleId)
      }
    }
    for (const previous of previousModules) {
      if (!nextById.has(previous.moduleId)) {
        removedModuleIds.push(previous.moduleId)
      }
    }

    for (const moduleId of removedModuleIds) {
      await channelModuleManager.stop(moduleId)
    }

    const statusMap = channelModuleManager.getStatusMap()
    const changedRunningModuleIds = changedModuleIds.filter((moduleId) =>
      isRunningProcessState(statusMap[moduleId]?.processState)
    )

    cachedModules = nextModules

    if (options?.emitSummary === true) {
      const summary: ModulesRescanSummaryPayload = {
        addedModuleIds,
        removedModuleIds,
        changedModuleIds,
        changedRunningModuleIds
      }
      if (!owner.isDestroyed()) owner.webContents.send('modules:rescan-summary', summary)
    }

    return nextModules
  }
  const channelModuleManager = new ChannelModuleManager({
    workspacePath,
    getWireClient,
    getCachedModules: () => cachedModules,
    onStatusChanged: (statusMap) => {
      if (!owner.isDestroyed()) owner.webContents.send('modules:status-changed', statusMap)
    },
    onQrUpdate: (payload) => {
      if (!owner.isDestroyed()) owner.webContents.send('modules:qr-update', payload)
    }
  })
  handleSafe('modules:list', async () => {
    if (cachedModules !== null) {
      return cachedModules
    }
    return scanAndCacheModules()
  })

  handleSafe('modules:pick-directory', async (): Promise<string | null> => {
    const result = await dialog.showOpenDialog(owner, {
      title: 'Select Module Directory',
      properties: ['openDirectory', 'createDirectory']
    })
    if (result.canceled || result.filePaths.length === 0) {
      return null
    }
    return result.filePaths[0]
  })

  handleSafe('modules:rescan', async () => scanAndCacheModules({ emitSummary: true }))

  handleSafe('modules:config-status', async (): Promise<ModuleConfigStatusMap> => {
    const modules = cachedModules ?? (await scanAndCacheModules())
    return readModuleConfigStatusMap(workspacePath, modules)
  })

  handleSafe(
    'modules:set-active-variant',
    async (_event, params: { channelName: string; moduleId: string }): Promise<{ ok: boolean; error?: string }> => {
      if (cachedModules === null) {
        await scanAndCacheModules()
      }
      const channelName = params?.channelName
      const moduleId = params?.moduleId
      if (typeof channelName !== 'string' || typeof moduleId !== 'string') {
        return { ok: false, error: 'Invalid payload' }
      }
      const normalizedChannelName = normalizeChannelName(channelName)
      if (!normalizedChannelName || !moduleId.trim()) {
        return { ok: false, error: 'Invalid payload' }
      }
      const module = cachedModules?.find((item) => item.moduleId === moduleId)
      if (!module) {
        return { ok: false, error: `Module '${moduleId}' not found` }
      }
      if (normalizeChannelName(module.channelName) !== normalizedChannelName) {
        return { ok: false, error: `Module '${moduleId}' does not belong to channel '${channelName}'` }
      }

      const groups = groupModulesByChannel(cachedModules ?? [], callbacks.getSettings().activeModuleVariants)
      const currentGroup = groups.find((group) => normalizeChannelName(group.channelName) === normalizedChannelName)
      if (currentGroup && currentGroup.activeModuleId !== moduleId) {
        await channelModuleManager.stop(currentGroup.activeModuleId)
      }

      const currentSettings = callbacks.getSettings()
      await callbacks.updateSettings({
        activeModuleVariants: {
          ...(currentSettings.activeModuleVariants ?? {}),
          [normalizedChannelName]: moduleId
        }
      })
      return { ok: true }
    }
  )

  handleSafe(
    'modules:read-config',
    async (
      _event,
      params: { configFileName: string }
    ): Promise<{ exists: boolean; config: Record<string, unknown> | null }> => {
      if (!isSafeConfigFileName(params.configFileName)) {
        throw new Error('Invalid config file name')
      }
      const configPath = path.join(workspacePath, '.craft', params.configFileName)
      try {
        const stat = await fs.stat(configPath)
        if (stat.size > 1_000_000) {
          throw new Error(`Config file is too large to load: ${params.configFileName}`)
        }
        const raw = await fs.readFile(configPath, 'utf-8')
        return { exists: true, config: parseJsonObjectConfig(raw) }
      } catch (error) {
        const code = (error as NodeJS.ErrnoException | null)?.code
        if (code === 'ENOENT') {
          return { exists: false, config: null }
        }
        throw error
      }
    }
  )

  handleSafe(
    'modules:write-config',
    async (_event, params: { configFileName: string; config: Record<string, unknown> }): Promise<{ ok: true }> => {
      if (!isSafeConfigFileName(params.configFileName)) {
        throw new Error('Invalid config file name')
      }
      const configPath = path.join(workspacePath, '.craft', params.configFileName)
      const settings = callbacks.getSettings()
      const wsConfig = resolveModuleWsConfig(settings, callbacks.getAppServerWsConfig?.())
      const mergedConfig = injectModuleDotcraftConfig(ensureObjectConfig(params.config), wsConfig)
      const previous = configWriteQueues.get(configPath) ?? Promise.resolve()
      const writeTask = previous
        .catch(() => {})
        .then(async () => {
          await fs.mkdir(path.dirname(configPath), { recursive: true })
          await fs.writeFile(configPath, `${JSON.stringify(mergedConfig, null, 2)}\n`, 'utf-8')
        })
      configWriteQueues.set(configPath, writeTask)
      await writeTask
      if (configWriteQueues.get(configPath) === writeTask) {
        configWriteQueues.delete(configPath)
      }
      return { ok: true }
    }
  )

  handleSafe(
    'modules:start',
    async (
      _event,
      params: { moduleId: string }
    ): Promise<{ ok: boolean; error?: string; missingFields?: string[] }> => {
      if (cachedModules === null) {
        await scanAndCacheModules()
      }
      if (!params?.moduleId || typeof params.moduleId !== 'string') {
        return { ok: false, error: 'Invalid module id' }
      }
      const module = cachedModules?.find((item) => item.moduleId === params.moduleId)
      if (!module) {
        return { ok: false, error: `Module '${params.moduleId}' not found` }
      }
      const configPath = path.join(workspacePath, '.craft', module.configFileName)
      let stored: Record<string, unknown> | null = null
      try {
        stored = parseJsonObjectConfig(await fs.readFile(configPath, 'utf-8'))
      } catch (error) {
        const code = (error as NodeJS.ErrnoException | null)?.code
        if (code !== 'ENOENT') {
          return { ok: false, error: error instanceof Error ? error.message : String(error) }
        }
      }
      const settings = callbacks.getSettings()
      const wsConfig = resolveModuleWsConfig(settings, callbacks.getAppServerWsConfig?.())
      const merged = injectModuleDotcraftConfig(stored ?? {}, wsConfig)
      const missingFields = findMissingRequiredConfigFields(merged, module.configDescriptors)
      if (missingFields.length > 0) {
        return {
          ok: false,
          error: `Required fields missing: ${missingFields.join(', ')}`,
          missingFields
        }
      }
      if (stored === null || JSON.stringify(merged) !== JSON.stringify(stored)) {
        try {
          await fs.mkdir(path.dirname(configPath), { recursive: true })
          await fs.writeFile(configPath, `${JSON.stringify(merged, null, 2)}\n`, 'utf-8')
        } catch (error) {
          return { ok: false, error: error instanceof Error ? error.message : String(error) }
        }
      }
      return channelModuleManager.start(params.moduleId)
    }
  )

  handleSafe('modules:stop', async (_event, params: { moduleId: string }): Promise<{ ok: boolean; error?: string }> => {
    if (!params?.moduleId || typeof params.moduleId !== 'string') {
      return { ok: false, error: 'Invalid module id' }
    }
    return channelModuleManager.stop(params.moduleId)
  })

  handleSafe('modules:running', async (): Promise<ModuleStatusMap> => {
    return channelModuleManager.getStatusMap()
  })

  handleSafe('modules:get-logs', async (_event, params: { moduleId: string }): Promise<{ lines: string[] }> => {
    if (!params?.moduleId || typeof params.moduleId !== 'string') {
      return { lines: [] }
    }
    return { lines: await channelModuleManager.getRecentLogs(params.moduleId) }
  })

  handleSafe(
    'modules:qr-status',
    async (_event, params: { moduleId: string }): Promise<{ active: boolean; qrDataUrl: string | null }> => {
      if (!params?.moduleId || typeof params.moduleId !== 'string') {
        return { active: false, qrDataUrl: null }
      }
      return channelModuleManager.getQrStatus(params.moduleId)
    }
  )
  async function restore(enabledChannelNames: string[]): Promise<void> {
    const discoveredModules = await scanAndCacheModules()
    const grouped = groupModulesByChannel(discoveredModules, callbacks.getSettings().activeModuleVariants)

    const enabledNames = new Set(enabledChannelNames.map((name) => normalizeChannelName(name)).filter(Boolean))
    const moduleIdsToStart = grouped
      .filter((group) => enabledNames.has(normalizeChannelName(group.channelName)))
      .map((group) => group.activeModuleId)
      .filter(Boolean)
    await channelModuleManager.restoreModules(moduleIdsToStart)
  }
  return {
    manager: channelModuleManager,
    restore,
    dispose: () => channelModuleManager.dispose()
  }
}
