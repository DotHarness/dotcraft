import { useEffect, useMemo, useRef, useState, type CSSProperties, type ReactNode } from 'react'
import { Ellipsis, Plus } from 'lucide-react'
import { addToast } from '../../stores/toastStore'
import { useLocale, useT } from '../../contexts/LocaleContext'
import type { AppLocale, LocalizedTextMap } from '../../../shared/locales'
import { useConnectionStore } from '../../stores/connectionStore'
import { useUIStore } from '../../stores/uiStore'
import {
  replaceCurrentAppNavigationLocation,
  runWithoutAppNavigationRecording
} from '../../stores/appNavigationStore'
import { FolderIcon, RefreshIcon } from '../ui/AppIcons'
import type { ChannelConnectionState } from './ChannelCard'
import { ModuleConfigForm } from './ModuleConfigForm'
import {
  ExternalChannelConfigForm,
  type ExternalChannelConfigWire
} from './ExternalChannelConfigForm'
import {
  CatalogCompactGrid,
  CatalogBreadcrumb,
  CatalogSearchBox,
  CatalogSection,
  CatalogTopBar,
  CATALOG_TOOLBAR_CONTROL_RADIUS,
  CATALOG_TOOLBAR_CONTROL_SIZE,
  styles as catalogStyles
} from '../catalog/CatalogSurface'
import { ContextMenu, type ContextMenuPosition } from '../ui/ContextMenu'
import { SkeletonCatalogGrid } from '../ui/Skeleton'
import { Button } from '../ui/Button'
import { IconButton } from '../ui/IconButton'
import { StatusPill } from './FormShared'
import { isPersistedEmbeddedModuleChannelEnabled } from '../../../shared/channelModulePersistence'
import type {
  ConnectionMode,
  DiscoveredModule,
  ModulesRescanSummaryPayload,
  ModuleStatusEntry,
  ModuleStatusMap,
  QrUpdatePayload
} from '../../../preload/api.d'

interface ChannelStatusWire {
  name: string
  category: string
  enabled: boolean
  running: boolean
}

interface ChannelInfoWire {
  name: string
}

interface ExternalChannelViewModel {
  name: string
  draft: ExternalChannelConfigWire
  configured: boolean
}

interface ModuleQrState {
  active: boolean
  qrDataUrl: string | null
  timestamp: number
  successUntil?: number
}

type ModuleQrPhase = 'idle' | 'waitingForQr' | 'qrAvailable' | 'loginSuccess' | 'error'

interface ChannelModuleGroup {
  channelName: string
  activeModuleId: string
  modules: DiscoveredModule[]
}

function normalizeConnectionMode(mode: unknown): ConnectionMode {
  return mode === 'remote' ? 'remote' : 'local'
}

function isModuleWsAvailable(mode: ConnectionMode): boolean {
  return mode === 'local' || mode === 'remote'
}

function normalizeChannelName(value: string): string {
  return value.trim().toLowerCase()
}

function groupModulesByChannel(
  modules: DiscoveredModule[],
  activeModuleVariants: Record<string, string>
): ChannelModuleGroup[] {
  const byChannel = new Map<string, DiscoveredModule[]>()
  for (const module of modules) {
    const key = normalizeChannelName(module.channelName)
    const list = byChannel.get(key)
    if (list) list.push(module)
    else byChannel.set(key, [module])
  }

  const groups: ChannelModuleGroup[] = []
  for (const [channelKey, channelModules] of byChannel.entries()) {
    const persistedActiveModuleId = activeModuleVariants[channelKey]
    const persistedMatch =
      persistedActiveModuleId == null
        ? undefined
        : channelModules.find((module) => module.moduleId === persistedActiveModuleId)
    const userPreferred = channelModules.find((module) => module.source === 'user')
    const active = persistedMatch ?? userPreferred ?? channelModules[0]
    if (!active) continue
    groups.push({
      channelName: active.channelName,
      activeModuleId: active.moduleId,
      modules: channelModules
    })
  }
  return groups
}

function moduleLogoPath(channelName: string): string {
  return new URL(`../../assets/channels/${channelName}.svg`, import.meta.url).toString()
}

function createEmptyExternalChannel(): ExternalChannelConfigWire {
  return {
    name: '',
    enabled: false,
    transport: 'subprocess',
    command: '',
    args: [],
    workingDirectory: '',
    env: {}
  }
}

function externalTransportLabel(transport: ExternalChannelConfigWire['transport']): string {
  if (transport === 'websocket') return 'WebSocket'
  if (transport === 'managedWebsocket') return 'Managed WebSocket'
  return 'Subprocess'
}

function externalTransportHasProcessLauncher(
  transport: ExternalChannelConfigWire['transport']
): boolean {
  return transport === 'subprocess' || transport === 'managedWebsocket'
}

function cloneExternalChannel(channel: ExternalChannelConfigWire): ExternalChannelConfigWire {
  return {
    ...channel,
    args: [...(channel.args ?? [])],
    env: { ...(channel.env ?? {}) }
  }
}

function statusLabelKey(status: ChannelConnectionState): string {
  return status === 'connected'
    ? 'channels.status.connected'
    : status === 'enabledNotConnected'
      ? 'channels.status.enabledNotConnected'
      : 'channels.status.notConfigured'
}

function moduleStatusLabelKey(status: ChannelConnectionState): string {
  if (status === 'connecting') return 'channels.modules.connecting'
  if (status === 'error') return 'channels.modules.error'
  if (status === 'stopped') return 'channels.modules.stopped'
  return statusLabelKey(status)
}

function stateColor(status: ChannelConnectionState): string {
  if (status === 'connected') return 'var(--success)'
  if (status === 'enabledNotConnected' || status === 'connecting') return 'var(--warning)'
  if (status === 'error') return 'var(--error, #ff453a)'
  return 'var(--text-dimmed)'
}

function deriveModuleStatus(
  moduleId: string,
  statusMap: ModuleStatusMap,
  persistedEnabled: boolean
): ChannelConnectionState {
  const entry = statusMap[moduleId]
  if (!entry) return persistedEnabled ? 'stopped' : 'notConfigured'
  if (entry.processState === 'crashed') return 'error'
  if (entry.connected) return 'connected'
  if (entry.processState === 'starting') return 'connecting'
  if (entry.processState === 'running') return 'enabledNotConnected'
  if (entry.processState === 'stopped') return persistedEnabled ? 'stopped' : 'notConfigured'
  return 'notConfigured'
}

function moduleStatusEntryFromChannelStatus(
  channelName: string,
  statusMap: Map<string, ChannelStatusWire> | null,
  fallbackConnected: Set<string> | null
): ModuleStatusEntry | undefined {
  const normalizedName = channelName.toLowerCase()
  const status = statusMap?.get(normalizedName)
  if (status) {
    return {
      processState: status.running || status.enabled ? 'running' : 'stopped',
      connected: status.running,
      restartCount: 0,
      lastExitCode: null
    }
  }

  if (fallbackConnected?.has(normalizedName)) {
    return {
      processState: 'running',
      connected: true,
      restartCount: 0,
      lastExitCode: null
    }
  }

  return undefined
}

function deriveExternalStatus(
  name: string,
  enabled: boolean,
  configured: boolean,
  statusMap: Map<string, ChannelStatusWire> | null,
  fallbackConnected: Set<string> | null
): ChannelConnectionState {
  if (statusMap !== null) {
    const s = statusMap.get(name.toLowerCase())
    if (!s) return configured && enabled ? 'enabledNotConnected' : 'notConfigured'
    if (s.running) return 'connected'
    if (s.enabled) return 'enabledNotConnected'
    return 'notConfigured'
  }

  const connected = fallbackConnected?.has(name.toLowerCase()) ?? false
  if (connected) return 'connected'
  return configured && enabled ? 'enabledNotConnected' : 'notConfigured'
}

function getNestedValue(config: Record<string, unknown>, dottedKey: string): unknown {
  const parts = dottedKey.split('.').filter(Boolean)
  if (parts.length === 0) return undefined
  let current: unknown = config
  for (const part of parts) {
    if (current == null || typeof current !== 'object' || Array.isArray(current)) {
      return undefined
    }
    current = (current as Record<string, unknown>)[part]
  }
  return current
}

function setNestedValue(
  config: Record<string, unknown>,
  dottedKey: string,
  value: unknown
): Record<string, unknown> {
  const parts = dottedKey.split('.').filter(Boolean)
  if (parts.length === 0) return config
  const next: Record<string, unknown> = { ...config }
  let current: Record<string, unknown> = next
  for (let index = 0; index < parts.length - 1; index += 1) {
    const key = parts[index]
    const existing = current[key]
    const child =
      existing != null && typeof existing === 'object' && !Array.isArray(existing)
        ? { ...(existing as Record<string, unknown>) }
        : {}
    current[key] = child
    current = child
  }
  current[parts[parts.length - 1]] = value
  return next
}

function cloneDescriptorDefaultValue(value: unknown): unknown {
  if (value == null || typeof value !== 'object') return value
  try {
    return structuredClone(value)
  } catch {
    try {
      return JSON.parse(JSON.stringify(value)) as unknown
    } catch {
      return value
    }
  }
}

function seedConfigWithDescriptorDefaults(
  config: Record<string, unknown>,
  descriptors: DiscoveredModule['configDescriptors']
): Record<string, unknown> {
  let nextConfig = { ...config }
  for (const descriptor of descriptors) {
    if (descriptor.required !== true) continue
    if (descriptor.defaultValue === undefined) continue
    if (getNestedValue(nextConfig, descriptor.key) !== undefined) continue
    nextConfig = setNestedValue(
      nextConfig,
      descriptor.key,
      cloneDescriptorDefaultValue(descriptor.defaultValue)
    )
  }
  return nextConfig
}

function resolveModuleDisplayName(
  module: Pick<DiscoveredModule, 'displayName' | 'localizedDisplayName'>,
  locale: AppLocale
): string {
  return module.localizedDisplayName?.[locale] ?? module.displayName
}

function resolveLocalizedText(
  localized: LocalizedTextMap | undefined,
  fallback: string | undefined,
  locale: AppLocale
): string | undefined {
  const localizedValue = localized?.[locale]?.trim()
  if (localizedValue) return localizedValue
  const fallbackValue = fallback?.trim()
  return fallbackValue || undefined
}

function moduleShortDescription(module: DiscoveredModule, locale: AppLocale): string {
  return (
    resolveLocalizedText(
      module.interface?.localizedShortDescription,
      module.interface?.shortDescription,
      locale
    ) ?? module.packageName
  )
}

function moduleLongDescription(module: DiscoveredModule, locale: AppLocale): string {
  return (
    resolveLocalizedText(
      module.interface?.localizedLongDescription,
      module.interface?.longDescription,
      locale
    ) ?? moduleShortDescription(module, locale)
  )
}

function modulePreviewPrompt(module: DiscoveredModule, locale: AppLocale): string {
  return (
    resolveLocalizedText(
      module.interface?.localizedPreviewPrompt,
      module.interface?.previewPrompt,
      locale
    ) ?? moduleShortDescription(module, locale)
  )
}

export function ChannelsView(): JSX.Element {
  const locale = useLocale()
  const t = useT()
  const capabilities = useConnectionStore((s) => s.capabilities)
  const selectedChannelKey = useUIStore((s) => s.selectedChannelKey)
  const setSelectedChannelKey = useUIStore((s) => s.setSelectedChannelKey)
  const [channelStatusMap, setChannelStatusMap] = useState<Map<string, ChannelStatusWire> | null>(null)
  const [fallbackConnected, setFallbackConnected] = useState<Set<string> | null>(null)
  const [statusError, setStatusError] = useState(false)
  const [externalChannels, setExternalChannels] = useState<ExternalChannelConfigWire[]>([])
  const [externalLoading, setExternalLoading] = useState(false)
  const [externalError, setExternalError] = useState<string | null>(null)
  const [externalDraft, setExternalDraft] = useState<ExternalChannelConfigWire>(createEmptyExternalChannel())
  const [savingExternal, setSavingExternal] = useState(false)
  const [deletingExternal, setDeletingExternal] = useState(false)
  const [modules, setModules] = useState<DiscoveredModule[]>([])
  const [modulesLoading, setModulesLoading] = useState(false)
  const [modulesError, setModulesError] = useState<string | null>(null)
  const [moduleConfig, setModuleConfig] = useState<Record<string, unknown>>({})
  const [savingModule, setSavingModule] = useState(false)
  const [moduleStatusMap, setModuleStatusMap] = useState<ModuleStatusMap>({})
  const [moduleQrState, setModuleQrState] = useState<Record<string, ModuleQrState>>({})
  const [togglingModuleId, setTogglingModuleId] = useState<string | null>(null)
  const [variantSwitchingChannel, setVariantSwitchingChannel] = useState<string | null>(null)
  const [activeModuleVariants, setActiveModuleVariants] = useState<Record<string, string>>({})
  const [connectionMode, setConnectionMode] = useState<ConnectionMode>('local')
  const [moduleLogsById, setModuleLogsById] = useState<Record<string, string[]>>({})
  const [loadingLogsModuleId, setLoadingLogsModuleId] = useState<string | null>(null)
  const [query, setQuery] = useState('')
  const [menuPosition, setMenuPosition] = useState<ContextMenuPosition | null>(null)
  const moduleConnectedSnapshotRef = useRef<Record<string, boolean>>({})
  const selectedModuleId = selectedChannelKey?.startsWith('module:')
    ? selectedChannelKey.slice('module:'.length)
    : null
  const selectedExternalName = selectedChannelKey?.startsWith('external:')
    ? selectedChannelKey.slice('external:'.length)
    : null

  const externalManagementEnabled = capabilities?.externalChannelManagement === true

  useEffect(() => {
    window.api.settings
      .get()
      .then((settings) => {
        setConnectionMode(normalizeConnectionMode(settings.connectionMode))
        const raw = settings.activeModuleVariants
        if (raw != null && typeof raw === 'object' && !Array.isArray(raw)) {
          const normalized: Record<string, string> = {}
          for (const [key, value] of Object.entries(raw)) {
            if (typeof value !== 'string') continue
            const channelName = normalizeChannelName(key)
            const moduleId = value.trim()
            if (!channelName || !moduleId) continue
            normalized[channelName] = moduleId
          }
          setActiveModuleVariants(normalized)
        }
      })
      .catch(() => {})

    const onFocus = () => {
      void window.api.settings
        .get()
        .then((settings) => setConnectionMode(normalizeConnectionMode(settings.connectionMode)))
        .catch(() => {})
    }
    window.addEventListener('focus', onFocus)
    return () => {
      window.removeEventListener('focus', onFocus)
    }
  }, [])

  useEffect(() => {
    let cancelled = false
    const hasChannelStatus = capabilities?.channelStatus === true

    if (hasChannelStatus) {
      window.api.appServer
        .sendRequest('channel/status', {})
        .then((res) => {
          if (cancelled) return
          const wire = res as { channels?: ChannelStatusWire[] }
          const map = new Map<string, ChannelStatusWire>()
          for (const ch of wire.channels ?? []) {
            map.set(ch.name.toLowerCase(), ch)
          }
          setChannelStatusMap(map)
          setFallbackConnected(null)
          setStatusError(false)
        })
        .catch(() => {
          if (cancelled) return
          setChannelStatusMap(null)
          setStatusError(true)
        })
    } else {
      window.api.appServer
        .sendRequest('channel/list', {})
        .then((res) => {
          if (cancelled) return
          const wire = res as { channels?: ChannelInfoWire[] }
          setFallbackConnected(new Set((wire.channels ?? []).map((c) => c.name.toLowerCase())))
          setChannelStatusMap(null)
          setStatusError(false)
        })
        .catch(() => {
          if (cancelled) return
          setFallbackConnected(null)
          setStatusError(true)
        })
    }

    return () => {
      cancelled = true
    }
  }, [capabilities])

  async function reloadModules(rescan = false): Promise<void> {
    setModulesLoading(true)
    setModulesError(null)
    try {
      const list = rescan ? await window.api.modules.rescan() : await window.api.modules.list()
      setModules(list)
      if (rescan && selectedModuleId) {
        const maybeSelected = list.find((module) => module.moduleId === selectedModuleId)
        if (maybeSelected) {
          await loadModuleConfig(maybeSelected)
        }
      }
    } catch (err) {
      setModules([])
      setModulesError(err instanceof Error ? err.message : String(err))
    } finally {
      setModulesLoading(false)
    }
  }

  async function loadModuleConfig(selectedModule: DiscoveredModule): Promise<void> {
    try {
      const result = await window.api.modules.readConfig({
        configFileName: selectedModule.configFileName
      })
      const baseConfig = result.config ?? {}
      setModuleConfig(seedConfigWithDescriptorDefaults(baseConfig, selectedModule.configDescriptors))
    } catch (err) {
      setModuleConfig({})
      addToast(
        t('channels.loadFailed', {
          error: err instanceof Error ? err.message : String(err)
        }),
        'error'
      )
    }
  }

  /**
   * When saving a new external channel, pass `selectedExternalNameOverride` so draft hydration
   * does not rely on stale `selectedChannelKey` (e.g. still `external:__new__` before React re-renders).
   */
  async function reloadExternalChannels(selectedExternalNameOverride?: string): Promise<void> {
    if (!externalManagementEnabled) {
      setExternalChannels([])
      return
    }

    setExternalLoading(true)
    setExternalError(null)
    try {
      const res = (await window.api.appServer.sendRequest('externalChannel/list', {})) as {
        channels?: ExternalChannelConfigWire[]
      }
      const list = (res.channels ?? []).map(cloneExternalChannel)
      setExternalChannels(list)

      const selectedName =
        selectedExternalNameOverride !== undefined
          ? selectedExternalNameOverride
          : selectedChannelKey?.startsWith('external:')
            ? selectedChannelKey.slice('external:'.length)
            : null
      if (selectedName && selectedName !== '__new__') {
        const selected = list.find((item) => item.name.toLowerCase() === selectedName.toLowerCase())
        if (selected) {
          setExternalDraft(cloneExternalChannel(selected))
        } else {
          runWithoutAppNavigationRecording(() => setSelectedChannelKey(null))
          replaceCurrentAppNavigationLocation()
          setExternalDraft(createEmptyExternalChannel())
        }
      }
    } catch (err) {
      setExternalChannels([])
      setExternalError(err instanceof Error ? err.message : String(err))
    } finally {
      setExternalLoading(false)
    }
  }

  const externalChannelCards = useMemo<ExternalChannelViewModel[]>(() => {
    if (!externalManagementEnabled) return []

    const moduleChannelNames = new Set(modules.map((module) => module.channelName.toLowerCase()))

    const merged: ExternalChannelViewModel[] = []
    for (const channel of externalChannels) {
      const normalizedName = channel.name.toLowerCase()
      if (moduleChannelNames.has(normalizedName)) continue
      merged.push({
        name: channel.name,
        draft: cloneExternalChannel(channel),
        configured: true
      })
    }

    return merged
  }, [externalChannels, externalManagementEnabled, modules])

  useEffect(() => {
    void reloadExternalChannels()
  }, [externalManagementEnabled])

  useEffect(() => {
    void reloadModules()
  }, [])

  const moduleGroups = useMemo(
    () => groupModulesByChannel(modules, activeModuleVariants),
    [modules, activeModuleVariants]
  )
  const remoteConnection = connectionMode === 'remote'
  const effectiveModuleStatusMap = useMemo<ModuleStatusMap>(() => {
    if (!remoteConnection) return moduleStatusMap
    const next: ModuleStatusMap = {}
    for (const module of modules) {
      const status = moduleStatusEntryFromChannelStatus(
        module.channelName,
        channelStatusMap,
        fallbackConnected
      )
      if (status) {
        next[module.moduleId] = status
      }
    }
    return next
  }, [channelStatusMap, fallbackConnected, moduleStatusMap, modules, remoteConnection])
  const moduleById = useMemo(() => {
    const map = new Map<string, DiscoveredModule>()
    for (const module of modules) {
      map.set(module.moduleId, module)
    }
    return map
  }, [modules])

  useEffect(() => {
    const unsubscribe = window.api.modules.onRescanSummary((payload: ModulesRescanSummaryPayload) => {
      if (payload.changedRunningModuleIds.length === 0) return
      const labels = payload.changedRunningModuleIds
        .map((moduleId) => {
          const module = moduleById.get(moduleId)
          return module ? resolveModuleDisplayName(module, locale) : moduleId
        })
        .slice(0, 3)
      const labelText = labels.join(', ')
      const hasMore = payload.changedRunningModuleIds.length > labels.length
      addToast(
        t('channels.modules.updatedRestart', {
          names: hasMore ? `${labelText}...` : labelText
        }),
        'success'
      )
    })
    return () => {
      unsubscribe()
    }
  }, [locale, moduleById, t])

  useEffect(() => {
    let disposed = false
    window.api.modules
      .running()
      .then((statusMap) => {
        if (!disposed) {
          const connectedSnapshot: Record<string, boolean> = {}
          for (const [moduleId, entry] of Object.entries(statusMap)) {
            connectedSnapshot[moduleId] = entry?.connected === true
          }
          moduleConnectedSnapshotRef.current = connectedSnapshot
          setModuleStatusMap(statusMap)
        }
      })
      .catch(() => {})

    const unsubscribe = window.api.modules.onStatusChanged((statusMap) => {
      if (disposed) return
      const previous = moduleConnectedSnapshotRef.current
      const nextSnapshot: Record<string, boolean> = {}
      const now = Date.now()
      setModuleQrState((prev) => {
        let changed = false
        const next = { ...prev }
        for (const [moduleId, entry] of Object.entries(statusMap)) {
          const isConnected = entry?.connected === true
          nextSnapshot[moduleId] = isConnected
          const wasConnected = previous[moduleId] === true
          if (!wasConnected && isConnected) {
            const current = next[moduleId]
            next[moduleId] = {
              active: current?.active ?? false,
              qrDataUrl: current?.qrDataUrl ?? null,
              timestamp: current?.timestamp ?? now,
              successUntil: now + 2_000
            }
            changed = true
          } else if (wasConnected && !isConnected && next[moduleId]?.successUntil !== undefined) {
            next[moduleId] = {
              ...next[moduleId],
              successUntil: undefined
            }
            changed = true
          }
        }
        return changed ? next : prev
      })
      moduleConnectedSnapshotRef.current = nextSnapshot
      setModuleStatusMap(statusMap)
    })
    const unsubscribeQr = window.api.modules.onQrUpdate((payload: QrUpdatePayload) => {
      if (disposed) return
      setModuleQrState((prev) => ({
        ...prev,
        [payload.moduleId]: {
          ...(prev[payload.moduleId] ?? {
            active: true,
            qrDataUrl: null,
            timestamp: payload.timestamp
          }),
          active: true,
          qrDataUrl: payload.qrDataUrl,
          timestamp: payload.timestamp
        }
      }))
    })
    return () => {
      disposed = true
      unsubscribe()
      unsubscribeQr()
    }
  }, [])

  useEffect(() => {
    if (!selectedModuleId) return
    let cancelled = false
    window.api.modules
      .qrStatus(selectedModuleId)
      .then((state) => {
        if (cancelled) return
        setModuleQrState((prev) => ({
          ...prev,
          [selectedModuleId]: {
            ...(prev[selectedModuleId] ?? {
              timestamp: Date.now(),
              successUntil: undefined
            }),
            active: state.active,
            qrDataUrl: state.qrDataUrl,
            timestamp: prev[selectedModuleId]?.timestamp ?? Date.now()
          }
        }))
      })
      .catch(() => {})
    return () => {
      cancelled = true
    }
  }, [selectedModuleId])

  useEffect(() => {
    const now = Date.now()
    const pending = Object.values(moduleQrState)
      .map((state) => state.successUntil ?? 0)
      .filter((value) => value > now)
    if (pending.length === 0) return
    const delay = Math.max(0, Math.min(...pending) - now + 30)
    const timer = setTimeout(() => {
      setModuleQrState((prev) => {
        const current = Date.now()
        let changed = false
        const next: Record<string, ModuleQrState> = {}
        for (const [moduleId, state] of Object.entries(prev)) {
          if (state.successUntil !== undefined && state.successUntil <= current) {
            next[moduleId] = { ...state, successUntil: undefined }
            changed = true
          } else {
            next[moduleId] = state
          }
        }
        return changed ? next : prev
      })
    }, delay)
    return () => clearTimeout(timer)
  }, [moduleQrState])

  async function handleSaveModule(selectedModule: DiscoveredModule): Promise<void> {
    setSavingModule(true)
    try {
      await window.api.modules.writeConfig({
        configFileName: selectedModule.configFileName,
        config: moduleConfig
      })
      const processState = moduleStatusMap[selectedModule.moduleId]?.processState
      const running = processState === 'starting' || processState === 'running'
      addToast(t(running ? 'channels.modules.configSavedRestart' : 'channels.savedRestart'), 'success')
    } catch (err) {
      addToast(
        t('channels.saveFailed', {
          error: err instanceof Error ? err.message : String(err)
        }),
        'error'
      )
    } finally {
      setSavingModule(false)
    }
  }

  async function handleStartModule(moduleId: string): Promise<void> {
    let currentConnectionMode = connectionMode
    try {
      const latestSettings = await window.api.settings.get()
      currentConnectionMode = normalizeConnectionMode(latestSettings.connectionMode)
      setConnectionMode(currentConnectionMode)
    } catch {
      // Ignore settings read failure and use the latest known mode.
    }
    if (!isModuleWsAvailable(currentConnectionMode)) {
      addToast(t('channels.modules.wsRequired'), 'error')
      return
    }
    setTogglingModuleId(moduleId)
    try {
      const result = await window.api.modules.start({ moduleId })
      if (!result.ok) {
        if (result.missingFields && result.missingFields.length > 0) {
          addToast(
            t('channels.modules.missingRequired', {
              fields: result.missingFields.join(', ')
            }),
            'error'
          )
          return
        }
        addToast(
          t('channels.saveFailed', { error: result.error ?? 'Failed to start module process' }),
          'error'
        )
      }
    } catch (err) {
      addToast(
        t('channels.saveFailed', { error: err instanceof Error ? err.message : String(err) }),
        'error'
      )
    } finally {
      setTogglingModuleId((prev) => (prev === moduleId ? null : prev))
    }
  }

  async function handleStopModule(moduleId: string): Promise<void> {
    setTogglingModuleId(moduleId)
    try {
      const result = await window.api.modules.stop({ moduleId })
      if (!result.ok) {
        addToast(t('channels.saveFailed', { error: result.error ?? 'Failed to stop module process' }), 'error')
        return
      }
      await reloadExternalChannels()
    } catch (err) {
      addToast(
        t('channels.saveFailed', { error: err instanceof Error ? err.message : String(err) }),
        'error'
      )
    } finally {
      setTogglingModuleId((prev) => (prev === moduleId ? null : prev))
    }
  }

  async function handleSaveExternal(): Promise<void> {
    setSavingExternal(true)
    try {
      const hasProcessLauncher = externalTransportHasProcessLauncher(externalDraft.transport)
      const payload: ExternalChannelConfigWire = {
        ...externalDraft,
        name: externalDraft.name.trim(),
        command: hasProcessLauncher ? externalDraft.command?.trim() ?? '' : null,
        args:
          hasProcessLauncher
            ? (externalDraft.args ?? []).map((arg) => arg.trim()).filter(Boolean)
            : null,
        workingDirectory:
          hasProcessLauncher ? (externalDraft.workingDirectory?.trim() || null) : null,
        env:
          hasProcessLauncher && externalDraft.env
            ? Object.fromEntries(
                Object.entries(externalDraft.env).filter(([key]) => key.trim() !== '')
              )
            : null
      }

      const upsertRes = (await window.api.appServer.sendRequest('externalChannel/upsert', {
        channel: payload
      })) as { channel?: ExternalChannelConfigWire }
      const savedChannel = upsertRes.channel
        ? cloneExternalChannel(upsertRes.channel)
        : cloneExternalChannel(payload)

      setSelectedChannelKey(`external:${savedChannel.name}`)
      setExternalDraft(savedChannel)
      await reloadExternalChannels(savedChannel.name)
      addToast(t('channels.savedRestart'), 'success')
    } catch (err) {
      addToast(
        t('channels.saveFailed', {
          error: err instanceof Error ? err.message : String(err)
        }),
        'error'
      )
    } finally {
      setSavingExternal(false)
    }
  }

  async function handleDeleteExternal(): Promise<void> {
    const name = externalDraft.name.trim()
    if (!name) return
    setDeletingExternal(true)
    try {
      await window.api.appServer.sendRequest('externalChannel/remove', { name })
      await reloadExternalChannels()
      runWithoutAppNavigationRecording(() => setSelectedChannelKey(null))
      replaceCurrentAppNavigationLocation()
      setExternalDraft(createEmptyExternalChannel())
      addToast(t('channels.external.removed'), 'success')
    } catch (err) {
      addToast(
        t('channels.saveFailed', {
          error: err instanceof Error ? err.message : String(err)
        }),
        'error'
      )
    } finally {
      setDeletingExternal(false)
    }
  }

  const externalStatusByName = useMemo(() => {
    const map = new Map<string, ChannelConnectionState>()
    for (const channel of externalChannelCards) {
      map.set(
        channel.name.toLowerCase(),
        deriveExternalStatus(
          channel.name,
          channel.draft.enabled,
          channel.configured,
          channelStatusMap,
          fallbackConnected
        )
      )
    }
    if (selectedChannelKey === 'external:__new__') {
      map.set(
        '__new__',
        deriveExternalStatus('__new__', externalDraft.enabled, false, channelStatusMap, fallbackConnected)
      )
    }
    return map
  }, [externalChannelCards, externalDraft.enabled, channelStatusMap, fallbackConnected, selectedChannelKey])

  const persistedModuleEnabledByChannelName = useMemo(() => {
    const enabledByChannel = new Map<string, boolean>()
    for (const channel of externalChannels) {
      enabledByChannel.set(
        channel.name.toLowerCase(),
        isPersistedEmbeddedModuleChannelEnabled(channel)
      )
    }
    return enabledByChannel
  }, [externalChannels])

  const selectedModule = selectedModuleId ? moduleById.get(selectedModuleId) ?? null : null
  const selectedModuleGroup = selectedModule
    ? moduleGroups.find(
        (group) => normalizeChannelName(group.channelName) === normalizeChannelName(selectedModule.channelName)
      ) ?? null
    : null
  const selectedModuleVariants = selectedModuleGroup?.modules ?? []
  const selectedModuleStatus = selectedModule ? effectiveModuleStatusMap[selectedModule.moduleId] : undefined
  const selectedModuleQrState = selectedModuleId ? moduleQrState[selectedModuleId] : undefined
  const selectedModuleLogoPath =
    selectedModule && selectedModule.channelName
      ? moduleLogoPath(selectedModule.channelName)
      : undefined
  useEffect(() => {
    if (selectedModuleId && !selectedModule) {
      runWithoutAppNavigationRecording(() => setSelectedChannelKey(null))
      replaceCurrentAppNavigationLocation()
    }
  }, [selectedModuleId, selectedModule])

  useEffect(() => {
    if (!selectedExternalName || selectedExternalName === '__new__') return
    const selected = externalChannelCards.find(
      (item) => item.name.toLowerCase() === selectedExternalName.toLowerCase()
    )
    if (!selected) {
      runWithoutAppNavigationRecording(() => setSelectedChannelKey(null))
      replaceCurrentAppNavigationLocation()
      setExternalDraft(createEmptyExternalChannel())
    }
  }, [selectedExternalName, externalChannelCards])

  const selectedModuleQrPhase: ModuleQrPhase = useMemo(() => {
    if (!selectedModule || !selectedModule.requiresInteractiveSetup) return 'idle'
    if (selectedModuleStatus?.processState === 'crashed') return 'error'
    if (
      selectedModuleQrState?.successUntil !== undefined &&
      selectedModuleQrState.successUntil > Date.now()
    ) {
      return 'loginSuccess'
    }
    if (selectedModuleStatus?.connected === true) return 'idle'
    const processRunning =
      selectedModuleStatus?.processState === 'starting' || selectedModuleStatus?.processState === 'running'
    if (!processRunning) return 'idle'
    if (selectedModuleQrState?.qrDataUrl) return 'qrAvailable'
    return 'waitingForQr'
  }, [selectedModule, selectedModuleStatus, selectedModuleQrState])

  async function handleSetActiveVariant(
    channelName: string,
    moduleId: string
  ): Promise<void> {
    const normalizedChannelName = normalizeChannelName(channelName)
    if (!normalizedChannelName || !moduleId) return
    setVariantSwitchingChannel(normalizedChannelName)
    try {
      const result = await window.api.modules.setActiveVariant({
        channelName,
        moduleId
      })
      if (!result.ok) {
        addToast(
          t('channels.saveFailed', {
            error: result.error ?? 'Failed to switch module variant'
          }),
          'error'
        )
        return
      }
      setActiveModuleVariants((prev) => ({
        ...prev,
        [normalizedChannelName]: moduleId
      }))
      setSelectedChannelKey(`module:${moduleId}`)
      const nextModule = moduleById.get(moduleId)
      if (nextModule) {
        await loadModuleConfig(nextModule)
      }
    } catch (err) {
      addToast(
        t('channels.saveFailed', { error: err instanceof Error ? err.message : String(err) }),
        'error'
      )
    } finally {
      setVariantSwitchingChannel((prev) =>
        prev === normalizedChannelName ? null : prev
      )
    }
  }

  async function handleLoadModuleLogs(moduleId: string): Promise<void> {
    setLoadingLogsModuleId(moduleId)
    try {
      const result = await window.api.modules.getLogs(moduleId)
      setModuleLogsById((prev) => ({ ...prev, [moduleId]: result.lines ?? [] }))
    } catch {
      // Keep silent; logs are diagnostics only.
    } finally {
      setLoadingLogsModuleId((prev) => (prev === moduleId ? null : prev))
    }
  }

  function handleOpenModulesFolder(): void {
    void window.api.modules.openFolder().then((result) => {
      if (!result.ok) {
        addToast(
          t('channels.saveFailed', {
            error: result.error ?? 'Failed to open modules folder'
          }),
          'error'
        )
      }
    })
  }

  function openNewExternalChannel(): void {
    setExternalDraft(createEmptyExternalChannel())
    setSelectedChannelKey('external:__new__')
  }

  function closeDetail(): void {
    setSelectedChannelKey(null)
  }

  const normalizedQuery = query.trim().toLowerCase()
  const moduleItems = moduleGroups
    .map((group) => {
      const module = moduleById.get(group.activeModuleId)
      if (!module) return null
      const persistedEnabled =
        persistedModuleEnabledByChannelName.get(module.channelName.toLowerCase()) === true ||
        (remoteConnection &&
          (channelStatusMap?.get(module.channelName.toLowerCase())?.enabled === true ||
            fallbackConnected?.has(module.channelName.toLowerCase()) === true))
      const status = deriveModuleStatus(module.moduleId, effectiveModuleStatusMap, persistedEnabled)
      const title = resolveModuleDisplayName(module, locale)
      const subtitle = moduleShortDescription(module, locale)
      const longDescription = moduleLongDescription(module, locale)
      const previewPrompt = modulePreviewPrompt(module, locale)
      const searchable =
        `${title} ${subtitle} ${longDescription} ${previewPrompt} ${module.channelName} ${module.packageName} ${module.variant}`.toLowerCase()
      if (normalizedQuery && !searchable.includes(normalizedQuery)) return null
      return {
        key: `module:${module.moduleId}`,
        logoPath: moduleLogoPath(module.channelName),
        title,
        subtitle,
        badgeText: group.modules.length > 1 ? `${group.modules.length}` : undefined,
        status,
        statusLabel: t(moduleStatusLabelKey(status)),
        active: selectedChannelKey === `module:${module.moduleId}`,
        onOpen: () => {
          setSelectedChannelKey(`module:${module.moduleId}`)
          void loadModuleConfig(module)
        }
      }
    })
    .filter((item): item is NonNullable<typeof item> => item !== null)

  const externalItems = externalChannelCards
    .map((channel) => {
      const status = externalStatusByName.get(channel.name.toLowerCase()) ?? 'notConfigured'
      const subtitle = `${t('channels.external.title')} · ${externalTransportLabel(channel.draft.transport)}`
      const searchable = `${channel.name} ${channel.draft.transport}`.toLowerCase()
      if (normalizedQuery && !searchable.includes(normalizedQuery)) return null
      return {
        key: `external:${channel.name}`,
        title: channel.name,
        subtitle,
        status,
        statusLabel: t(statusLabelKey(status)),
        active: selectedChannelKey === `external:${channel.name}`,
        onOpen: () => {
          setExternalDraft(cloneExternalChannel(channel.draft))
          setSelectedChannelKey(`external:${channel.name}`)
        }
      }
    })
    .filter((item): item is NonNullable<typeof item> => item !== null)

  const externalDetailStatus =
    selectedExternalName === '__new__'
      ? deriveExternalStatus('__new__', externalDraft.enabled, false, channelStatusMap, fallbackConnected)
      : selectedExternalName
        ? externalStatusByName.get(selectedExternalName.toLowerCase()) ?? 'notConfigured'
        : null

  const detailContent =
    selectedModuleId && selectedModule ? (
      <ModuleConfigForm
        module={selectedModule}
        variantModules={selectedModuleVariants}
        onVariantChange={(nextModuleId) => {
          if (!selectedModule) return
          void handleSetActiveVariant(selectedModule.channelName, nextModuleId)
        }}
        variantSwitching={
          selectedModule
            ? variantSwitchingChannel === normalizeChannelName(selectedModule.channelName)
            : false
        }
        config={moduleConfig}
        onChange={setModuleConfig}
        onSave={() => void handleSaveModule(selectedModule)}
        saving={savingModule}
        logoPath={selectedModuleLogoPath}
        moduleStatus={selectedModuleStatus as ModuleStatusEntry | undefined}
        persistedEnabled={
          persistedModuleEnabledByChannelName.get(selectedModule.channelName.toLowerCase()) === true ||
          (remoteConnection &&
            (channelStatusMap?.get(selectedModule.channelName.toLowerCase())?.enabled === true ||
              fallbackConnected?.has(selectedModule.channelName.toLowerCase()) === true))
        }
        wsAvailable={isModuleWsAvailable(connectionMode)}
        localControlsAvailable={!remoteConnection}
        onStart={() => {
          void handleStartModule(selectedModule.moduleId)
        }}
        onStop={() => {
          void handleStopModule(selectedModule.moduleId)
        }}
        starting={togglingModuleId === selectedModule.moduleId}
        qrDataUrl={selectedModuleQrState?.qrDataUrl ?? null}
        qrPhase={selectedModuleQrPhase}
        moduleLogLines={moduleLogsById[selectedModule.moduleId] ?? []}
        logsLoading={loadingLogsModuleId === selectedModule.moduleId}
        onLoadLogs={() => {
          void handleLoadModuleLogs(selectedModule.moduleId)
        }}
        hideHeader
      />
    ) : selectedExternalName ? (
      !externalManagementEnabled ? (
        <div style={emptyText}>{t('channels.external.unavailable')}</div>
      ) : externalDetailStatus ? (
        <ExternalChannelConfigForm
          value={externalDraft}
          saving={savingExternal}
          deleting={deletingExternal}
          isNew={selectedExternalName === '__new__'}
          status={externalDetailStatus}
          statusLabel={t(statusLabelKey(externalDetailStatus))}
          onChange={setExternalDraft}
          onSave={() => void handleSaveExternal()}
          onDelete={
            selectedExternalName === '__new__'
              ? undefined
              : () => {
                  void handleDeleteExternal()
                }
          }
          hideHeader
        />
      ) : null
    ) : null

  if (detailContent && selectedModule) {
    const title = resolveModuleDisplayName(selectedModule, locale)
    const persistedEnabled =
      persistedModuleEnabledByChannelName.get(selectedModule.channelName.toLowerCase()) === true ||
      (remoteConnection &&
        (channelStatusMap?.get(selectedModule.channelName.toLowerCase())?.enabled === true ||
          fallbackConnected?.has(selectedModule.channelName.toLowerCase()) === true))
    const status = deriveModuleStatus(selectedModule.moduleId, effectiveModuleStatusMap, persistedEnabled)
    const infoItems = [
      { label: t('channels.detail.package'), value: selectedModule.packageName },
      {
        label: t('channels.detail.source'),
        value:
          selectedModule.source === 'bundled'
            ? t('channels.modules.source.bundled')
            : t('channels.modules.source.user')
      },
      { label: t('channels.detail.variant'), value: selectedModule.variant },
      { label: t('channels.detail.transports'), value: selectedModule.supportedTransports.join(', ') },
      {
        label: t('channels.detail.capabilities'),
        value: formatCapabilitySummary(selectedModule.capabilitySummary)
      }
    ]

    return (
      <ChannelDetailPage
        title={title}
        subtitle={moduleShortDescription(selectedModule, locale)}
        logoPath={selectedModuleLogoPath}
        status={status}
        statusLabel={t(moduleStatusLabelKey(status))}
        previewPrompt={modulePreviewPrompt(selectedModule, locale)}
        description={moduleLongDescription(selectedModule, locale)}
        onBack={closeDetail}
      >
        <section style={detailSection}>
          <h2 style={detailSectionTitle}>{t('channels.detail.configuration')}</h2>
          {detailContent}
        </section>
        <ChannelInfoGrid items={infoItems} />
      </ChannelDetailPage>
    )
  }

  if (detailContent && selectedExternalName) {
    const title =
      selectedExternalName === '__new__'
        ? t('channels.external.new')
        : externalDraft.name || t('channels.external.title')
    const transportLabel = externalTransportLabel(externalDraft.transport)
    return (
      <ChannelDetailPage
        title={title}
        subtitle={t('channels.external.detailShort')}
        status={externalDetailStatus ?? 'notConfigured'}
        statusLabel={externalDetailStatus ? t(statusLabelKey(externalDetailStatus)) : t('channels.status.notConfigured')}
        previewPrompt={t('channels.external.previewPrompt')}
        description={t('channels.external.detailLong')}
        onBack={closeDetail}
      >
        <section style={detailSection}>
          <h2 style={detailSectionTitle}>{t('channels.detail.configuration')}</h2>
          {detailContent}
        </section>
        <ChannelInfoGrid
          items={[
            { label: t('channels.detail.source'), value: t('channels.external.title') },
            { label: t('channels.detail.transports'), value: transportLabel }
          ]}
        />
      </ChannelDetailPage>
    )
  }

  return (
    <div style={page}>
      <CatalogTopBar
        actions={(
          <>
            {externalManagementEnabled && (
              <Button
                variant="primary"
                size="toolbar"
                aria-label={t('channels.external.add')}
                onClick={openNewExternalChannel}
                iconLeft={<Plus size={14} aria-hidden />}
              >
                {t('channels.external.add')}
              </Button>
            )}
              <IconButton
                label={t('channels.moreActions')}
                tooltipLabel={t('channels.moreActions')}
                tooltipPlacement="bottom"
                size={CATALOG_TOOLBAR_CONTROL_SIZE}
                radius={CATALOG_TOOLBAR_CONTROL_RADIUS}
                aria-haspopup="menu"
                aria-expanded={menuPosition != null}
                onClick={(event) => setMenuPosition({ x: event.clientX, y: event.clientY })}
                icon={<Ellipsis size={15} aria-hidden />}
              />
          </>
        )}
      />
      <header style={browseHeader}>
        <h1 style={heroTitle}>{t('channels.heroTitle')}</h1>
        <div style={searchRow}>
          <CatalogSearchBox
            value={query}
            placeholder={t('channels.searchPlaceholder')}
            onChange={setQuery}
          />
        </div>
      </header>

      <div style={contentShell}>
        <div style={contentPane}>
          <main style={browseMain}>
            <CatalogSection title={t('channels.modules.group')}>
              {moduleItems.length > 0 ? (
                <CatalogCompactGrid>
                  {moduleItems.map(({ key, ...item }) => (
                    <ChannelCatalogItem key={key} {...item} />
                  ))}
                </CatalogCompactGrid>
              ) : modulesLoading ? (
                <SkeletonCatalogGrid count={4} ariaLabel={t('channels.loading')} />
              ) : (
                <p style={emptyText}>{t('channels.modules.empty')}</p>
              )}
            </CatalogSection>

            <CatalogSection title={t('channels.external.group')}>
              {externalItems.length > 0 ? (
                <CatalogCompactGrid>
                  {externalItems.map(({ key, ...item }) => (
                    <ChannelCatalogItem key={key} {...item} />
                  ))}
                </CatalogCompactGrid>
              ) : externalLoading ? (
                <SkeletonCatalogGrid count={4} ariaLabel={t('channels.loading')} />
              ) : (
                <p style={emptyText}>
                  {externalManagementEnabled
                    ? t('channels.external.empty')
                    : t('channels.external.unavailable')}
                </p>
              )}
            </CatalogSection>

            {(statusError || externalError || modulesError) && (
              <div style={diagnosticsPanel} role="status">
                {modulesError
                  ? t('channels.loadFailed', { error: modulesError })
                  : externalError
                    ? t('channels.loadFailed', { error: externalError })
                    : t('channels.statusUnavailable')}
              </div>
            )}
          </main>
        </div>
      </div>

      {menuPosition && (
        <ContextMenu
          position={menuPosition}
          onClose={() => setMenuPosition(null)}
          items={[
            {
              label: t('channels.modules.refresh'),
              icon: <RefreshIcon size={14} />,
              onClick: () => void reloadModules(true)
            },
            {
              label: t('channels.modules.openFolder'),
              icon: <FolderIcon size={14} />,
              onClick: handleOpenModulesFolder
            }
          ]}
        />
      )}
    </div>
  )
}

function ChannelCatalogItem({
  logoPath,
  title,
  subtitle,
  badgeText,
  status,
  statusLabel,
  active,
  onOpen
}: {
  logoPath?: string
  title: string
  subtitle: string
  badgeText?: string
  status: ChannelConnectionState
  statusLabel: string
  active: boolean
  onOpen: () => void
}): JSX.Element {
  const [hovered, setHovered] = useState(false)
  return (
    <div
      role="button"
      tabIndex={0}
      onClick={onOpen}
      onKeyDown={(event) => {
        if (event.key !== 'Enter' && event.key !== ' ') return
        event.preventDefault()
        onOpen()
      }}
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
      onFocus={() => setHovered(true)}
      onBlur={() => setHovered(false)}
      style={{
        ...compactItem,
        backgroundColor: active || hovered ? 'var(--bg-tertiary)' : 'transparent'
      }}
    >
      <ChannelIcon logoPath={logoPath} title={title} />
      <span style={channelText}>
        <span style={rowTitleLine}>
          <strong style={rowTitle}>{title}</strong>
          {badgeText && <span style={variantBadge}>{badgeText}</span>}
        </span>
        <span style={rowDesc}>{subtitle}</span>
      </span>
      <span style={channelStatus}>
        <span
          aria-hidden
          style={{
            ...statusDot,
            backgroundColor: stateColor(status)
          }}
        />
        <span style={statusLabelStyle}>{statusLabel}</span>
      </span>
    </div>
  )
}

function ChannelIcon({ logoPath, title }: { logoPath?: string; title: string }): JSX.Element {
  if (logoPath) {
    return <img src={logoPath} alt="" width={40} height={40} style={channelIcon} />
  }
  return (
    <span aria-hidden style={fallbackIcon}>
      {title.slice(0, 1).toUpperCase()}
    </span>
  )
}

function formatCapabilitySummary(summary: Record<string, unknown> | undefined): string {
  if (!summary || Object.keys(summary).length === 0) return '-'
  const enabledCapabilities = Object.entries(summary)
    .filter(([, value]) => value === true)
    .map(([key]) => key)
  return enabledCapabilities.length > 0 ? enabledCapabilities.join(', ') : '-'
}

function ChannelDetailPage({
  title,
  subtitle,
  logoPath,
  status,
  statusLabel,
  previewPrompt,
  description,
  onBack,
  children,
}: {
  title: string
  subtitle: string
  logoPath?: string
  status: ChannelConnectionState
  statusLabel: string
  previewPrompt: string
  description: string
  onBack: () => void
  children: ReactNode
}): JSX.Element {
  const t = useT()
  return (
    <div style={detailPage}>
      <CatalogTopBar
        navigation={(
          <CatalogBreadcrumb
            parentLabel={t('channels.title')}
            currentLabel={title}
            onBack={onBack}
          />
        )}
      />
      <main style={detailMain}>
        <div style={detailMainContent}>
          <header style={detailHeader}>
            <div style={detailIdentity}>
              <ChannelIcon logoPath={logoPath} title={title} />
              <div style={{ minWidth: 0 }}>
                <h1 style={detailTitle}>{title}</h1>
                <p style={detailSubtitle}>{subtitle}</p>
              </div>
            </div>
            <StatusPill status={status} label={statusLabel} />
          </header>

          <div style={previewCard}>
            <div style={previewBubble}>
              <ChannelIcon logoPath={logoPath} title={title} />
              <strong style={previewName}>{title}</strong>
              <span style={previewText}>{previewPrompt}</span>
            </div>
          </div>

          <p style={detailDescription}>{description}</p>

          {children}
        </div>
      </main>
    </div>
  )
}

function ChannelInfoGrid({
  items
}: {
  items: Array<{ label: string; value: string }>
}): JSX.Element {
  const t = useT()
  return (
    <section style={detailSection}>
      <h2 style={detailSectionTitle}>{t('channels.detail.info')}</h2>
      <dl style={infoGrid}>
        {items.map((item) => (
          <div key={item.label} style={infoRow}>
            <dt style={infoLabel}>{item.label}</dt>
            <dd style={infoValue}>{item.value}</dd>
          </div>
        ))}
      </dl>
    </section>
  )
}

const page: CSSProperties = catalogStyles.page
const browseHeader: CSSProperties = catalogStyles.browseHeader
const heroTitle: CSSProperties = catalogStyles.heroTitle
const searchRow: CSSProperties = catalogStyles.searchRow
const browseMain: CSSProperties = catalogStyles.browseMain
const compactItem: CSSProperties = catalogStyles.compactItem
const rowTitle: CSSProperties = catalogStyles.rowTitle
const rowTitleLine: CSSProperties = catalogStyles.rowTitleLine
const rowDesc: CSSProperties = catalogStyles.rowDesc
const emptyText: CSSProperties = catalogStyles.emptyText

const contentShell: CSSProperties = {
  flex: 1,
  minHeight: 0,
  minWidth: 0,
  display: 'flex',
  position: 'relative'
}

const contentPane: CSSProperties = {
  flex: 1,
  minWidth: 0,
  minHeight: 0,
  display: 'flex',
  flexDirection: 'column'
}

const channelText: CSSProperties = {
  minWidth: 0,
  flex: 1,
  display: 'flex',
  flexDirection: 'column'
}

const channelIcon: CSSProperties = {
  width: 40,
  height: 40,
  borderRadius: 8,
  flexShrink: 0,
  backgroundColor: 'var(--bg-secondary)'
}

const fallbackIcon: CSSProperties = {
  ...channelIcon,
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  color: 'var(--text-secondary)',
  fontSize: 15,
  fontWeight: 700
}

const variantBadge: CSSProperties = {
  flexShrink: 0,
  display: 'inline-flex',
  alignItems: 'center',
  height: 18,
  padding: '0 6px',
  borderRadius: 999,
  border: '1px solid var(--border-default)',
  color: 'var(--text-secondary)',
  fontSize: 10,
  fontWeight: 600
}

const channelStatus: CSSProperties = {
  minWidth: 112,
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'flex-end',
  gap: 6,
  color: 'var(--text-secondary)'
}

const statusDot: CSSProperties = {
  width: 7,
  height: 7,
  borderRadius: '50%',
  flexShrink: 0
}

const statusLabelStyle: CSSProperties = {
  minWidth: 0,
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap',
  fontSize: 11
}

const detailPage: CSSProperties = {
  ...catalogStyles.page,
  overflow: 'hidden'
}

const detailMain: CSSProperties = {
  flex: 1,
  minHeight: 0,
  overflow: 'auto',
  width: '100%'
}

const detailMainContent: CSSProperties = {
  width: 'min(760px, calc(100vw - 56px))',
  margin: '0 auto',
  padding: '58px 0 56px'
}

const detailHeader: CSSProperties = {
  display: 'flex',
  alignItems: 'flex-start',
  justifyContent: 'space-between',
  gap: 16,
  marginBottom: 28
}

const detailIdentity: CSSProperties = {
  minWidth: 0,
  display: 'flex',
  alignItems: 'center',
  gap: 14
}

const detailTitle: CSSProperties = {
  margin: 0,
  color: 'var(--text-primary)',
  fontSize: 25,
  lineHeight: 1.18,
  fontWeight: 700,
  letterSpacing: 0
}

const detailSubtitle: CSSProperties = {
  margin: '6px 0 0',
  color: 'var(--text-secondary)',
  fontSize: 14,
  lineHeight: 1.45
}

const previewCard: CSSProperties = {
  minHeight: 132,
  borderRadius: 8,
  background:
    'linear-gradient(135deg, color-mix(in srgb, #9bc2ff 78%, white 8%), color-mix(in srgb, #eadcff 86%, white 5%))',
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  padding: 18,
  marginBottom: 34
}

const previewBubble: CSSProperties = {
  maxWidth: 'min(520px, 100%)',
  minHeight: 38,
  display: 'inline-flex',
  alignItems: 'center',
  gap: 9,
  padding: '8px 13px',
  borderRadius: 8,
  backgroundColor: 'color-mix(in srgb, white 88%, transparent)',
  color: '#101216',
  fontSize: 13,
  boxShadow: '0 6px 20px color-mix(in srgb, #5f6f90 14%, transparent)'
}

const previewName: CSSProperties = {
  flexShrink: 0,
  fontSize: 13,
  fontWeight: 700,
  color: '#05070a'
}

const previewText: CSSProperties = {
  minWidth: 0,
  color: '#101216',
  lineHeight: 1.35
}

const detailDescription: CSSProperties = {
  margin: '0 8px 38px',
  color: 'var(--text-primary)',
  fontSize: 14,
  lineHeight: 1.55
}

const detailSection: CSSProperties = {
  marginTop: 30
}

const detailSectionTitle: CSSProperties = {
  margin: '0 0 12px',
  color: 'var(--text-primary)',
  fontSize: 15,
  lineHeight: 1.35,
  fontWeight: 700,
  letterSpacing: 0
}

const infoGrid: CSSProperties = {
  margin: 0,
  border: '1px solid var(--border-default)',
  borderRadius: 8,
  overflow: 'hidden'
}

const infoRow: CSSProperties = {
  display: 'grid',
  gridTemplateColumns: '150px minmax(0, 1fr)',
  gap: 12,
  padding: '12px 14px',
  borderBottom: '1px solid var(--border-subtle)'
}

const infoLabel: CSSProperties = {
  color: 'var(--text-dimmed)',
  fontSize: 12
}

const infoValue: CSSProperties = {
  margin: 0,
  minWidth: 0,
  color: 'var(--text-secondary)',
  fontSize: 12,
  lineHeight: 1.45,
  overflowWrap: 'anywhere'
}

const diagnosticsPanel: CSSProperties = {
  maxWidth: 760,
  margin: '0 auto',
  border: '1px solid var(--border-default)',
  borderRadius: 8,
  backgroundColor: 'var(--bg-secondary)',
  color: 'var(--text-secondary)',
  padding: '12px 14px',
  fontSize: 12
}
