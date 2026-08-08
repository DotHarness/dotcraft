import { useCallback, useEffect, useMemo, useRef, useState, type CSSProperties, type JSX } from 'react'
import {
  Archive,
  Hand,
  ListChecks,
  OctagonAlert,
  Pencil,
  Plus,
  TestTube2,
  Trash2
} from 'lucide-react'
import { addToast } from '../../stores/toastStore'
import { AppearancePanel } from './panels/AppearancePanel'
import { VoicePanel } from './panels/VoicePanel'
import { normalizeLocale, SUPPORTED_LOCALES, type AppLocale } from '../../../shared/locales'
import { useSetUiLocale, useT } from '../../contexts/LocaleContext'
import type { MessageKey } from '../../../shared/locales'
import {
  resolveRemoteWebSocketConfig,
  type ConnectionSettingsDraft,
  type RemoteConnectionValidationCode
} from '../../../shared/remoteConnection'
import {
  ANTHROPIC_PROTOCOL,
  defaultProviderEndpoint,
  DESKTOP_PROVIDER_PROTOCOLS,
  normalizeProviderProtocol,
  OPENAI_RESPONSES_PROTOCOL,
  providerProtocolLabel,
  type DesktopProviderProtocol
} from '../../../shared/providerProtocols'
import { useUIStore } from '../../stores/uiStore'
import { useConnectionStore } from '../../stores/connectionStore'
import { usePluginStore } from '../../stores/pluginStore'
import { useSkillsStore } from '../../stores/skillsStore'
import { usePendingRestartStore } from '../../stores/pendingRestartStore'
import {
  replaceCurrentAppNavigationLocation,
  runWithoutAppNavigationRecording
} from '../../stores/appNavigationStore'
import { useSettingsWorkspaceConfigChangeEffects } from '../../hooks/useSettingsWorkspaceConfigChangeEffects'
import { SecretInput } from '../channels/FormShared'
import { stringifyComposerDraftSegments } from '../conversation/richInputSerialization'
import type { ComposerDraftSegment } from '../../types/composerDraft'
import { ArchivedThreadsSettingsView } from './ArchivedThreadsSettingsView'
import { ActionTooltip } from '../ui/ActionTooltip'
import { ExtensionsIcon, FolderIcon, OpenInBrowserIcon, RefreshIcon, WrenchIcon } from '../ui/AppIcons'
import { IconButton } from '../ui/IconButton'
import { Input } from '../ui/Input'
import { Button } from '../ui/Button'
import { Skeleton } from '../ui/Skeleton'
import { InputWithAction } from '../ui/InputWithAction'
import { SelectionCard, ResolvedPill } from '../ui/SelectionCard'
import { PillSwitch } from '../ui/PillSwitch'
import { useConfirmDialog } from '../ui/ConfirmDialog'
import { SettingsGroup, SettingsRow } from './SettingsGroup'
import {
  SETTINGS_SURFACE_CLASS,
  settingsDescriptionStyle,
  settingsErrorTextStyle,
  settingsHintStyle,
  settingsMetaTextStyle,
  settingsPlaceholderStyle
} from './settingsTypography'
import { SettingsDescriptionWithLearnMore } from './SettingsLearnMoreLink'
import { SettingsPanelShell } from './SettingsPanelShell'
import { SourceControlPanel } from './panels/SourceControlPanel'
import { SettingsBreadcrumb } from './SettingsBreadcrumb'
import { PluginCatalogItem, PluginIcon, pluginSubtitle, pluginTitle } from '../plugins/PluginCatalogItem'
import { PluginInstallDialog } from '../plugins/PluginInstallDialog'
import {
  EditableKeyValueList,
  EditableValueList,
  normalizeKeyValueRows,
  normalizeValueRows,
  rowsToRecord,
  rowsToValues,
  type KeyValueRow,
  type ValueRow
} from './ui/EditableList'
import { SettingsSelect } from './ui/SettingsSelect'
import { GeneralPanel } from './panels/GeneralPanel'
import { ConnectionPanel } from './panels/ConnectionPanel'
import { ServersPanel } from './panels/servers/ServersPanel'
import { ProviderProtocolIcon } from './panels/ProviderProtocolIcon'
import { UsagePanel } from './panels/UsagePanel'
import { UsageOverview } from './UsageOverview'
import { ProfilePanel } from './panels/ProfilePanel'
import { ProfileView } from './ProfileView'
import { McpPanel } from './panels/McpPanel'
import { HooksPanel } from './panels/HooksPanel'
import { SubAgentsPanel } from './panels/SubAgentsPanel'
import { DesktopExtensionSettingsPanel } from '../extensions/DesktopExtensionSettingsPanel'
import { findDesktopSettingsPanelExtension } from '../../utils/desktopExtensionRegistry'
import {
  useMcpStore,
  type McpServerConfigWire,
  type McpServerStatusWire,
  type McpTransport
} from '../../stores/mcpStore'
import type {
  BinarySource,
  BrowserUseApprovalMode,
  TaskCompletionNotificationMode
} from '../../../preload/api'
import type { WorkspaceConfigChangedPayload } from '../../utils/workspaceConfigChanged'
import { slugProviderId, uniqueProviderId } from '../../utils/providerId'
import { formatPlanLabel } from '../../utils/chatgptPlan'
import {
  cloneModelPreference,
  createManualModelPreference,
  findProviderPreference,
  readProviderPreferences,
  setProviderPreference,
  toContractProviderPreferences,
  type ModelPreference,
  type ProviderPreferences
} from '../../../shared/modelPreference'
import {
  parseModelCatalogItems,
  type ModelCatalogItem
} from '../../stores/modelCatalogStore'
import {
  createCatalogDefaultPreference,
  normalizePreferenceForModel,
  PreferenceModelPicker
} from '../conversation/PreferenceModelPicker'

declare const __APP_VERSION__: string | undefined

interface SettingsViewProps {
  workspacePath?: string
  identityWorkspacePath?: string
  onThreadListRefreshRequested?: () => void
  workspaceConfigChange?: WorkspaceConfigChangedPayload | null
  workspaceConfigChangeSeq?: number
  openChromeSettingsSeq?: number
}

interface McpTestResultWire {
  success: boolean
  errorCode?: string
  errorMessage?: string
  toolCount?: number
}

interface WorkspaceCoreConfig {
  providerId: string | null
  providerPreferences: ProviderPreferences
  welcomeSuggestionsEnabled: boolean | null
  skillsSelfLearningEnabled: boolean | null
  memoryAutoConsolidateEnabled: boolean | null
  dreamsEnabled: boolean | null
  dreamsInterval: string | null
  dreamsThreadLookbackCount: number | null
  dreamsAutoApply: boolean | null
  defaultApprovalPolicy: VisibleApprovalPolicy | null
}

interface WorkspaceCoreConfigResult {
  workspace: WorkspaceCoreConfig
  userDefaults: WorkspaceCoreConfig
}

const EMPTY_WORKSPACE_CORE_CONFIG: WorkspaceCoreConfig = {
  providerId: null,
  providerPreferences: {},
  welcomeSuggestionsEnabled: null,
  skillsSelfLearningEnabled: null,
  memoryAutoConsolidateEnabled: null,
  dreamsEnabled: null,
  dreamsInterval: null,
  dreamsThreadLookbackCount: null,
  dreamsAutoApply: null,
  defaultApprovalPolicy: null
}

interface ProviderCapabilitiesWire {
  streamingChat?: boolean
  toolCalling?: boolean
  modelListing?: boolean
  tokenUsageReporting?: boolean
  cachedInputUsageReporting?: boolean
  promptCacheRequestShaping?: boolean
  extendedThinking?: boolean
  toolChoiceControls?: boolean
  rawMetadataPassthrough?: boolean
}

interface ProviderInfoWire {
  id: string
  displayName: string
  protocol: DesktopProviderProtocol
  apiKey?: string | null
  hasApiKey: boolean
  endPoint: string
  networkTimeoutSeconds?: number | null
  supportsHostedImageGeneration?: boolean
  isImplicit: boolean
  capabilities?: ProviderCapabilitiesWire
  authMethod?: 'apiKey' | 'chatgptOAuth'
  chatGptAccountId?: string | null
  chatGptPlanType?: string | null
}

interface ProviderDraft {
  id: string
  displayName: string
  protocol: DesktopProviderProtocol
  apiKey: string
  endPoint: string
  networkTimeoutSeconds: string
  authMethod: 'apiKey' | 'chatgptOAuth'
  supportsHostedImageGeneration: boolean
  supportsHostedImageGenerationTouched: boolean
}

type ProviderEditorId = string | '__new__' | null

interface ProviderTestResultWire {
  success: boolean
  providerId?: string | null
  protocol?: string
  models?: Array<{ id?: string; Id?: string }>
  errorCode?: string
  errorMessage?: string
}

interface ActiveRemoteStackRef {
  hostId: string
  stackId: string
}

// Canonical id/displayName for ChatGPT-subscription providers. Must match the literals
// written by OpenAIAuthBindingPersistence.BindProviderToOAuth on the backend
// (src/DotCraft.Core/Auth/OpenAI/OpenAIAuthBindingPersistence.cs) so that re-binding
// from the form lines up with the backend's upsert key and displayName.
const OPENAI_CHATGPT_DEFAULT_ID = 'openai'
const OPENAI_CHATGPT_DISPLAY_NAME = 'OpenAI (ChatGPT)'

function createProviderDraft(): ProviderDraft {
  return withDefaultHostedImageGenerationSupport({
    id: '',
    displayName: '',
    protocol: OPENAI_RESPONSES_PROTOCOL,
    apiKey: '',
    endPoint: defaultProviderEndpoint(OPENAI_RESPONSES_PROTOCOL),
    networkTimeoutSeconds: '',
    authMethod: 'apiKey',
    supportsHostedImageGeneration: false,
    supportsHostedImageGenerationTouched: false
  })
}

function providerDraftFromInfo(provider: ProviderInfoWire): ProviderDraft {
  return {
    id: provider.id,
    displayName: provider.displayName,
    protocol: normalizeProviderProtocol(provider.protocol),
    apiKey: provider.hasApiKey ? '********' : '',
    endPoint: provider.endPoint ?? '',
    networkTimeoutSeconds:
      typeof provider.networkTimeoutSeconds === 'number' ? String(provider.networkTimeoutSeconds) : '',
    authMethod: provider.authMethod === 'chatgptOAuth' ? 'chatgptOAuth' : 'apiKey',
    supportsHostedImageGeneration: provider.supportsHostedImageGeneration === true,
    supportsHostedImageGenerationTouched: true
  }
}

function normalizeProviderList(value: unknown): ProviderInfoWire[] {
  const source = value != null && typeof value === 'object' ? value as { providers?: unknown } : {}
  if (!Array.isArray(source.providers)) return []
  return source.providers
    .map((item): ProviderInfoWire | null => {
      if (item == null || typeof item !== 'object') return null
      const raw = item as Partial<ProviderInfoWire>
      const id = typeof raw.id === 'string' ? raw.id.trim() : ''
      if (!id) return null
      const rawAuthMethod = typeof raw.authMethod === 'string' ? raw.authMethod.toLowerCase() : ''
      return {
        id,
        displayName: typeof raw.displayName === 'string' && raw.displayName.trim() !== '' ? raw.displayName : id,
        protocol: normalizeProviderProtocol(raw.protocol),
        apiKey: typeof raw.apiKey === 'string' ? raw.apiKey : null,
        hasApiKey: raw.hasApiKey === true,
        endPoint: typeof raw.endPoint === 'string' ? raw.endPoint : '',
        networkTimeoutSeconds:
          typeof raw.networkTimeoutSeconds === 'number' && Number.isFinite(raw.networkTimeoutSeconds)
            ? raw.networkTimeoutSeconds
            : null,
        supportsHostedImageGeneration: raw.supportsHostedImageGeneration === true,
        isImplicit: raw.isImplicit === true,
        capabilities: raw.capabilities,
        authMethod: rawAuthMethod === 'chatgptoauth' ? 'chatgptOAuth' : 'apiKey',
        chatGptAccountId: typeof raw.chatGptAccountId === 'string' && raw.chatGptAccountId.trim() !== ''
          ? raw.chatGptAccountId
          : null,
        chatGptPlanType: typeof raw.chatGptPlanType === 'string' && raw.chatGptPlanType.trim() !== ''
          ? raw.chatGptPlanType
          : null
      }
    })
    .filter((item): item is ProviderInfoWire => item != null)
}

function canConfigureHostedImageGeneration(provider: Pick<ProviderDraft, 'protocol' | 'authMethod'>): boolean {
  return provider.protocol === OPENAI_RESPONSES_PROTOCOL || provider.authMethod === 'chatgptOAuth'
}

function withDefaultHostedImageGenerationSupport(draft: ProviderDraft): ProviderDraft {
  if (draft.supportsHostedImageGenerationTouched) return draft
  return {
    ...draft,
    supportsHostedImageGeneration: defaultHostedImageGenerationSupport(draft)
  }
}

function defaultHostedImageGenerationSupport(provider: Pick<ProviderDraft, 'protocol' | 'authMethod' | 'endPoint'>): boolean {
  if (provider.authMethod === 'chatgptOAuth') return true
  if (provider.protocol !== OPENAI_RESPONSES_PROTOCOL) return false
  return isOfficialOpenAIEndpoint(provider.endPoint.trim() || defaultProviderEndpoint(OPENAI_RESPONSES_PROTOCOL))
}

function isOfficialOpenAIEndpoint(endpoint: string): boolean {
  try {
    const candidate = new URL(endpoint)
    const official = new URL(defaultProviderEndpoint(OPENAI_RESPONSES_PROTOCOL))
    return candidate.protocol.toLowerCase() === official.protocol.toLowerCase() &&
      candidate.hostname.toLowerCase() === official.hostname.toLowerCase() &&
      candidate.port === official.port &&
      candidate.pathname.replace(/\/+$/, '').toLowerCase() === official.pathname.replace(/\/+$/, '').toLowerCase()
  } catch {
    return false
  }
}

type VisibleApprovalPolicy = 'default' | 'autoApprove'
type DreamsRunStatus = 'running' | 'succeeded' | 'skipped' | 'failed' | 'canceled'
type DreamsReviewStatus = 'pending' | 'applied' | 'discarded' | 'archived'

interface DreamsRunState {
  id: string
  status: DreamsRunStatus
  startedAt: string
  endedAt?: string | null
  processedThreadCount: number
  candidateThreadCount: number
  dreamWritten: boolean
  historyWritten: boolean
  topicFilesWritten: number
  topicFilesDeleted: number
  evidenceSearchCount: number
  evidenceReadCount: number
  outputStoreId?: string | null
  reviewStatus?: DreamsReviewStatus | null
  autoApplied: boolean
  errorType?: string | null
  evidenceThreadIds: string[]
  writtenPaths: string[]
  threadId?: string | null
  turnId?: string | null
  turnIds: string[]
  trigger?: string | null
  message?: string | null
  inputManifestPath?: string | null
}

interface DreamsStatus {
  enabled: boolean
  interval: string
  threadLookbackCount: number
  autoApply: boolean
  historyTailChars: number
  minCompletedTurnsSinceLastRun: number
  nextRunAt?: string | null
  running: boolean
  activeDreamStoreId?: string | null
  lastRun: DreamsRunState | null
}

const DEFAULT_DREAMS_INTERVAL = '24:00:00'
const DEFAULT_DREAMS_THREAD_LOOKBACK_COUNT = 20
const DREAMS_INTERVAL_OPTIONS = ['06:00:00', '12:00:00', '24:00:00', '168:00:00'] as const
const DREAMS_THREAD_LOOKBACK_OPTIONS = [10, 20, 50, 100] as const
const SETTINGS_SELECT_WIDTH = '240px'

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => window.setTimeout(resolve, ms))
}

function formatDreamsIntervalOption(value: string, t: (key: MessageKey | string, vars?: Record<string, string | number>) => string): string {
  switch (value) {
    case '06:00:00':
      return t('settings.personalization.dreamsInterval.6h')
    case '12:00:00':
      return t('settings.personalization.dreamsInterval.12h')
    case '24:00:00':
      return t('settings.personalization.dreamsInterval.24h')
    case '168:00:00':
      return t('settings.personalization.dreamsInterval.7d')
    default:
      return value
  }
}

function normalizeVisibleApprovalPolicy(value: unknown): VisibleApprovalPolicy | null {
  return value === 'default' || value === 'autoApprove' ? value : null
}

function normalizeDreamsInterval(value: unknown): string | null {
  if (typeof value !== 'string') return null
  const trimmed = value.trim()
  if (trimmed === '') return null
  const dayMatch = /^(\d+)\.(\d{1,2}):(\d{2}):(\d{2})$/.exec(trimmed)
  if (dayMatch) {
    const days = Number(dayMatch[1])
    const hours = Number(dayMatch[2])
    const minutes = Number(dayMatch[3])
    const seconds = Number(dayMatch[4])
    if (Number.isFinite(days) && Number.isFinite(hours) && minutes < 60 && seconds < 60) {
      return `${String(days * 24 + hours).padStart(2, '0')}:${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}`
    }
  }
  return trimmed
}

function normalizeDreamsRunStatus(value: unknown): DreamsRunStatus {
  return value === 'running' || value === 'succeeded' || value === 'skipped' || value === 'failed' || value === 'canceled'
    ? value
    : 'skipped'
}

function normalizeDreamsReviewStatus(value: unknown): DreamsReviewStatus | null {
  return value === 'pending' || value === 'applied' || value === 'discarded' || value === 'archived'
    ? value
    : null
}

function asStringArray(value: unknown): string[] {
  return Array.isArray(value)
    ? value.filter((item): item is string => typeof item === 'string')
    : []
}

function normalizeDreamsRunState(value: unknown): DreamsRunState | null {
  if (value == null || typeof value !== 'object') return null
  const source = value as Partial<DreamsRunState>
  return {
    id: typeof source.id === 'string' ? source.id : '',
    status: normalizeDreamsRunStatus(source.status),
    startedAt: typeof source.startedAt === 'string' ? source.startedAt : '',
    endedAt: typeof source.endedAt === 'string' ? source.endedAt : null,
    processedThreadCount:
      typeof source.processedThreadCount === 'number' && Number.isFinite(source.processedThreadCount)
        ? source.processedThreadCount
        : 0,
    candidateThreadCount:
      typeof source.candidateThreadCount === 'number' && Number.isFinite(source.candidateThreadCount)
        ? source.candidateThreadCount
        : 0,
    dreamWritten: source.dreamWritten === true,
    historyWritten: source.historyWritten === true,
    topicFilesWritten:
      typeof source.topicFilesWritten === 'number' && Number.isFinite(source.topicFilesWritten)
        ? source.topicFilesWritten
        : 0,
    topicFilesDeleted:
      typeof source.topicFilesDeleted === 'number' && Number.isFinite(source.topicFilesDeleted)
        ? source.topicFilesDeleted
        : 0,
    evidenceSearchCount:
      typeof source.evidenceSearchCount === 'number' && Number.isFinite(source.evidenceSearchCount)
        ? source.evidenceSearchCount
        : 0,
    evidenceReadCount:
      typeof source.evidenceReadCount === 'number' && Number.isFinite(source.evidenceReadCount)
        ? source.evidenceReadCount
        : 0,
    outputStoreId: typeof source.outputStoreId === 'string' ? source.outputStoreId : null,
    reviewStatus: normalizeDreamsReviewStatus(source.reviewStatus),
    autoApplied: source.autoApplied === true,
    errorType: typeof source.errorType === 'string' ? source.errorType : null,
    evidenceThreadIds: asStringArray(source.evidenceThreadIds),
    writtenPaths: asStringArray(source.writtenPaths),
    threadId: typeof source.threadId === 'string' ? source.threadId : null,
    turnId: typeof source.turnId === 'string' ? source.turnId : null,
    turnIds: asStringArray(source.turnIds),
    trigger: typeof source.trigger === 'string' ? source.trigger : null,
    message: typeof source.message === 'string' ? source.message : null,
    inputManifestPath: typeof source.inputManifestPath === 'string' ? source.inputManifestPath : null
  }
}

function normalizeDreamsRunList(value: unknown): DreamsRunState[] {
  const source = value != null && typeof value === 'object' ? value as { runs?: unknown } : {}
  return Array.isArray(source.runs)
    ? source.runs.map(normalizeDreamsRunState).filter((run): run is DreamsRunState => run != null)
    : []
}

function normalizeDreamsStatus(value: unknown): DreamsStatus {
  const source = value != null && typeof value === 'object' ? value as Partial<DreamsStatus> : {}
  const lastRun = normalizeDreamsRunState(source.lastRun)
  return {
    enabled: source.enabled !== false,
    interval: normalizeDreamsInterval(source.interval) ?? DEFAULT_DREAMS_INTERVAL,
    threadLookbackCount:
      typeof source.threadLookbackCount === 'number' && Number.isInteger(source.threadLookbackCount) && source.threadLookbackCount > 0
        ? source.threadLookbackCount
        : DEFAULT_DREAMS_THREAD_LOOKBACK_COUNT,
    autoApply: source.autoApply === true,
    historyTailChars:
      typeof source.historyTailChars === 'number' && Number.isFinite(source.historyTailChars)
        ? source.historyTailChars
        : 0,
    minCompletedTurnsSinceLastRun:
      typeof source.minCompletedTurnsSinceLastRun === 'number' && Number.isFinite(source.minCompletedTurnsSinceLastRun)
        ? source.minCompletedTurnsSinceLastRun
        : 0,
    nextRunAt: typeof source.nextRunAt === 'string' ? source.nextRunAt : null,
    running: source.running === true,
    activeDreamStoreId: typeof source.activeDreamStoreId === 'string' ? source.activeDreamStoreId : null,
    lastRun
  }
}

function resolveEffectiveProviderPreference(
  workspaceProviderPreferences: ProviderPreferences,
  userProviderPreferences: ProviderPreferences,
  providerId: string
): ModelPreference | null {
  return findProviderPreference(workspaceProviderPreferences, providerId)
    ?? findProviderPreference(userProviderPreferences, providerId)
}

function normalizeWorkspaceCoreConfig(value: unknown): WorkspaceCoreConfig {
  const source = value != null && typeof value === 'object' ? value as Partial<WorkspaceCoreConfig> : {}
  return {
    providerId: typeof source.providerId === 'string' ? source.providerId : null,
    providerPreferences: readProviderPreferences(source.providerPreferences),
    welcomeSuggestionsEnabled:
      typeof source.welcomeSuggestionsEnabled === 'boolean'
        ? source.welcomeSuggestionsEnabled
        : null,
    skillsSelfLearningEnabled:
      typeof source.skillsSelfLearningEnabled === 'boolean'
        ? source.skillsSelfLearningEnabled
        : null,
    memoryAutoConsolidateEnabled:
      typeof source.memoryAutoConsolidateEnabled === 'boolean'
        ? source.memoryAutoConsolidateEnabled
        : null,
    dreamsEnabled:
      typeof source.dreamsEnabled === 'boolean'
        ? source.dreamsEnabled
        : null,
    dreamsInterval: normalizeDreamsInterval(source.dreamsInterval),
    dreamsThreadLookbackCount:
      typeof source.dreamsThreadLookbackCount === 'number' && Number.isInteger(source.dreamsThreadLookbackCount) && source.dreamsThreadLookbackCount > 0
        ? source.dreamsThreadLookbackCount
        : null,
    dreamsAutoApply:
      typeof source.dreamsAutoApply === 'boolean'
        ? source.dreamsAutoApply
        : null,
    defaultApprovalPolicy: normalizeVisibleApprovalPolicy(source.defaultApprovalPolicy)
  }
}

function createEmptyWorkspaceCoreResult(): WorkspaceCoreConfigResult {
  return {
    workspace: { ...EMPTY_WORKSPACE_CORE_CONFIG },
    userDefaults: { ...EMPTY_WORKSPACE_CORE_CONFIG }
  }
}

function normalizeWorkspaceCoreResult(value: unknown): WorkspaceCoreConfigResult {
  if (value == null || typeof value !== 'object') {
    return createEmptyWorkspaceCoreResult()
  }

  const source = value as Partial<WorkspaceCoreConfigResult>
  return {
    workspace: normalizeWorkspaceCoreConfig(source.workspace),
    userDefaults: normalizeWorkspaceCoreConfig(source.userDefaults)
  }
}

type WorkspaceCoreReadApi = {
  workspaceConfig?: {
    getCore?: (() => Promise<unknown>) | undefined
  } | undefined
} | undefined

function getWorkspaceCoreReader(api: WorkspaceCoreReadApi): (() => Promise<unknown>) | null {
  const getCore = api?.workspaceConfig?.getCore
  return typeof getCore === 'function' ? getCore : null
}

export async function readWorkspaceCoreSafeFromApi(
  api: WorkspaceCoreReadApi
): Promise<WorkspaceCoreConfigResult> {
  const getCore = getWorkspaceCoreReader(api)
  if (!getCore) {
    return createEmptyWorkspaceCoreResult()
  }

  try {
    return normalizeWorkspaceCoreResult(await getCore())
  } catch {
    return createEmptyWorkspaceCoreResult()
  }
}

export async function readWorkspaceCoreStrictFromApi(
  api: WorkspaceCoreReadApi
): Promise<WorkspaceCoreConfigResult> {
  const getCore = getWorkspaceCoreReader(api)
  if (!getCore) {
    throw new Error('Workspace core API is unavailable')
  }

  return normalizeWorkspaceCoreResult(await getCore())
}

type ConnectionMode = 'local' | 'remote'

interface ChromeSetupStatus {
  extension: unknown
  nativeHost: unknown
  chromeRunning: unknown
  installedBrowsers: unknown
  backend?: unknown
  bridge: unknown
}

type ChromeSetupTone = 'ok' | 'warning' | 'error' | 'muted'

const DEFAULT_WS_HOST = '127.0.0.1'
const DEFAULT_WS_PORT = 9100

const REMOTE_URL_ERROR_KEYS: Record<RemoteConnectionValidationCode, MessageKey> = {
  'missing-url': 'settings.remoteUrlError.missing',
  'invalid-url': 'settings.remoteUrlError.invalid',
  'unsupported-protocol': 'settings.remoteUrlError.protocol'
}

function createEmptyMcpServer(): McpServerConfigWire {
  return {
    name: '',
    enabled: true,
    transport: 'stdio',
    command: '',
    args: [],
    env: {},
    envVars: [],
    cwd: '',
    url: '',
    bearerTokenEnvVar: '',
    httpHeaders: {},
    envHttpHeaders: {},
    startupTimeoutSec: null,
    toolTimeoutSec: null
  }
}

function isPluginManagedMcpServer(server: McpServerConfigWire, originsEnabled: boolean): boolean {
  return (originsEnabled && server.origin?.kind === 'plugin') || server.readOnly === true
}

function toContractMcpServer(server: McpServerConfigWire) {
  return {
    ...server,
    origin: server.origin ? { ...server.origin } : server.origin
  }
}

function mcpPluginSourceLabel(server: McpServerConfigWire, t: (key: MessageKey | string, vars?: Record<string, string | number>) => string): string {
  return t('settings.mcp.origin.fromPlugin', {
    plugin: server.origin?.pluginDisplayName || server.origin?.pluginId || 'plugin'
  })
}

function getStatusTone(
  t: (key: MessageKey | string, vars?: Record<string, string | number>) => string,
  status?: McpServerStatusWire
): { label: string; color: string } {
  switch (status?.startupState) {
    case 'ready':
      return { label: t('settings.mcp.status.connected'), color: '#3fb950' }
    case 'starting':
      return { label: t('settings.mcp.status.connecting'), color: '#d29922' }
    case 'error':
      return { label: t('settings.mcp.status.error'), color: '#f85149' }
    case 'disabled':
      return { label: t('settings.mcp.disabledSuffix').replace(/^ · /, ''), color: 'var(--text-dimmed)' }
    default:
      return { label: t('settings.mcp.status.idle'), color: 'var(--text-dimmed)' }
  }
}

function cardStyle(): CSSProperties {
  return {
    border: '1px solid var(--border-default)',
    borderRadius: '10px',
    background: 'var(--bg-secondary)',
    padding: '14px 16px'
  }
}

function providerRowStyle(active: boolean): CSSProperties {
  return {
    ...cardStyle(),
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: '16px',
    cursor: 'pointer',
    textAlign: 'left',
    background: active
      ? 'color-mix(in srgb, var(--accent) 8%, var(--bg-secondary))'
      : 'var(--bg-secondary)',
    borderColor: active
      ? 'color-mix(in srgb, var(--accent) 45%, var(--border-default))'
      : 'var(--border-default)'
  }
}

function providerBadgeStyle(tone: 'neutral' | 'accent'): CSSProperties {
  return {
    display: 'inline-flex',
    alignItems: 'center',
    minHeight: 20,
    padding: '2px 7px',
    borderRadius: 999,
    background: tone === 'accent'
      ? 'color-mix(in srgb, var(--accent) 16%, transparent)'
      : 'var(--bg-tertiary)',
    color: tone === 'accent' ? 'var(--accent)' : 'var(--text-secondary)',
    fontSize: 11,
    fontWeight: 600,
    lineHeight: 1
  }
}

function providerFieldStackStyle(): CSSProperties {
  return {
    display: 'flex',
    flexDirection: 'column',
    gap: '12px'
  }
}

function providerFieldGridStyle(): CSSProperties {
  return {
    display: 'grid',
    gridTemplateColumns: 'repeat(auto-fit, minmax(240px, 1fr))',
    gap: '12px'
  }
}

function providerFooterStyle(): CSSProperties {
  return {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
    gap: '12px',
    flexWrap: 'wrap'
  }
}

function providerInlineStatusStyle(tone: 'success' | 'warning' | 'error' | 'neutral'): CSSProperties {
  const color =
    tone === 'success'
      ? 'var(--success, #3fb950)'
      : tone === 'warning'
        ? 'var(--warning, #d29922)'
        : tone === 'error'
          ? 'var(--error, #f85149)'
          : 'var(--text-dimmed)'
  return {
    minWidth: 0,
    maxWidth: 'min(420px, 100%)',
    display: 'inline-flex',
    alignItems: 'center',
    gap: '8px',
    color,
    fontSize: '12px',
    lineHeight: 1.4
  }
}

function providerInlineStatusDotStyle(tone: 'success' | 'warning' | 'error' | 'neutral'): CSSProperties {
  const background =
    tone === 'success'
      ? 'var(--success, #3fb950)'
      : tone === 'warning'
        ? 'var(--warning, #d29922)'
        : tone === 'error'
          ? 'var(--error, #f85149)'
          : 'var(--text-dimmed)'
  return {
    width: 7,
    height: 7,
    borderRadius: 999,
    background,
    flexShrink: 0
  }
}

function settingsMainStyle(): CSSProperties {
  return {
    flex: 1,
    minWidth: 0,
    overflowY: 'auto',
    padding: '20px',
    scrollbarGutter: 'stable'
  }
}

function settingsContentContainerStyle(): CSSProperties {
  return {
    width: '100%',
    maxWidth: '760px',
    margin: '0 auto',
    boxSizing: 'border-box'
  }
}

function sectionLabelStyle(): CSSProperties {
  return {
    display: 'block',
    fontSize: 'var(--type-secondary-size)',
    lineHeight: 'var(--type-secondary-line-height)',
    fontWeight: 600,
    color: 'var(--text-secondary)',
    marginBottom: '6px'
  }
}

function normalizeBrowserUseDomainInput(input: string): string | null {
  const trimmed = input.trim()
  if (!trimmed || /[\u0000-\u001f]/.test(trimmed)) return null
  const candidate = /^[a-zA-Z][a-zA-Z\d+\-.]*:/.test(trimmed)
    ? trimmed
    : `https://${trimmed}`
  try {
    const domain = new URL(candidate).hostname.trim().toLowerCase().replace(/\.+$/, '')
    return domain || null
  } catch {
    return null
  }
}

function chromeActionToolbarStyle(): CSSProperties {
  return {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'flex-end',
    gap: 8,
    flexWrap: 'wrap',
    flex: '1 1 320px',
    minWidth: 0
  }
}

function mcpSourcePillStyle(): CSSProperties {
  return {
    display: 'inline-flex',
    alignItems: 'center',
    minHeight: 20,
    padding: '2px 7px',
    borderRadius: 999,
    backgroundColor: 'var(--bg-tertiary)',
    color: 'var(--text-secondary)',
    fontSize: 11,
    fontWeight: 600
  }
}

function asRecord(value: unknown): Record<string, unknown> | null {
  return value != null && typeof value === 'object' && !Array.isArray(value)
    ? value as Record<string, unknown>
    : null
}

function setupResultOk(value: unknown): boolean {
  return asRecord(value)?.ok === true
}

function setupResultText(value: unknown, key: string): string {
  const record = asRecord(value)
  const candidate = record?.[key]
  return typeof candidate === 'string' ? candidate : ''
}

function chromeBackendStatus(status: ChromeSetupStatus | null): unknown {
  return status?.backend ?? status?.bridge
}

function normalizeChromeSetupStatus(status: ChromeSetupStatus): ChromeSetupStatus {
  const backend = status.backend ?? status.bridge
  return {
    ...status,
    backend,
    bridge: status.bridge ?? backend
  }
}

function chromeSetupSummary(
  status: ChromeSetupStatus | null,
  t: (key: MessageKey | string, vars?: Record<string, string | number>) => string
): { label: string; tone: ChromeSetupTone } {
  if (!status) return { label: t('settings.chrome.status.notChecked'), tone: 'muted' }
  if (!setupResultOk(status.installedBrowsers)) return { label: t('settings.chrome.status.chromeMissing'), tone: 'error' }
  if (!setupResultOk(status.extension)) return { label: t('settings.chrome.status.extensionMissing'), tone: 'error' }
  if (!setupResultOk(status.nativeHost)) return { label: t('settings.chrome.status.nativeHostMissing'), tone: 'warning' }
  if (!setupResultOk(status.chromeRunning)) return { label: t('settings.chrome.status.notRunning'), tone: 'warning' }
  if (!setupResultOk(chromeBackendStatus(status))) return { label: t('settings.chrome.status.backendDisconnected'), tone: 'warning' }
  return { label: t('settings.chrome.status.connected'), tone: 'ok' }
}

function chromeExtensionManagementUrl(_status: ChromeSetupStatus | null): string {
  return 'chrome://extensions'
}

function chromeNativeHostActionLabel(
  status: ChromeSetupStatus | null,
  installing: boolean,
  t: (key: MessageKey | string, vars?: Record<string, string | number>) => string
): string {
  if (installing) return t('settings.chrome.installingNativeHost')
  const nativeHost = asRecord(status?.nativeHost)
  const safeDetails = asRecord(nativeHost?.safeDetails)
  if (
    status &&
    !setupResultOk(nativeHost) &&
    safeDetails?.exists === false &&
    safeDetails?.hostExists === false
  ) {
    return t('settings.chrome.installHost')
  }
  return t('settings.chrome.repairHost')
}

function statusDotColor(tone: ChromeSetupTone): string {
  if (tone === 'ok') return 'var(--success)'
  if (tone === 'warning') return 'var(--warning)'
  if (tone === 'error') return 'var(--error)'
  return 'var(--text-dimmed)'
}

function ChromeStatusPill({
  label,
  tone
}: {
  label: string
  tone: ChromeSetupTone
}): JSX.Element {
  const color = statusDotColor(tone)
  const bg =
    tone === 'ok'
      ? 'rgba(52, 199, 89, 0.15)'
      : tone === 'warning'
        ? 'rgba(255, 149, 0, 0.15)'
        : tone === 'error'
          ? 'rgba(255, 69, 58, 0.15)'
          : 'var(--bg-tertiary)'

  return (
    <span
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        gap: '6px',
        minHeight: 24,
        padding: '0 9px',
        borderRadius: 999,
        background: bg,
        color,
        fontSize: 12,
        fontWeight: 600
      }}
    >
      <span aria-hidden style={{ width: 7, height: 7, borderRadius: '50%', background: color }} />
      {label}
    </span>
  )
}

interface ChatGptOAuthPanelProps {
  providerId: string
  providerInfo: ProviderInfoWire | null
  /** Current workspace-selected provider id (null when none configured). */
  selectedProviderId: string | null
  /** Whether the current workspace-selected provider has a usable API key. */
  selectedProviderHasApiKey: boolean
  onAfterMutation: () => void
  /** Notify parent that {@link providerId} is now the workspace-selected provider. */
  onProviderActivated?: (providerId: string) => void
}

function ChatGptOAuthPanel({
  providerId,
  providerInfo,
  selectedProviderId,
  selectedProviderHasApiKey,
  onAfterMutation,
  onProviderActivated
}: ChatGptOAuthPanelProps): JSX.Element {
  const t = useT()
  const [pending, setPending] = useState(false)
  const [authorizeUrl, setAuthorizeUrl] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!pending) return
    const unsubscribe = window.api.appServer.onNotification((payload) => {
      if (payload?.method === 'auth/openai/authorizeUrl') {
        const params = payload.params as { url?: string } | undefined
        if (typeof params?.url === 'string') {
          setAuthorizeUrl(params.url)
        }
      }
    })
    return () => unsubscribe()
  }, [pending])

  async function handleSignIn(): Promise<void> {
    setPending(true)
    setError(null)
    setAuthorizeUrl(null)
    try {
      await window.api.appServer.sendRequest(
        'auth/openai/login',
        { providerId, openBrowser: true },
        15 * 60 * 1000 // up to 15 minutes for the user to complete browser flow
      )
      // Auto-activate the OAuth provider when there is no workspace selection yet, or
      // when the current selection is a broken API-key provider (no key). Otherwise leave
      // the user's selection alone to avoid clobbering an intentional choice. Run BEFORE
      // onAfterMutation so the subsequent reloadProviders() reflects the new selection in
      // one pass instead of flickering.
      const shouldActivate =
        !selectedProviderId ||
        selectedProviderId === providerId ||
        !selectedProviderHasApiKey
      let activated = false
      if (shouldActivate && selectedProviderId !== providerId) {
        try {
          await window.api.appServer.sendRequest(
            'workspace/config/update',
            { providerId },
            20_000
          )
          activated = true
          onProviderActivated?.(providerId)
        } catch (activateErr) {
          addToast(t('settings.llm.toast.saveProviderSelectionFailed', {
            error: activateErr instanceof Error ? activateErr.message : String(activateErr)
          }), 'error')
        }
      } else if (selectedProviderId === providerId) {
        // Already the active provider — no switch needed, but still treat as activated for messaging.
        activated = true
      }
      if (activated) {
        addToast(t('settings.llm.toast.chatgptActivated'), 'success')
      } else if (shouldActivate) {
        // Activation attempted but failed — error toast was already shown above.
      } else {
        addToast(t('settings.llm.toast.chatgptActivateSkipped'), 'info')
      }
      onAfterMutation()
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err)
      setError(message)
      addToast(t('settings.llm.authMethod.signInFailed', { error: message }), 'error')
    } finally {
      setPending(false)
      setAuthorizeUrl(null)
    }
  }

  async function handleSignOut(): Promise<void> {
    setPending(true)
    setError(null)
    try {
      await window.api.appServer.sendRequest('auth/openai/logout', { providerId }, 30_000)
      onAfterMutation()
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err)
      setError(message)
    } finally {
      setPending(false)
    }
  }

  async function handleCopyUrl(): Promise<void> {
    if (!authorizeUrl) return
    try {
      await navigator.clipboard.writeText(authorizeUrl)
      addToast(t('settings.llm.authMethod.urlCopied'), 'success')
    } catch {
      // Silent; user can still see the URL in the panel.
    }
  }

  const signedIn = providerInfo?.authMethod === 'chatgptOAuth' && Boolean(providerInfo?.chatGptAccountId)

  return (
    <div style={{ display: 'grid', gap: '12px' }}>
      {signedIn ? (
        <div
          style={{
            padding: '12px 14px',
            borderRadius: '8px',
            border: '1px solid var(--accent)',
            background: 'var(--bg-tertiary)',
            color: 'var(--text-primary)',
            fontSize: '13px',
            lineHeight: 1.55
          }}
        >
          <div style={{ fontWeight: 600 }}>
            {t('settings.llm.authMethod.signedInAs', {
              account: maskAccountId(providerInfo!.chatGptAccountId!),
              plan: providerInfo!.chatGptPlanType ?? 'unknown'
            })}
          </div>
        </div>
      ) : (
        <div
          style={{
            padding: '12px 14px',
            borderRadius: '8px',
            border: '1px dashed var(--border-default)',
            color: 'var(--text-secondary)',
            fontSize: '12px',
            lineHeight: 1.55
          }}
        >
          {t('settings.llm.authMethod.notSignedIn')}
        </div>
      )}

      {pending && authorizeUrl && (
        <div
          style={{
            padding: '10px 12px',
            borderRadius: '8px',
            border: '1px solid var(--border-default)',
            background: 'var(--bg-secondary)',
            display: 'flex',
            flexDirection: 'column',
            gap: '8px'
          }}
        >
          <div style={{ fontSize: '12px', color: 'var(--text-secondary)' }}>
            {t('settings.llm.authMethod.signInPending')}
          </div>
          <div
            style={{
              fontFamily: 'var(--font-mono, monospace)',
              fontSize: '11px',
              wordBreak: 'break-all',
              color: 'var(--text-primary)'
            }}
          >
            {authorizeUrl}
          </div>
          <Button
            size="sm"
            onClick={() => void handleCopyUrl()}
            style={{ alignSelf: 'flex-start' }}
          >
            {t('settings.llm.authMethod.copyUrl')}
          </Button>
        </div>
      )}

      {error && (
        <div style={{ fontSize: '12px', color: 'var(--error, #f85149)' }}>
          {error}
        </div>
      )}

      <div style={{ display: 'flex', gap: '8px', flexWrap: 'wrap' }}>
        <Button
          variant="primary"
          onClick={() => void handleSignIn()}
          disabled={pending}
        >
          {pending
            ? t('settings.llm.authMethod.signInPending')
            : signedIn
              ? t('settings.llm.authMethod.signIn')
              : t('settings.llm.authMethod.signIn')}
        </Button>
        {signedIn && (
          <Button
            onClick={() => void handleSignOut()}
            disabled={pending}
          >
            {t('settings.llm.authMethod.signOut')}
          </Button>
        )}
      </div>
    </div>
  )
}

function maskAccountId(accountId: string): string {
  const trimmed = accountId.trim()
  if (trimmed.length <= 8) return trimmed
  return `${trimmed.slice(0, 4)}…${trimmed.slice(-4)}`
}

export function SettingsView({
  workspacePath,
  identityWorkspacePath,
  onThreadListRefreshRequested,
  workspaceConfigChange = null,
  workspaceConfigChangeSeq = 0,
  openChromeSettingsSeq = 0
}: SettingsViewProps): JSX.Element {
  const t = useT()
  const confirm = useConfirmDialog()
  const isMac = window.api.platform === 'darwin'
  const setUiLocale = useSetUiLocale()
  const setActiveMainView = useUIStore((s) => s.setActiveMainView)
  const activeSettingsTab = useUIStore((s) => s.activeSettingsTab)
  const setActiveSettingsTab = useUIStore((s) => s.setActiveSettingsTab)
  const settingsCloseRequestSeq = useUIStore((s) => s.settingsCloseRequestSeq)
  const showThinkingContent = useUIStore((s) => s.showThinkingContent)
  const setShowThinkingContent = useUIStore((s) => s.setShowThinkingContent)
  const capabilities = useConnectionStore((s) => s.capabilities)
  const setExpectedRestart = useConnectionStore((s) => s.setExpectedRestart)
  const dashboardUrl = useConnectionStore((s) => s.dashboardUrl)
  const plugins = usePluginStore((s) => s.plugins)
  const activeExtensionSettings = useMemo(
    () => findDesktopSettingsPanelExtension(plugins, activeSettingsTab),
    [activeSettingsTab, plugins]
  )
  const fetchPlugins = usePluginStore((s) => s.fetchPlugins)
  const installPlugin = usePluginStore((s) => s.installPlugin)
  const togglePluginEnabled = usePluginStore((s) => s.togglePluginEnabled)
  const fetchSkills = useSkillsStore((s) => s.fetchSkills)
  const mcpStatuses = useMcpStore((s) => s.statuses)
  const setMcpStatuses = useMcpStore((s) => s.setStatuses)
  const [binarySource, setBinarySource] = useState<BinarySource>('bundled')
  const [binaryPath, setBinaryPath] = useState('')
  const [resolvedBinaryPath, setResolvedBinaryPath] = useState<string | null>(null)
  const [resolvingBinary, setResolvingBinary] = useState(false)
  const [connectionMode, setConnectionMode] = useState<ConnectionMode>('local')
  const [, setSavedConnectionMode] = useState<ConnectionMode>('local')
  const [wsHost, setWsHost] = useState(DEFAULT_WS_HOST)
  const [wsPort, setWsPort] = useState(String(DEFAULT_WS_PORT))
  const [remoteUrl, setRemoteUrl] = useState('')
  const [remoteToken, setRemoteToken] = useState('')
  const [activeRemoteStack, setActiveRemoteStack] = useState<ActiveRemoteStackRef | null>(null)
  const [locale, setLocale] = useState<AppLocale>(normalizeLocale(undefined))
  const [taskCompletionNotificationMode, setTaskCompletionNotificationMode] =
    useState<TaskCompletionNotificationMode>('whenUnfocused')
  const [showInMenuBar, setShowInMenuBar] = useState(isMac)
  const [version, setVersion] = useState('')
  const [saving, setSaving] = useState(false)
  const [restartingAppServer, setRestartingAppServer] = useState(false)
  const [browserUseApprovalMode, setBrowserUseApprovalMode] = useState<BrowserUseApprovalMode>('alwaysAsk')
  const [browserUseBlockedDomains, setBrowserUseBlockedDomains] = useState<string[]>([])
  const [browserUseAllowedDomains, setBrowserUseAllowedDomains] = useState<string[]>([])
  const [browserUseDomainDraft, setBrowserUseDomainDraft] = useState('')
  const [browserUseDomainTarget, setBrowserUseDomainTarget] = useState<'blocked' | 'allowed' | null>(null)
  const [browserUseDomainError, setBrowserUseDomainError] = useState('')
  const [clearingBrowserCookies, setClearingBrowserCookies] = useState(false)
  const [browserUseInstallOpen, setBrowserUseInstallOpen] = useState(false)
  const [browserUseInstalling, setBrowserUseInstalling] = useState(false)
  const [chromeInstallOpen, setChromeInstallOpen] = useState(false)
  const [chromeInstalling, setChromeInstalling] = useState(false)
  const [chromeDetailOpen, setChromeDetailOpen] = useState(false)
  const [chromeSetupStatus, setChromeSetupStatus] = useState<ChromeSetupStatus | null>(null)
  const [chromeSetupLoading, setChromeSetupLoading] = useState(false)
  const [, setChromeSetupError] = useState('')
  const [chromeNativeHostInstalling, setChromeNativeHostInstalling] = useState(false)
  const [chromeOpening, setChromeOpening] = useState(false)
  const [chromeToggling, setChromeToggling] = useState(false)
  const [baselineConnection, setBaselineConnection] = useState<{
    binarySource: BinarySource
    binaryPath: string
    connectionMode: ConnectionMode
    wsHost: string
    wsPort: string
    remoteUrl: string
    remoteToken: string
  } | null>(null)
  const [, setWorkspaceCoreBaseline] = useState<WorkspaceCoreConfig>({
    providerId: null,
    providerPreferences: {},
    welcomeSuggestionsEnabled: null,
    skillsSelfLearningEnabled: null,
    memoryAutoConsolidateEnabled: null,
    dreamsEnabled: null,
    dreamsInterval: null,
    dreamsThreadLookbackCount: null,
    dreamsAutoApply: null,
    defaultApprovalPolicy: null
  })
  const [userDefaultCore, setUserDefaultCore] = useState<WorkspaceCoreConfig>({
    providerId: null,
    providerPreferences: {},
    welcomeSuggestionsEnabled: null,
    skillsSelfLearningEnabled: null,
    memoryAutoConsolidateEnabled: null,
    dreamsEnabled: null,
    dreamsInterval: null,
    dreamsThreadLookbackCount: null,
    dreamsAutoApply: null,
    defaultApprovalPolicy: null
  })
  const [providers, setProviders] = useState<ProviderInfoWire[]>([])
  const [providersLoading, setProvidersLoading] = useState(false)
  const [providerDraft, setProviderDraft] = useState<ProviderDraft>(() => createProviderDraft())
  // Snapshot of the user's typed id/displayName before they switch to ChatGPT-OAuth, so we can
  // restore those values if they toggle the auth method back to API key without saving.
  const preChatGptDraftRef = useRef<{ id: string; displayName: string } | null>(null)
  const [providerEditorId, setProviderEditorId] = useState<ProviderEditorId>(null)
  const [selectedProviderId, setSelectedProviderId] = useState('')
  const selectedProviderIdRef = useRef('')
  const [workspacePreference, setWorkspacePreference] = useState<ModelPreference>(
    () => createManualModelPreference('')
  )
  const [providerPreferences, setProviderPreferences] = useState<ProviderPreferences>({})
  const [providerTestResult, setProviderTestResult] = useState<ProviderTestResultWire | null>(null)
  const [testingProvider, setTestingProvider] = useState(false)
  const [savingProvider, setSavingProvider] = useState(false)
  const [deletingProvider, setDeletingProvider] = useState(false)
  const [providerModelCatalog, setProviderModelCatalog] = useState<ModelCatalogItem[]>([])
  const [providerModelLoading, setProviderModelLoading] = useState(false)
  const [providerModelError, setProviderModelError] = useState('')
  const providerModelRequestSeqRef = useRef(0)
  const [workspaceManualModelDraft, setWorkspaceManualModelDraft] = useState('')
  const [applyingWorkspaceProvider, setApplyingWorkspaceProvider] = useState(false)
  const [applyingWorkspaceModel, setApplyingWorkspaceModel] = useState(false)
  const [subAgentPreference, setSubAgentPreference] = useState<ModelPreference | null>(null)
  const [subAgentManualModelDraft, setSubAgentManualModelDraft] = useState('')
  const [subAgentProviderPreferences, setSubAgentProviderPreferences] = useState<ProviderPreferences>({})
  const subAgentProviderPreferencesRef = useRef<ProviderPreferences>({})
  const [applyingSubAgentModel, setApplyingSubAgentModel] = useState(false)
  const [welcomeSuggestionsEnabled, setWelcomeSuggestionsEnabled] = useState(true)
  const [applyingWelcomeSuggestions, setApplyingWelcomeSuggestions] = useState(false)
  const [selfLearningEnabled, setSelfLearningEnabled] = useState(true)
  const [applyingSelfLearning, setApplyingSelfLearning] = useState(false)
  const [selfLearningRestartPending, setSelfLearningRestartPending] = useState(false)
  const [memoryAutoConsolidateEnabled, setMemoryAutoConsolidateEnabled] = useState(true)
  const [applyingMemoryAutoConsolidate, setApplyingMemoryAutoConsolidate] = useState(false)
  const [resettingMemory, setResettingMemory] = useState(false)
  const [dreamsEnabled, setDreamsEnabled] = useState(true)
  const [dreamsInterval, setDreamsInterval] = useState(DEFAULT_DREAMS_INTERVAL)
  const [dreamsThreadLookbackCount, setDreamsThreadLookbackCount] = useState(DEFAULT_DREAMS_THREAD_LOOKBACK_COUNT)
  const [dreamsAutoApply, setDreamsAutoApply] = useState(false)
  const [dreamsStatus, setDreamsStatus] = useState<DreamsStatus | null>(null)
  const [dreamRuns, setDreamRuns] = useState<DreamsRunState[]>([])
  const [dreamRunsLoading, setDreamRunsLoading] = useState(false)
  const [archivingDreamRunId, setArchivingDreamRunId] = useState<string | null>(null)
  const [archivingAllDreamRuns, setArchivingAllDreamRuns] = useState(false)
  const [applyingDreams, setApplyingDreams] = useState(false)
  const [runningDreams, setRunningDreams] = useState(false)
  const [defaultApprovalPolicy, setDefaultApprovalPolicy] = useState<VisibleApprovalPolicy>('default')
  const [applyingDefaultApprovalPolicy, setApplyingDefaultApprovalPolicy] = useState(false)
  const inputRef = useRef<HTMLInputElement>(null)
  const settingsCloseRequestSeqRef = useRef(settingsCloseRequestSeq)
  const workspaceCoreApiAvailable = getWorkspaceCoreReader(window.api) != null

  const [mcpServers, setMcpServers] = useState<McpServerConfigWire[]>([])
  const [mcpLoading, setMcpLoading] = useState(false)
  const [mcpError, setMcpError] = useState<string | null>(null)
  const [editingServerName, setEditingServerName] = useState<string | null>(null)
  const [mcpDraft, setMcpDraft] = useState<McpServerConfigWire>(createEmptyMcpServer())
  const [argRows, setArgRows] = useState<ValueRow[]>(normalizeValueRows([]))
  const [envRows, setEnvRows] = useState<KeyValueRow[]>(normalizeKeyValueRows({}))
  const [envVarRows, setEnvVarRows] = useState<ValueRow[]>(normalizeValueRows([]))
  const [httpHeaderRows, setHttpHeaderRows] = useState<KeyValueRow[]>(normalizeKeyValueRows({}))
  const [envHttpHeaderRows, setEnvHttpHeaderRows] = useState<KeyValueRow[]>(normalizeKeyValueRows({}))
  const [testingMcp, setTestingMcp] = useState(false)
  const [savingMcp, setSavingMcp] = useState(false)
  const [deletingMcp, setDeletingMcp] = useState(false)
  const [togglingServerName, setTogglingServerName] = useState<string | null>(null)
  const [authenticatingMcpName, setAuthenticatingMcpName] = useState<string | null>(null)
  const [mcpTestResult, setMcpTestResult] = useState<McpTestResultWire | null>(null)
  const [mcpSavedHint, setMcpSavedHint] = useState('')
  const [subAgentRefreshTick, setSubAgentRefreshTick] = useState(0)

  const mcpEnabled = capabilities?.mcpManagement === true
  const mcpOriginsEnabled = capabilities?.mcpServerOrigins === true
  const subAgentEnabled = capabilities?.subAgentManagement === true
  const hooksEnabled = capabilities?.hooksManagement === true
  const sourceControlEnabled = capabilities?.sourceControlManagement === true
  const pluginManagementEnabled = capabilities?.pluginManagement === true
  const providerManagementEnabled = capabilities?.providerManagement === true
  const modelCatalogManagementEnabled = capabilities?.modelCatalogManagement === true
  const memoryManagementEnabled = capabilities?.memoryManagement === true
  const dreamsCapabilityEnabled = capabilities?.dreams === true
  const personalizationAvailable = workspaceCoreApiAvailable || memoryManagementEnabled || dreamsCapabilityEnabled
  const browserUsePlugin = plugins.find((plugin) => plugin.id === 'browser') ?? null
  const chromePlugin = plugins.find((plugin) => plugin.id === 'chrome') ?? null
  const browserUsePluginReady = !pluginManagementEnabled || browserUsePlugin?.installed === true
  const chromeSetup = chromeSetupSummary(chromeSetupStatus, t)
  const selectedProvider = providers.find((provider) => provider.id === selectedProviderId) ?? null
  const providerEditorProvider =
    providerEditorId != null && providerEditorId !== '__new__'
      ? providers.find((provider) => provider.id === providerEditorId) ?? null
      : null
  const providerEditorIsNew = providerEditorId === '__new__'
  const canDeleteProviderInEditor =
    providerEditorProvider != null &&
    providerEditorProvider.isImplicit !== true &&
    providerEditorProvider.id !== selectedProviderId
  const providersCountLabel = providers.length === 1
    ? t('settings.llm.providersCount.one', { count: providers.length })
    : t('settings.llm.providersCount.other', { count: providers.length })
  const selectedProviderMissing =
    selectedProviderId.trim() !== '' &&
    !providersLoading &&
    providers.length > 0 &&
    selectedProvider == null
  const workspaceProviderMissingMessage = selectedProviderMissing
    ? t('settings.llm.workspaceProviderMissing', { providerId: selectedProviderId })
    : ''
  const llmDirty = false
  const activeRemoteStackConnection = connectionMode === 'remote' && activeRemoteStack != null
  const manualRemoteConnection = connectionMode === 'remote' && !activeRemoteStackConnection
  const localConnectionSettingsEnabled = connectionMode !== 'remote'
  const connectionDirty =
    baselineConnection != null &&
    (connectionMode !== baselineConnection.connectionMode ||
      (localConnectionSettingsEnabled &&
        (binarySource !== baselineConnection.binarySource ||
          binaryPath.trim() !== baselineConnection.binaryPath.trim() ||
          wsHost.trim() !== baselineConnection.wsHost.trim() ||
          wsPort.trim() !== baselineConnection.wsPort.trim())) ||
      (manualRemoteConnection &&
        (remoteUrl.trim() !== baselineConnection.remoteUrl.trim() ||
          remoteToken.trim() !== baselineConnection.remoteToken.trim())))
  const remoteConnectionValidation = useMemo(
    () => manualRemoteConnection
      ? resolveRemoteWebSocketConfig({ url: remoteUrl, token: remoteToken })
      : null,
    [manualRemoteConnection, remoteToken, remoteUrl]
  )
  function applyWorkspaceCoreBaseline(core: WorkspaceCoreConfigResult, keepDraftValues: boolean): void {
    setWorkspaceCoreBaseline(core.workspace)
    setUserDefaultCore(core.userDefaults)
    if (!keepDraftValues) {
      const resolvedProviderId = core.workspace.providerId ?? core.userDefaults.providerId ?? 'openai'
      selectedProviderIdRef.current = resolvedProviderId
      setSelectedProviderId(resolvedProviderId)
      const resolvedPreference = resolveEffectiveProviderPreference(
        core.workspace.providerPreferences,
        core.userDefaults.providerPreferences,
        resolvedProviderId
      ) ?? createManualModelPreference('')
      setWorkspacePreference(resolvedPreference)
      setWorkspaceManualModelDraft(resolvedPreference.model)
      setProviderPreferences({ ...core.workspace.providerPreferences })
    }

    const resolvedWelcomeSuggestionsEnabled =
      core.workspace.welcomeSuggestionsEnabled ??
      core.userDefaults.welcomeSuggestionsEnabled ??
      true
    setWelcomeSuggestionsEnabled(resolvedWelcomeSuggestionsEnabled)
    const resolvedSelfLearningEnabled =
      core.workspace.skillsSelfLearningEnabled ??
      core.userDefaults.skillsSelfLearningEnabled ??
      true
    setSelfLearningEnabled(resolvedSelfLearningEnabled)
    const resolvedMemoryAutoConsolidateEnabled =
      core.workspace.memoryAutoConsolidateEnabled ??
      core.userDefaults.memoryAutoConsolidateEnabled ??
      true
    setMemoryAutoConsolidateEnabled(resolvedMemoryAutoConsolidateEnabled)
    const resolvedDreamsEnabled =
      core.workspace.dreamsEnabled ??
      core.userDefaults.dreamsEnabled ??
      true
    setDreamsEnabled(resolvedDreamsEnabled)
    const resolvedDreamsInterval =
      core.workspace.dreamsInterval ??
      core.userDefaults.dreamsInterval ??
      DEFAULT_DREAMS_INTERVAL
    setDreamsInterval(resolvedDreamsInterval)
    const resolvedDreamsThreadLookbackCount =
      core.workspace.dreamsThreadLookbackCount ??
      core.userDefaults.dreamsThreadLookbackCount ??
      DEFAULT_DREAMS_THREAD_LOOKBACK_COUNT
    setDreamsThreadLookbackCount(resolvedDreamsThreadLookbackCount)
    const resolvedDreamsAutoApply =
      core.workspace.dreamsAutoApply ??
      core.userDefaults.dreamsAutoApply ??
      false
    setDreamsAutoApply(resolvedDreamsAutoApply)
    const resolvedDefaultApprovalPolicy =
      core.workspace.defaultApprovalPolicy ??
      core.userDefaults.defaultApprovalPolicy ??
      'default'
    setDefaultApprovalPolicy(resolvedDefaultApprovalPolicy)

    if (keepDraftValues) {
      return
    }

  }

  async function readWorkspaceCoreSafe(): Promise<WorkspaceCoreConfigResult> {
    return readWorkspaceCoreSafeFromApi(window.api)
  }

  async function readWorkspaceCoreStrict(): Promise<WorkspaceCoreConfigResult> {
    return readWorkspaceCoreStrictFromApi(window.api)
  }

  async function reloadWorkspaceCore(): Promise<void> {
    const core = await readWorkspaceCoreSafe()
    applyWorkspaceCoreBaseline(core, false)
  }

  async function reloadProviders(): Promise<void> {
    if (!providerManagementEnabled) {
      setProviders([])
      return
    }
    setProvidersLoading(true)
    try {
      const result = await window.api.appServer.sendRequest('provider/list', {}, 20_000)
      setProviders(normalizeProviderList(result))
    } catch (err) {
      addToast(t('settings.llm.toast.loadProvidersFailed', {
        error: err instanceof Error ? err.message : String(err)
      }), 'error')
    } finally {
      setProvidersLoading(false)
    }
  }

  async function reloadSubAgentModelMemory(): Promise<void> {
    if (!providerManagementEnabled || !subAgentEnabled) {
      setSubAgentPreference(null)
      setSubAgentManualModelDraft('')
      subAgentProviderPreferencesRef.current = {}
      setSubAgentProviderPreferences({})
      return
    }
    try {
      const result = (await window.api.appServer.sendRequest(
        'subagent/profiles/list',
        {}
      )) as { settings?: { providerPreferences?: ProviderPreferences | null } }
      const nextProviderPreferences = readProviderPreferences(result.settings?.providerPreferences)
      const activeProviderId = selectedProviderIdRef.current.trim()
      const preference = activeProviderId
        ? findProviderPreference(nextProviderPreferences, activeProviderId)
        : null
      setSubAgentPreference(preference)
      setSubAgentManualModelDraft(preference?.model ?? '')
      subAgentProviderPreferencesRef.current = nextProviderPreferences
      setSubAgentProviderPreferences(nextProviderPreferences)
    } catch {
      // Non-fatal: the native model editor simply falls back to inherit.
    }
  }

  async function reloadProviderPreferences(providerId: string): Promise<void> {
    const requestSeq = ++providerModelRequestSeqRef.current
    if (!modelCatalogManagementEnabled) {
      setProviderModelCatalog([])
      setProviderModelLoading(false)
      return
    }
    const normalizedProviderId = providerId.trim()
    if (
      providerManagementEnabled &&
      !providersLoading &&
      providers.length > 0 &&
      normalizedProviderId &&
      !providers.some((provider) => provider.id === normalizedProviderId)
    ) {
      setProviderModelCatalog([])
      setProviderModelError(t('settings.llm.workspaceProviderMissing', { providerId: normalizedProviderId }))
      return
    }
    setProviderModelLoading(true)
    setProviderModelError('')
    try {
      const result = await window.api.appServer.sendRequest(
        'model/list',
        normalizedProviderId ? { providerId: normalizedProviderId } : {},
        20_000
      ) as {
        success?: boolean
        models?: Array<{ id?: string; Id?: string }>
        errorMessage?: string
        ErrorMessage?: string
      }
      if (requestSeq !== providerModelRequestSeqRef.current) return
      if (result.success === false) {
        setProviderModelCatalog([])
        setProviderModelError(
          result.errorMessage ??
          result.ErrorMessage ??
          t('settings.llm.modelListUnavailable')
        )
        return
      }
      const models = parseModelCatalogItems(result)
      setProviderModelCatalog(models)
    } catch (err) {
      if (requestSeq !== providerModelRequestSeqRef.current) return
      setProviderModelCatalog([])
      setProviderModelError(err instanceof Error ? err.message : String(err))
    } finally {
      if (requestSeq === providerModelRequestSeqRef.current) {
        setProviderModelLoading(false)
      }
    }
  }

  async function fetchWorkspaceProviderModelOptions(providerId: string): Promise<ModelCatalogItem[] | null> {
    if (!modelCatalogManagementEnabled) return null

    try {
      const result = await window.api.appServer.sendRequest(
        'model/list',
        providerId.trim() ? { providerId: providerId.trim() } : {},
        20_000
      ) as {
        success?: boolean
        models?: Array<{ id?: string; Id?: string }>
      }
      if (result.success === false) return null
      return parseModelCatalogItems(result)
    } catch {
      return null
    }
  }

  useEffect(() => {
    if (!providerManagementEnabled) return
    void reloadProviders()
  }, [providerManagementEnabled])

  useEffect(() => {
    if (!providerManagementEnabled || !subAgentEnabled) return
    void reloadSubAgentModelMemory()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [providerManagementEnabled, subAgentEnabled, subAgentRefreshTick])

  useEffect(() => {
    if (!subAgentEnabled) return
    const preference = selectedProviderId
      ? findProviderPreference(subAgentProviderPreferences, selectedProviderId)
      : null
    setSubAgentPreference(preference)
    setSubAgentManualModelDraft(preference?.model ?? '')
  }, [selectedProviderId, subAgentEnabled, subAgentProviderPreferences])

  useEffect(() => {
    if (!providerManagementEnabled || workspaceConfigChangeSeq === 0) return
    if (workspaceConfigChange?.regions.includes('providers')) {
      void reloadProviders()
    }
  }, [providerManagementEnabled, workspaceConfigChange, workspaceConfigChangeSeq])

  useEffect(() => {
    if (!selectedProviderId) return
    void reloadProviderPreferences(selectedProviderId)
  }, [modelCatalogManagementEnabled, selectedProviderId])

  useEffect(() => {
    if (
      providerModelLoading
      || applyingWorkspaceModel
      || !selectedProviderId.trim()
      || workspacePreference.model.trim()
      || providerModelCatalog.length === 0
    ) {
      return
    }

    const nextPreference = createCatalogDefaultPreference(
      providerModelCatalog[0],
      providerModelCatalog[0].id
    )
    const previousPreference = cloneModelPreference(workspacePreference)
    setWorkspacePreference(nextPreference)
    setWorkspaceManualModelDraft(nextPreference.model)
    void persistWorkspacePreference(nextPreference, previousPreference)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [
    applyingWorkspaceModel,
    providerModelCatalog,
    providerModelLoading,
    selectedProviderId,
    workspacePreference.model
  ])

  useEffect(() => {
    if (!selectedProviderMissing) return
    setProviderModelCatalog([])
    setProviderModelError(workspaceProviderMissingMessage)
  }, [selectedProviderMissing, workspaceProviderMissingMessage])

  function startCreateProvider(): void {
    setProviderEditorId('__new__')
    setProviderDraft(createProviderDraft())
    setProviderTestResult(null)
  }

  function startEditProvider(provider: ProviderInfoWire): void {
    setProviderEditorId(provider.id)
    setProviderDraft(providerDraftFromInfo(provider))
    setProviderTestResult(null)
  }

  function closeProviderEditor(): void {
    setProviderEditorId(null)
    setProviderTestResult(null)
  }

  async function handleWorkspaceProviderChange(providerId: string): Promise<void> {
    const normalized = providerId.trim()
    if (!normalized || normalized === selectedProviderId || applyingWorkspaceProvider) return

    const previousProviderId = selectedProviderId
    const previousPreference = cloneModelPreference(workspacePreference)
    setApplyingWorkspaceProvider(true)
    setProviderModelCatalog([])
    setProviderModelError('')
    try {
      const listedModels = await fetchWorkspaceProviderModelOptions(normalized)
      const rememberedPreference = resolveEffectiveProviderPreference(
        providerPreferences,
        userDefaultCore.providerPreferences,
        normalized
      )
      const listedRemembered = rememberedPreference == null
        ? undefined
        : listedModels?.find((model) => model.id === rememberedPreference.model)
      const nextPreference = rememberedPreference
        ? normalizePreferenceForModel(rememberedPreference, listedModels ?? [])
        : listedModels && listedModels.length > 0
          ? createCatalogDefaultPreference(listedModels[0], listedModels[0].id)
          : createManualModelPreference('')
      if (listedModels && listedModels.length > 0 && rememberedPreference && !listedRemembered) {
        Object.assign(nextPreference, createCatalogDefaultPreference(listedModels[0], listedModels[0].id))
      }

      const nextProviderPreferences = nextPreference.model
        ? setProviderPreference(providerPreferences, normalized, nextPreference)
        : providerPreferences

      const updatePayload: { providerId: string; providerPreferences: ProviderPreferences } = {
        providerId: normalized,
        providerPreferences: nextProviderPreferences
      }

      await window.api.appServer.sendRequest('workspace/config/update', {
        ...updatePayload,
        providerPreferences: toContractProviderPreferences(updatePayload.providerPreferences)
      }, 20_000)
      selectedProviderIdRef.current = normalized
      setSelectedProviderId(normalized)
      if (listedModels != null) {
        setProviderModelCatalog(listedModels)
        setProviderModelError('')
      }
      setProviderPreferences(nextProviderPreferences)
      setWorkspaceCoreBaseline((current) => ({
        ...current,
        providerId: normalized,
        providerPreferences: nextProviderPreferences
      }))
      setWorkspacePreference(nextPreference)
      setWorkspaceManualModelDraft(nextPreference.model)

      if (subAgentEnabled) {
        const nextSubPreference = findProviderPreference(
          subAgentProviderPreferencesRef.current,
          normalized
        )
        setSubAgentPreference(nextSubPreference)
        setSubAgentManualModelDraft(nextSubPreference?.model ?? '')
      }
    } catch (err) {
      selectedProviderIdRef.current = previousProviderId
      setSelectedProviderId(previousProviderId)
      setWorkspacePreference(previousPreference)
      setWorkspaceManualModelDraft(previousPreference.model)
      addToast(t('settings.llm.toast.saveProviderSelectionFailed', {
        error: err instanceof Error ? err.message : String(err)
      }), 'error')
    } finally {
      setApplyingWorkspaceProvider(false)
    }
  }

  async function persistWorkspacePreference(
    nextPreference: ModelPreference,
    previousPreference: ModelPreference
  ): Promise<void> {
    setApplyingWorkspaceModel(true)
    try {
      const activeProviderId = selectedProviderId.trim()
      if (!activeProviderId || !nextPreference.model.trim()) return
      const normalized = cloneModelPreference(nextPreference)
      normalized.model = normalized.model.trim()
      const nextProviderPreferences = setProviderPreference(
        providerPreferences,
        activeProviderId,
        normalized
      )
      await window.api.appServer.sendRequest('workspace/config/update', {
        providerPreferences: toContractProviderPreferences(nextProviderPreferences)
      }, 20_000)
      setProviderPreferences(nextProviderPreferences)
      setWorkspaceCoreBaseline((current) => ({
        ...current,
        providerPreferences: nextProviderPreferences
      }))
      setWorkspacePreference(normalized)
      setWorkspaceManualModelDraft(normalized.model)
    } catch (err) {
      setWorkspacePreference(previousPreference)
      setWorkspaceManualModelDraft(previousPreference.model)
      addToast(t('settings.llm.toast.saveModelFailed', {
        error: err instanceof Error ? err.message : String(err)
      }), 'error')
    } finally {
      setApplyingWorkspaceModel(false)
    }
  }

  async function handleWorkspacePreferenceChange(nextPreference: ModelPreference): Promise<void> {
    if (applyingWorkspaceModel) return
    const previousPreference = cloneModelPreference(workspacePreference)
    const normalized = normalizePreferenceForModel(nextPreference, providerModelCatalog)
    setWorkspacePreference(normalized)
    setWorkspaceManualModelDraft(normalized.model)
    await persistWorkspacePreference(normalized, previousPreference)
  }

  async function persistSubAgentPreference(
    nextPreference: ModelPreference | null,
    previousPreference: ModelPreference | null
  ): Promise<void> {
    setApplyingSubAgentModel(true)
    try {
      const activeProviderId = selectedProviderIdRef.current.trim()
      const normalized = nextPreference == null ? null : cloneModelPreference(nextPreference)
      if (normalized) normalized.model = normalized.model.trim()
      const nextSubProviderPreferences = setProviderPreference(
        subAgentProviderPreferencesRef.current,
        activeProviderId,
        normalized
      )
      await window.api.appServer.sendRequest('subagent/settings/update', {
        providerPreferences: toContractProviderPreferences(nextSubProviderPreferences)
      }, 20_000)
      setSubAgentPreference(normalized)
      setSubAgentManualModelDraft(normalized?.model ?? '')
      subAgentProviderPreferencesRef.current = nextSubProviderPreferences
      setSubAgentProviderPreferences(nextSubProviderPreferences)
    } catch (err) {
      setSubAgentPreference(previousPreference)
      setSubAgentManualModelDraft(previousPreference?.model ?? '')
      addToast(t('settings.llm.toast.saveSubAgentModelFailed', {
        error: err instanceof Error ? err.message : String(err)
      }), 'error')
    } finally {
      setApplyingSubAgentModel(false)
    }
  }

  async function handleSubAgentPreferenceChange(nextPreference: ModelPreference): Promise<void> {
    if (applyingSubAgentModel) return
    const previousPreference = subAgentPreference == null
      ? null
      : cloneModelPreference(subAgentPreference)
    const normalized = normalizePreferenceForModel(nextPreference, providerModelCatalog)
    setSubAgentPreference(normalized)
    setSubAgentManualModelDraft(normalized.model)
    await persistSubAgentPreference(normalized, previousPreference)
  }

  async function handleSubAgentInheritanceChange(custom: boolean): Promise<void> {
    if (applyingSubAgentModel) return
    const previousPreference = subAgentPreference == null
      ? null
      : cloneModelPreference(subAgentPreference)
    const nextPreference = custom ? cloneModelPreference(workspacePreference) : null
    setSubAgentPreference(nextPreference)
    setSubAgentManualModelDraft(nextPreference?.model ?? '')
    await persistSubAgentPreference(nextPreference, previousPreference)
  }

  async function handleSubAgentManualModelCommit(): Promise<void> {
    const normalized = subAgentManualModelDraft.trim()
    const persistedPreference = findProviderPreference(
      subAgentProviderPreferencesRef.current,
      selectedProviderIdRef.current
    )
    if (normalized === (persistedPreference?.model ?? '') || applyingSubAgentModel) {
      setSubAgentManualModelDraft(persistedPreference?.model ?? '')
      return
    }

    if (!normalized) return
    const previousPreference = subAgentPreference == null
      ? null
      : cloneModelPreference(subAgentPreference)
    const nextPreference = {
      ...cloneModelPreference(subAgentPreference ?? workspacePreference),
      model: normalized
    }
    setSubAgentPreference(nextPreference)
    await persistSubAgentPreference(nextPreference, previousPreference)
  }

  async function handleWorkspaceManualModelCommit(): Promise<void> {
    const normalized = workspaceManualModelDraft.trim()
    const persistedPreference = findProviderPreference(providerPreferences, selectedProviderId)
    if (normalized === (persistedPreference?.model ?? '') || applyingWorkspaceModel) {
      setWorkspaceManualModelDraft(persistedPreference?.model ?? '')
      return
    }

    if (!normalized) return
    const previousPreference = persistedPreference
      ? cloneModelPreference(persistedPreference)
      : createManualModelPreference('')
    const nextPreference = { ...cloneModelPreference(workspacePreference), model: normalized }
    setWorkspacePreference(nextPreference)
    await persistWorkspacePreference(nextPreference, previousPreference)
  }

  async function handleProviderTest(): Promise<void> {
    setTestingProvider(true)
    setProviderTestResult(null)
    try {
      const editing = providerEditorProvider
      const apiKeyUnchanged = editing?.hasApiKey === true && providerDraft.apiKey === '********'
      // ChatGPT-OAuth providers can't be tested via the draft payload — the backend draft path
      // has no AuthMethod field and would fall through the resolver as an api-key provider with
      // empty key. Always route to the saved provider's id so the OAuth-bound runtime is used.
      const isChatGptOAuth = providerDraft.authMethod === 'chatgptOAuth'
      let payload: Record<string, unknown>
      if (isChatGptOAuth) {
        payload = { providerId: providerDraft.id.trim() }
      } else if (editing && apiKeyUnchanged) {
        payload = { providerId: editing.id }
      } else {
        payload = {
          protocol: providerDraft.protocol,
          apiKey: providerDraft.apiKey === '********' ? null : providerDraft.apiKey.trim(),
          endPoint: providerDraft.endPoint.trim() || null,
          networkTimeoutSeconds: providerDraft.networkTimeoutSeconds.trim()
            ? Number(providerDraft.networkTimeoutSeconds.trim())
            : null
        }
      }
      const result = await window.api.appServer.sendRequest('provider/test', payload, 25_000) as ProviderTestResultWire
      setProviderTestResult(result)
    } catch (err) {
      setProviderTestResult({
        success: false,
        protocol: providerDraft.protocol,
        models: [],
        errorMessage: err instanceof Error ? err.message : String(err)
      })
    } finally {
      setTestingProvider(false)
    }
  }

  async function handleProviderSave(): Promise<void> {
    const id = providerDraft.id.trim()
    if (!id) {
      addToast(t('settings.llm.toast.providerIdRequired'), 'warning')
      return
    }
    setSavingProvider(true)
    try {
      const timeout = providerDraft.networkTimeoutSeconds.trim()
        ? Number(providerDraft.networkTimeoutSeconds.trim())
        : null
      const supportsHostedImageGeneration = canConfigureHostedImageGeneration(providerDraft)
        ? providerDraft.supportsHostedImageGeneration
        : false
      if (providerEditorProvider != null) {
        const editing = providerEditorProvider
        const payload: Record<string, unknown> = {
          id: editing.id,
          displayName: providerDraft.displayName.trim() || id,
          protocol: providerDraft.protocol,
          endPoint: providerDraft.endPoint.trim() || null,
          networkTimeoutSeconds: timeout,
          supportsHostedImageGeneration,
          authMethod: providerDraft.authMethod
        }
        if (!(editing?.hasApiKey === true && providerDraft.apiKey === '********')) {
          payload.apiKey = providerDraft.authMethod === 'chatgptOAuth'
            ? null
            : providerDraft.apiKey.trim() || null
        }
        await window.api.appServer.sendRequest('provider/update', payload, 20_000)
        addToast(t('settings.llm.toast.providerUpdated'), 'success')
      } else {
        await window.api.appServer.sendRequest('provider/create', {
          id,
          displayName: providerDraft.displayName.trim() || id,
          protocol: providerDraft.protocol,
          apiKey: providerDraft.authMethod === 'chatgptOAuth' ? '' : providerDraft.apiKey.trim(),
          endPoint: providerDraft.endPoint.trim(),
          networkTimeoutSeconds: timeout,
          supportsHostedImageGeneration,
          authMethod: providerDraft.authMethod
        }, 20_000)
        addToast(t('settings.llm.toast.providerCreated'), 'success')
        // Auto-activate the new provider unless the workspace already has a usable selection.
        // Mirrors the backend's "only-if-empty" semantics from BindProviderToOAuth and avoids
        // the "API key must be configured" trap when the previous selection was broken.
        const previous = providers.find((p) => p.id === selectedProviderId)
        const shouldActivate = !selectedProviderId || !previous || previous.hasApiKey === false
        if (shouldActivate && selectedProviderId !== id) {
          await handleWorkspaceProviderChange(id)
        }
      }
      await reloadProviders()
      closeProviderEditor()
    } catch (err) {
      addToast(t('settings.llm.toast.saveProviderFailed', {
        error: err instanceof Error ? err.message : String(err)
      }), 'error')
    } finally {
      setSavingProvider(false)
    }
  }

  async function handleProviderDelete(provider: ProviderInfoWire): Promise<void> {
    if (provider.isImplicit || provider.id === selectedProviderId) return
    setDeletingProvider(true)
    try {
      await window.api.appServer.sendRequest('provider/delete', { id: provider.id }, 20_000)
      addToast(t('settings.llm.toast.providerDeleted'), 'success')
      await reloadProviders()
      if (providerEditorId === provider.id) closeProviderEditor()
    } catch (err) {
      addToast(t('settings.llm.toast.deleteProviderFailed', {
        error: err instanceof Error ? err.message : String(err)
      }), 'error')
    } finally {
      setDeletingProvider(false)
    }
  }

  const applyDreamsStatusSnapshot = useCallback((status: DreamsStatus): void => {
    setDreamsStatus(status)
    setDreamsEnabled(status.enabled)
    setDreamsInterval(status.interval)
    setDreamsThreadLookbackCount(status.threadLookbackCount)
    setDreamsAutoApply(status.autoApply)
  }, [])

  const reloadDreamsStatus = useCallback(async (): Promise<void> => {
    if (!dreamsCapabilityEnabled) {
      setDreamsStatus(null)
      return
    }

    try {
      const result = await window.api.appServer.sendRequest('dreams/status', {}, 20_000)
      applyDreamsStatusSnapshot(normalizeDreamsStatus(result))
    } catch (err) {
      addToast(t('settings.personalization.dreamsStatusFailed', {
        error: err instanceof Error ? err.message : String(err)
      }), 'error')
    }
  }, [applyDreamsStatusSnapshot, dreamsCapabilityEnabled, t])

  const reloadDreamRuns = useCallback(async (): Promise<void> => {
    if (!dreamsCapabilityEnabled) {
      setDreamRuns([])
      return
    }

    setDreamRunsLoading(true)
    try {
        const result = await window.api.appServer.sendRequest('dreams/list', {}, 20_000)
      const runs = normalizeDreamsRunList(result)
      setDreamRuns(runs)
    } catch (err) {
      addToast(t('settings.dreams.loadFailed', {
        error: err instanceof Error ? err.message : String(err)
      }), 'error')
    } finally {
      setDreamRunsLoading(false)
    }
  }, [dreamsCapabilityEnabled, t])

  const handleWelcomeSuggestionsToggle = useCallback(
    async (checked: boolean): Promise<void> => {
      const previous = welcomeSuggestionsEnabled
      setWelcomeSuggestionsEnabled(checked)
      setApplyingWelcomeSuggestions(true)
      try {
        const result = await window.api.appServer.sendRequest('workspace/config/update', {
          welcomeSuggestionsEnabled: checked
        }) as { welcomeSuggestionsEnabled?: boolean | null }
        const persisted = typeof result?.welcomeSuggestionsEnabled === 'boolean'
          ? result.welcomeSuggestionsEnabled
          : checked
        setWelcomeSuggestionsEnabled(persisted)
        await reloadWorkspaceCore()
      } catch (err) {
        const msg = err instanceof Error ? err.message : String(err)
        setWelcomeSuggestionsEnabled(previous)
        addToast(t('settings.personalization.welcomeSuggestionsSaveFailed', { error: msg }), 'error')
      } finally {
        setApplyingWelcomeSuggestions(false)
      }
    },
    [reloadWorkspaceCore, t, welcomeSuggestionsEnabled]
  )

  const handleSelfLearningToggle = useCallback(
    async (checked: boolean): Promise<void> => {
      const previous = selfLearningEnabled
      setSelfLearningEnabled(checked)
      setApplyingSelfLearning(true)
      try {
        const result = await window.api.appServer.sendRequest('workspace/config/update', {
          skillsSelfLearningEnabled: checked
        }) as { skillsSelfLearningEnabled?: boolean | null }
        const persisted = typeof result?.skillsSelfLearningEnabled === 'boolean'
          ? result.skillsSelfLearningEnabled
          : checked
        setSelfLearningEnabled(persisted)
        setSelfLearningRestartPending(true)
        await reloadWorkspaceCore()
      } catch (err) {
        const msg = err instanceof Error ? err.message : String(err)
        setSelfLearningEnabled(previous)
        addToast(t('settings.personalization.selfLearningSaveFailed', { error: msg }), 'error')
      } finally {
        setApplyingSelfLearning(false)
      }
    },
    [reloadWorkspaceCore, selfLearningEnabled, t]
  )

  const handleMemoryAutoConsolidateToggle = useCallback(
    async (checked: boolean): Promise<void> => {
      const previous = memoryAutoConsolidateEnabled
      setMemoryAutoConsolidateEnabled(checked)
      setApplyingMemoryAutoConsolidate(true)
      try {
        const result = await window.api.appServer.sendRequest('workspace/config/update', {
          memoryAutoConsolidateEnabled: checked
        }) as { memoryAutoConsolidateEnabled?: boolean | null }
        const persisted = typeof result?.memoryAutoConsolidateEnabled === 'boolean'
          ? result.memoryAutoConsolidateEnabled
          : checked
        setMemoryAutoConsolidateEnabled(persisted)
        await reloadWorkspaceCore()
      } catch (err) {
        const msg = err instanceof Error ? err.message : String(err)
        setMemoryAutoConsolidateEnabled(previous)
        addToast(t('settings.personalization.longTermMemorySaveFailed', { error: msg }), 'error')
      } finally {
        setApplyingMemoryAutoConsolidate(false)
      }
    },
    [memoryAutoConsolidateEnabled, reloadWorkspaceCore, t]
  )

  const handleDreamsEnabledToggle = useCallback(
    async (checked: boolean): Promise<void> => {
      const previous = dreamsEnabled
      setDreamsEnabled(checked)
      setApplyingDreams(true)
      try {
        await window.api.appServer.sendRequest('workspace/config/update', {
          dreamsEnabled: checked
        })
        await reloadDreamsStatus()
      } catch (err) {
        const msg = err instanceof Error ? err.message : String(err)
        setDreamsEnabled(previous)
        addToast(t('settings.personalization.dreamsSaveFailed', { error: msg }), 'error')
      } finally {
        setApplyingDreams(false)
      }
    },
    [dreamsEnabled, reloadDreamsStatus, t]
  )

  const handleDreamsAutoApplyToggle = useCallback(
    async (checked: boolean): Promise<void> => {
      if (checked && !dreamsAutoApply) {
        const confirmed = await confirm({
          title: t('settings.personalization.dreamsAutoApply.warningTitle'),
          message: t('settings.personalization.dreamsAutoApply.warningBody'),
          confirmLabel: t('settings.personalization.dreamsAutoApply.warningConfirm'),
          cancelLabel: t('common.cancel'),
          danger: true
        })
        if (!confirmed) return
      }

      const previous = dreamsAutoApply
      setDreamsAutoApply(checked)
      setApplyingDreams(true)
      try {
        await window.api.appServer.sendRequest('workspace/config/update', {
          dreamsAutoApply: checked
        })
        await reloadWorkspaceCore()
        await reloadDreamsStatus()
      } catch (err) {
        const msg = err instanceof Error ? err.message : String(err)
        setDreamsAutoApply(previous)
        addToast(t('settings.personalization.dreamsSaveFailed', { error: msg }), 'error')
      } finally {
        setApplyingDreams(false)
      }
    },
    [confirm, dreamsAutoApply, reloadDreamsStatus, reloadWorkspaceCore, t]
  )

  const handleDreamsIntervalChange = useCallback(
    async (nextInterval: string): Promise<void> => {
      const previous = dreamsInterval
      setDreamsInterval(nextInterval)
      setApplyingDreams(true)
      try {
        await window.api.appServer.sendRequest('workspace/config/update', {
          dreamsInterval: nextInterval
        })
        await reloadDreamsStatus()
      } catch (err) {
        const msg = err instanceof Error ? err.message : String(err)
        setDreamsInterval(previous)
        addToast(t('settings.personalization.dreamsSaveFailed', { error: msg }), 'error')
      } finally {
        setApplyingDreams(false)
      }
    },
    [dreamsInterval, reloadDreamsStatus, t]
  )

  const handleDreamsThreadLookbackChange = useCallback(
    async (nextCount: number): Promise<void> => {
      const previous = dreamsThreadLookbackCount
      setDreamsThreadLookbackCount(nextCount)
      setApplyingDreams(true)
      try {
        await window.api.appServer.sendRequest('workspace/config/update', {
          dreamsThreadLookbackCount: nextCount
        })
        await reloadDreamsStatus()
      } catch (err) {
        const msg = err instanceof Error ? err.message : String(err)
        setDreamsThreadLookbackCount(previous)
        addToast(t('settings.personalization.dreamsSaveFailed', { error: msg }), 'error')
      } finally {
        setApplyingDreams(false)
      }
    },
    [dreamsThreadLookbackCount, reloadDreamsStatus, t]
  )

  const handleRunDreamsNow = useCallback(
    async (): Promise<void> => {
      if (runningDreams) return

      setRunningDreams(true)
      try {
          const result = await window.api.appServer.sendRequest('dreams/run', {}, 20_000)
        let status = normalizeDreamsStatus(result)
        applyDreamsStatusSnapshot(status)
        for (let attempt = 0; status.running && attempt < 12; attempt++) {
          await delay(1500)
            const next = await window.api.appServer.sendRequest('dreams/status', {}, 20_000)
          status = normalizeDreamsStatus(next)
          applyDreamsStatusSnapshot(status)
        }
        if (status.lastRun?.status === 'failed') {
          addToast(t('settings.personalization.dreamsRunFailed', {
            error: status.lastRun.message ?? t('settings.personalization.dreamsStatus.failed')
          }), 'error')
        } else if (status.lastRun?.status === 'skipped') {
          addToast(t('settings.personalization.dreamsRunSkipped'), 'info')
        } else if (status.lastRun?.status === 'succeeded') {
          addToast(t('settings.personalization.dreamsRunSucceeded'), 'success')
        }
        if (activeSettingsTab === 'dreams') {
          await reloadDreamRuns()
        }
      } catch (err) {
        const msg = err instanceof Error ? err.message : String(err)
        addToast(t('settings.personalization.dreamsRunFailed', { error: msg }), 'error')
      } finally {
        setRunningDreams(false)
      }
    },
    [activeSettingsTab, applyDreamsStatusSnapshot, reloadDreamRuns, runningDreams, t]
  )

  const openDreamReview = useCallback(
    async (runId: string): Promise<void> => {
      if (!dashboardUrl) return
      const baseUrl = dashboardUrl.replace(/#.*$/, '')
      await window.api.shell.openExternal(`${baseUrl}#dreams/run/${encodeURIComponent(runId)}`)
    },
    [dashboardUrl]
  )

  const handleArchiveDreamRun = useCallback(
    async (run: DreamsRunState): Promise<void> => {
      if (run.status === 'running' || archivingDreamRunId != null || archivingAllDreamRuns) return

      const confirmed = await confirm({
        title: t('settings.dreams.archiveConfirmTitle'),
        message: t('settings.dreams.archiveConfirmMessage'),
        confirmLabel: t('settings.dreams.archive'),
        cancelLabel: t('common.cancel')
      })
      if (!confirmed) return

      setArchivingDreamRunId(run.id)
      try {
        await window.api.appServer.sendRequest('dreams/archive', { runId: run.id }, 20_000)
        addToast(t('settings.dreams.archiveSucceeded'), 'success')
        await reloadDreamRuns()
      } catch (err) {
        addToast(t('settings.dreams.actionFailed', {
          error: err instanceof Error ? err.message : String(err)
        }), 'error')
      } finally {
        setArchivingDreamRunId(null)
      }
    },
    [archivingAllDreamRuns, archivingDreamRunId, confirm, reloadDreamRuns, t]
  )

  const handleArchiveAllDreamRuns = useCallback(
    async (): Promise<void> => {
      if (
        dreamRuns.length === 0 ||
        dreamRunsLoading ||
        archivingDreamRunId != null ||
        archivingAllDreamRuns ||
        dreamRuns.some((run) => run.status === 'running')
      ) {
        return
      }

      const confirmed = await confirm({
        title: t('settings.dreams.archiveAllConfirmTitle'),
        message: t('settings.dreams.archiveAllConfirmMessage', { count: dreamRuns.length }),
        confirmLabel: t('settings.dreams.archiveAll'),
        cancelLabel: t('common.cancel')
      })
      if (!confirmed) return

      setArchivingAllDreamRuns(true)
      let archivedCount = 0
      let firstError = ''
      try {
        for (const run of dreamRuns) {
          try {
            await window.api.appServer.sendRequest('dreams/archive', { runId: run.id }, 20_000)
            archivedCount += 1
          } catch (err) {
            if (!firstError) {
              firstError = err instanceof Error ? err.message : String(err)
            }
          }
        }

        if (archivedCount === dreamRuns.length) {
          addToast(t('settings.dreams.archiveAllSucceeded', { count: archivedCount }), 'success')
        } else if (archivedCount > 0) {
          addToast(t('settings.dreams.archiveAllPartial', {
            archived: archivedCount,
            total: dreamRuns.length
          }), 'warning')
        } else {
          addToast(t('settings.dreams.actionFailed', { error: firstError }), 'error')
        }
        await reloadDreamRuns()
      } finally {
        setArchivingAllDreamRuns(false)
      }
    },
    [archivingAllDreamRuns, archivingDreamRunId, confirm, dreamRuns, dreamRunsLoading, reloadDreamRuns, t]
  )

  const handleResetMemory = useCallback(
    async (): Promise<void> => {
      if (resettingMemory) return

      const confirmed = await confirm({
        title: t('settings.personalization.resetMemoryConfirmTitle'),
        message: t('settings.personalization.resetMemoryConfirmMessage'),
        confirmLabel: t('settings.personalization.resetMemoryButton'),
        cancelLabel: t('common.cancel'),
        danger: true
      })
      if (!confirmed) return

      setResettingMemory(true)
      try {
          await window.api.appServer.sendRequest('memory/reset', {}, 20_000)
        addToast(t('settings.personalization.resetMemorySuccess'), 'success')
      } catch (err) {
        const msg = err instanceof Error ? err.message : String(err)
        addToast(t('settings.personalization.resetMemoryFailed', { error: msg }), 'error')
      } finally {
        setResettingMemory(false)
      }
    },
    [confirm, resettingMemory, t]
  )

  const handleShowThinkingContentToggle = useCallback(
    async (checked: boolean): Promise<void> => {
      const previous = showThinkingContent
      setShowThinkingContent(checked)
      try {
        await window.api.settings.set({ showThinkingContent: checked })
      } catch (err) {
        setShowThinkingContent(previous)
        addToast(
          t('settings.saveFailed', {
            error: err instanceof Error ? err.message : String(err)
          }),
          'error'
        )
      }
    },
    [setShowThinkingContent, showThinkingContent, t]
  )

  const handleShowInMenuBarToggle = useCallback(
    async (checked: boolean): Promise<void> => {
      const previous = showInMenuBar
      setShowInMenuBar(checked)
      try {
        await window.api.settings.set({ showInMenuBar: checked })
      } catch (err) {
        setShowInMenuBar(previous)
        addToast(
          t('settings.saveFailed', {
            error: err instanceof Error ? err.message : String(err)
          }),
          'error'
        )
      }
    },
    [showInMenuBar, t]
  )

  const handleDefaultApprovalPolicyChange = useCallback(
    async (nextPolicy: VisibleApprovalPolicy): Promise<boolean> => {
      if (nextPolicy === defaultApprovalPolicy || applyingDefaultApprovalPolicy) return false

      if (nextPolicy === 'autoApprove') {
        const confirmed = await confirm({
          title: t('settings.permissions.fullAccess.warningTitle'),
          message: t('settings.permissions.fullAccess.warningBody'),
          confirmLabel: t('settings.permissions.fullAccess.warningConfirm'),
          cancelLabel: t('common.cancel'),
          danger: true
        })
        if (!confirmed) return false
      }

      const previous = defaultApprovalPolicy
      setDefaultApprovalPolicy(nextPolicy)
      setApplyingDefaultApprovalPolicy(true)
      try {
        const result = await window.api.appServer.sendRequest('workspace/config/update', {
          defaultApprovalPolicy: nextPolicy
        }) as { defaultApprovalPolicy?: string | null }
        const persisted = normalizeVisibleApprovalPolicy(result?.defaultApprovalPolicy) ?? nextPolicy
        setDefaultApprovalPolicy(persisted)
        await reloadWorkspaceCore()
        return true
      } catch (err) {
        const msg = err instanceof Error ? err.message : String(err)
        setDefaultApprovalPolicy(previous)
        addToast(t('settings.permissions.saveFailed', { error: msg }), 'error')
        return false
      } finally {
        setApplyingDefaultApprovalPolicy(false)
      }
    },
    [applyingDefaultApprovalPolicy, confirm, defaultApprovalPolicy, reloadWorkspaceCore, t]
  )

  useSettingsWorkspaceConfigChangeEffects({
    change: workspaceConfigChange,
    changeSeq: workspaceConfigChangeSeq,
    llmDirty,
    mcpEnabled,
    subAgentEnabled,
    onExternalLlmChangeNotice: () => {
      addToast(t('settings.llm.externalChangeNotice'), 'info')
    },
    reloadWorkspaceCore,
    reloadDreamsStatus,
    reloadMcpData: async () => {
      await Promise.all([reloadMcpServers(), reloadMcpStatuses()])
    },
    reloadSubAgentData: () => {
      setSubAgentRefreshTick((current) => current + 1)
    }
  })

  useEffect(() => {
    const unavailable = (activeSettingsTab === 'mcp' && !mcpEnabled)
      || (activeSettingsTab === 'subAgents' && !subAgentEnabled)
      || (activeSettingsTab === 'sourceControl' && !sourceControlEnabled)
      || (activeSettingsTab === 'hooks' && !hooksEnabled)
    if (!unavailable) return
    runWithoutAppNavigationRecording(() => setActiveSettingsTab('general'))
    replaceCurrentAppNavigationLocation()
  }, [activeSettingsTab, hooksEnabled, mcpEnabled, subAgentEnabled, sourceControlEnabled])

  useEffect(() => {
    if ((activeSettingsTab === 'browserUse' || activeSettingsTab === 'computerControl') && pluginManagementEnabled) {
      void fetchPlugins()
    }
  }, [activeSettingsTab, fetchPlugins, pluginManagementEnabled])

  useEffect(() => {
    if (openChromeSettingsSeq <= 0) return
    setActiveSettingsTab('computerControl')
    setChromeDetailOpen(true)
  }, [openChromeSettingsSeq])

  useEffect(() => {
    if (activeSettingsTab !== 'computerControl' || !chromeDetailOpen || chromePlugin?.installed !== true) {
      return
    }
    void reloadChromeSetupStatus()
  }, [activeSettingsTab, chromeDetailOpen, chromePlugin?.installed])

  useEffect(() => {
    inputRef.current?.focus()
    window.api.settings
      .get()
      .then(async (s) => {
        const loadedMode = s.connectionMode === 'remote' ? 'remote' : 'local'
        setBinarySource((s.binarySource ?? 'bundled') as BinarySource)
        setBinaryPath(s.appServerBinaryPath ?? '')
        setConnectionMode(loadedMode)
        setSavedConnectionMode(loadedMode)
        setWsHost(s.webSocket?.host ?? DEFAULT_WS_HOST)
        setWsPort(String(s.webSocket?.port ?? DEFAULT_WS_PORT))
        setRemoteUrl(s.remote?.url ?? '')
        setRemoteToken(s.remote?.token ?? '')
        setActiveRemoteStack(
          s.activeRemoteStack?.hostId && s.activeRemoteStack.stackId
            ? { hostId: s.activeRemoteStack.hostId, stackId: s.activeRemoteStack.stackId }
            : null
        )
        setLocale(normalizeLocale(s.locale))
        setTaskCompletionNotificationMode(
          s.notifications?.taskCompletionMode === 'always' || s.notifications?.taskCompletionMode === 'never'
            ? s.notifications.taskCompletionMode
            : 'whenUnfocused'
        )
        setShowInMenuBar(isMac ? s.showInMenuBar !== false : false)
        setShowThinkingContent(s.showThinkingContent === true)
        setBrowserUseApprovalMode((s.browserUse?.approvalMode ?? 'alwaysAsk') as BrowserUseApprovalMode)
        setBrowserUseBlockedDomains([...(s.browserUse?.blockedDomains ?? [])])
        setBrowserUseAllowedDomains([...(s.browserUse?.allowedDomains ?? [])])
        setBaselineConnection({
          binarySource: (s.binarySource ?? 'bundled') as BinarySource,
          binaryPath: s.appServerBinaryPath ?? '',
          connectionMode: loadedMode,
          wsHost: s.webSocket?.host ?? DEFAULT_WS_HOST,
          wsPort: String(s.webSocket?.port ?? DEFAULT_WS_PORT),
          remoteUrl: s.remote?.url ?? '',
          remoteToken: s.remote?.token ?? ''
        })
      })
      .catch(() => {})
    setVersion(typeof __APP_VERSION__ !== 'undefined' ? __APP_VERSION__ : '0.1.0')
    readWorkspaceCoreSafe()
      .then((core) => {
        applyWorkspaceCoreBaseline(core, false)
      })
    // `readWorkspaceCoreSafe` already normalizes missing bridge / failed reads.
  }, [isMac])

  useEffect(() => {
    let fallback: 'general' | 'personalization' | null = null
    if (!personalizationAvailable && activeSettingsTab === 'personalization') {
      fallback = 'general'
    }
    if (!dreamsCapabilityEnabled && activeSettingsTab === 'dreams') {
      fallback = personalizationAvailable ? 'personalization' : 'general'
    }
    if (!fallback) return
    runWithoutAppNavigationRecording(() => setActiveSettingsTab(fallback))
    replaceCurrentAppNavigationLocation()
  }, [activeSettingsTab, dreamsCapabilityEnabled, personalizationAvailable])

  useEffect(() => {
    if (activeSettingsTab === 'personalization' && dreamsCapabilityEnabled) {
      void reloadDreamsStatus()
    }
  }, [activeSettingsTab, dreamsCapabilityEnabled, reloadDreamsStatus])

  useEffect(() => {
    if (activeSettingsTab === 'dreams' && dreamsCapabilityEnabled) {
      void reloadDreamsStatus()
      void reloadDreamRuns()
    }
  }, [activeSettingsTab, dreamsCapabilityEnabled, reloadDreamRuns, reloadDreamsStatus])

  useEffect(() => {
    let cancelled = false
    setResolvingBinary(true)
    window.api.appServer
      .getResolvedBinary({
        binarySource,
        binaryPath
      })
      .then((result) => {
        if (!cancelled) {
          setResolvedBinaryPath(result.path)
        }
      })
      .catch(() => {
        if (!cancelled) {
          setResolvedBinaryPath(null)
        }
      })
      .finally(() => {
        if (!cancelled) {
          setResolvingBinary(false)
        }
      })
    return () => {
      cancelled = true
    }
  }, [binaryPath, binarySource])

  useEffect(() => {
    if (!mcpEnabled) return
    let cancelled = false

    async function loadMcpData(): Promise<void> {
      setMcpLoading(true)
      setMcpError(null)
      try {
        const [listRes, statusRes] = await Promise.all([
          window.api.appServer.sendRequest('mcp/list', {}),
          window.api.appServer.sendRequest('mcpServerStatus/list', { detail: 'toolsAndAuthOnly' })
        ])
        if (cancelled) return
        const list = (listRes as { servers?: McpServerConfigWire[] }).servers ?? []
        const statuses = (statusRes as { data?: McpServerStatusWire[] }).data ?? []
        setMcpServers(list)
        setMcpStatuses(statuses)
      } catch (err) {
        if (!cancelled) {
          setMcpServers([])
          setMcpError(err instanceof Error ? err.message : String(err))
        }
      } finally {
        if (!cancelled) {
          setMcpLoading(false)
        }
      }
    }

    void loadMcpData()
    return () => {
      cancelled = true
    }
  }, [mcpEnabled, setMcpStatuses])

  useEffect(() => {
    if (!mcpSavedHint) return
    const timer = window.setTimeout(() => setMcpSavedHint(''), 1500)
    return () => window.clearTimeout(timer)
  }, [mcpSavedHint])

  const mergedMcpServers = useMemo(() => {
    return [...mcpServers].sort((a, b) => a.name.localeCompare(b.name, undefined, { sensitivity: 'base' }))
  }, [mcpServers])

  function closeSettings(): void {
    if (connectionDirty || llmDirty) {
      const shouldDiscard = window.confirm(t('settings.pendingChanges.leaveConfirm'))
      if (!shouldDiscard) return
      if (baselineConnection) {
        setBinarySource(baselineConnection.binarySource)
        setBinaryPath(baselineConnection.binaryPath)
        setConnectionMode(baselineConnection.connectionMode)
        setWsHost(baselineConnection.wsHost)
        setWsPort(baselineConnection.wsPort)
        setRemoteUrl(baselineConnection.remoteUrl)
        setRemoteToken(baselineConnection.remoteToken)
      }
    }
    setActiveMainView('conversation')
  }

  useEffect(() => {
    if (settingsCloseRequestSeqRef.current === settingsCloseRequestSeq) return
    settingsCloseRequestSeqRef.current = settingsCloseRequestSeq
    closeSettings()
  }, [settingsCloseRequestSeq])

  function startMcpDraft(server?: McpServerConfigWire): void {
    if (server && isPluginManagedMcpServer(server, mcpOriginsEnabled)) return
    const next = server
      ? {
          ...createEmptyMcpServer(),
          ...server,
          args: [...(server.args ?? [])],
          env: { ...(server.env ?? {}) },
          envVars: [...(server.envVars ?? [])],
          httpHeaders: { ...(server.httpHeaders ?? {}) },
          envHttpHeaders: { ...(server.envHttpHeaders ?? {}) }
        }
      : createEmptyMcpServer()
    setEditingServerName(server?.name ?? '__new__')
    setMcpDraft(next)
    setArgRows(normalizeValueRows(next.args))
    setEnvRows(normalizeKeyValueRows(next.env))
    setEnvVarRows(normalizeValueRows(next.envVars))
    setHttpHeaderRows(normalizeKeyValueRows(next.httpHeaders))
    setEnvHttpHeaderRows(normalizeKeyValueRows(next.envHttpHeaders))
    setMcpTestResult(null)
  }

  function cancelMcpEdit(): void {
    setEditingServerName(null)
    setMcpDraft(createEmptyMcpServer())
    setArgRows(normalizeValueRows([]))
    setEnvRows(normalizeKeyValueRows({}))
    setEnvVarRows(normalizeValueRows([]))
    setHttpHeaderRows(normalizeKeyValueRows({}))
    setEnvHttpHeaderRows(normalizeKeyValueRows({}))
    setMcpTestResult(null)
  }

  function buildDraftPayload(): McpServerConfigWire {
    const transport = mcpDraft.transport
    return {
      name: mcpDraft.name.trim(),
      enabled: mcpDraft.enabled,
      transport,
      command: transport === 'stdio' ? mcpDraft.command?.trim() ?? '' : null,
      args: transport === 'stdio' ? rowsToValues(argRows) : null,
      env: transport === 'stdio' ? rowsToRecord(envRows) : null,
      envVars: transport === 'stdio' ? rowsToValues(envVarRows) : null,
      cwd: transport === 'stdio' ? (mcpDraft.cwd?.trim() || null) : null,
      url: transport === 'streamableHttp' ? mcpDraft.url?.trim() ?? '' : null,
      bearerTokenEnvVar:
        transport === 'streamableHttp' ? mcpDraft.bearerTokenEnvVar?.trim() || null : null,
      httpHeaders: transport === 'streamableHttp' ? rowsToRecord(httpHeaderRows) : null,
      envHttpHeaders: transport === 'streamableHttp' ? rowsToRecord(envHttpHeaderRows) : null,
      startupTimeoutSec: mcpDraft.startupTimeoutSec ?? null,
      toolTimeoutSec: mcpDraft.toolTimeoutSec ?? null
    }
  }

  async function reloadMcpServers(): Promise<void> {
    const listRes = await window.api.appServer.sendRequest('mcp/list', {})
    const list = (listRes as { servers?: McpServerConfigWire[] }).servers ?? []
    setMcpServers(list)
  }

  async function reloadMcpStatuses(): Promise<void> {
    const statusRes = await window.api.appServer.sendRequest('mcpServerStatus/list', { detail: 'toolsAndAuthOnly' })
    const statuses = (statusRes as { data?: McpServerStatusWire[] }).data ?? []
    setMcpStatuses(statuses)
  }

  async function handleMcpTest(): Promise<void> {
    const payload = buildDraftPayload()
    setTestingMcp(true)
    setMcpTestResult(null)
    try {
      const result = (await window.api.appServer.sendRequest('mcp/test', {
        server: toContractMcpServer(payload)
      })) as McpTestResultWire
      setMcpTestResult(result)
      addToast(
        result.success
          ? `MCP connection test succeeded${typeof result.toolCount === 'number' ? ` (${result.toolCount} tools)` : ''}`
          : `MCP connection test failed${result.errorMessage ? `: ${result.errorMessage}` : ''}`,
        result.success ? 'success' : 'error'
      )
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err)
      setMcpTestResult({ success: false, errorMessage: message })
      addToast(`MCP connection test failed: ${message}`, 'error')
    } finally {
      setTestingMcp(false)
    }
  }

  async function handleMcpSave(): Promise<void> {
    const payload = buildDraftPayload()
    setSavingMcp(true)
    try {
      const originalName = editingServerName !== '__new__' ? editingServerName?.trim() ?? null : null
      const nextName = payload.name.trim()
      const isRename =
        originalName !== null &&
        originalName.localeCompare(nextName, undefined, { sensitivity: 'accent' }) !== 0

      let renameCleanupFailed = false
      if (isRename) {
        try {
          await window.api.appServer.sendRequest('mcp/remove', { name: originalName })
        } catch (err) {
          renameCleanupFailed = true
          console.warn('Failed to remove old MCP server before rename save', err)
        }
      }

      await window.api.appServer.sendRequest('mcp/upsert', {
        server: toContractMcpServer(payload)
      })
      await Promise.all([reloadMcpServers(), reloadMcpStatuses()])
      setMcpSavedHint(t('settings.savedToast'))
      if (renameCleanupFailed) {
        addToast('MCP server saved, but the old server entry may still exist', 'error')
      }
      cancelMcpEdit()
    } catch (err) {
      addToast(`Failed to save MCP server: ${err instanceof Error ? err.message : String(err)}`, 'error')
    } finally {
      setSavingMcp(false)
    }
  }

  async function handleMcpQuickToggle(server: McpServerConfigWire, nextEnabled: boolean): Promise<void> {
    if (isPluginManagedMcpServer(server, mcpOriginsEnabled)) return
    setTogglingServerName(server.name)
    try {
      await window.api.appServer.sendRequest('mcp/upsert', {
        server: { ...toContractMcpServer(server), enabled: nextEnabled }
      })
      await Promise.all([reloadMcpServers(), reloadMcpStatuses()])
    } catch (err) {
      addToast(
        `Failed to ${nextEnabled ? 'enable' : 'disable'} MCP server: ${err instanceof Error ? err.message : String(err)}`,
        'error'
      )
    } finally {
      setTogglingServerName((current) => (current === server.name ? null : current))
    }
  }

  async function handleMcpDelete(): Promise<void> {
    const name = (editingServerName !== '__new__' ? editingServerName?.trim() : mcpDraft.name.trim()) ?? ''
    if (!name) return
    setDeletingMcp(true)
    try {
      await window.api.appServer.sendRequest('mcp/remove', { name })
      await Promise.all([reloadMcpServers(), reloadMcpStatuses()])
      setMcpSavedHint(t('settings.savedToast'))
      cancelMcpEdit()
    } catch (err) {
      addToast(`Failed to remove MCP server: ${err instanceof Error ? err.message : String(err)}`, 'error')
    } finally {
      setDeletingMcp(false)
    }
  }

  async function handleViewPluginMcp(server: McpServerConfigWire): Promise<void> {
    const pluginId = server.origin?.pluginId?.trim()
    if (!pluginId) return

    try {
      await usePluginStore.getState().selectPlugin(pluginId)
      setActiveMainView('skills')
    } catch (err) {
      addToast(`Failed to open plugin: ${err instanceof Error ? err.message : String(err)}`, 'error')
    }
  }

  async function handleMcpOAuthLogin(server: McpServerConfigWire): Promise<void> {
    setAuthenticatingMcpName(server.name)
    try {
      const result = (await window.api.appServer.sendRequest('mcpServer/oauth/login', {
        name: server.name
      })) as { authorizationUrl?: string }
      if (!result.authorizationUrl) throw new Error('The MCP server did not return an authorization URL.')
      await window.api.shell.openExternal(result.authorizationUrl)
      addToast(t('settings.mcp.oauthOpened'), 'info')
    } catch (err) {
      addToast(
        t('settings.mcp.oauthFailed', { error: err instanceof Error ? err.message : String(err) }),
        'error'
      )
    } finally {
      setAuthenticatingMcpName((current) => current === server.name ? null : current)
    }
  }

  async function handleLocaleChange(next: AppLocale): Promise<void> {
    const normalized = normalizeLocale(next)
    const prev = locale
    setLocale(normalized)
    try {
      await window.api.settings.set({ locale: normalized })
      setUiLocale(normalized)
    } catch (err) {
      setLocale(prev)
      addToast(
        t('settings.saveFailed', {
          error: err instanceof Error ? err.message : String(err)
        }),
        'error'
      )
    }
  }

  async function handleTaskCompletionNotificationModeChange(next: TaskCompletionNotificationMode): Promise<void> {
    const previous = taskCompletionNotificationMode
    setTaskCompletionNotificationMode(next)
    try {
      await window.api.settings.set({
        notifications: {
          taskCompletionMode: next
        }
      })
    } catch (err) {
      setTaskCompletionNotificationMode(previous)
      addToast(
        t('settings.saveFailed', {
          error: err instanceof Error ? err.message : String(err)
        }),
        'error'
      )
    }
  }

  async function persistBrowserUseSettings(next: {
    approvalMode?: BrowserUseApprovalMode
    blockedDomains?: string[]
    allowedDomains?: string[]
  }): Promise<void> {
    const browserUse = {
      approvalMode: next.approvalMode ?? browserUseApprovalMode,
      blockedDomains: next.blockedDomains ?? browserUseBlockedDomains,
      allowedDomains: next.allowedDomains ?? browserUseAllowedDomains
    }
    await window.api.settings.set({ browserUse })
  }

  async function handleBrowserUseApprovalModeChange(next: BrowserUseApprovalMode): Promise<void> {
    const previous = browserUseApprovalMode
    setBrowserUseApprovalMode(next)
    try {
      await persistBrowserUseSettings({ approvalMode: next })
    } catch (err) {
      setBrowserUseApprovalMode(previous)
      addToast(t('settings.saveFailed', { error: err instanceof Error ? err.message : String(err) }), 'error')
    }
  }

  function openBrowserUseDomainDialog(target: 'blocked' | 'allowed'): void {
    setBrowserUseDomainTarget(target)
    setBrowserUseDomainDraft('')
    setBrowserUseDomainError('')
  }

  async function handleAddBrowserUseDomain(): Promise<void> {
    if (!browserUseDomainTarget) return
    const domain = normalizeBrowserUseDomainInput(browserUseDomainDraft)
    if (!domain) {
      setBrowserUseDomainError(t('settings.browserUse.domainInvalid'))
      return
    }
    const blocked = browserUseDomainTarget === 'blocked'
      ? Array.from(new Set([...browserUseBlockedDomains, domain]))
      : browserUseBlockedDomains.filter((item) => item !== domain)
    const allowed = browserUseDomainTarget === 'allowed'
      ? Array.from(new Set([...browserUseAllowedDomains, domain]))
      : browserUseAllowedDomains.filter((item) => item !== domain)
    setBrowserUseBlockedDomains(blocked)
    setBrowserUseAllowedDomains(allowed)
    setBrowserUseDomainTarget(null)
    setBrowserUseDomainDraft('')
    setBrowserUseDomainError('')
    try {
      await persistBrowserUseSettings({ blockedDomains: blocked, allowedDomains: allowed })
    } catch (err) {
      addToast(t('settings.saveFailed', { error: err instanceof Error ? err.message : String(err) }), 'error')
    }
  }

  async function handleRemoveBrowserUseDomain(target: 'blocked' | 'allowed', domain: string): Promise<void> {
    const blocked = target === 'blocked'
      ? browserUseBlockedDomains.filter((item) => item !== domain)
      : browserUseBlockedDomains
    const allowed = target === 'allowed'
      ? browserUseAllowedDomains.filter((item) => item !== domain)
      : browserUseAllowedDomains
    setBrowserUseBlockedDomains(blocked)
    setBrowserUseAllowedDomains(allowed)
    try {
      await persistBrowserUseSettings({ blockedDomains: blocked, allowedDomains: allowed })
    } catch (err) {
      addToast(t('settings.saveFailed', { error: err instanceof Error ? err.message : String(err) }), 'error')
    }
  }

  async function handleClearBrowserUseCookies(): Promise<void> {
    setClearingBrowserCookies(true)
    try {
      await window.api.workspace.viewer.browserUse.clearCookies()
      addToast(t('settings.browserUse.cookiesCleared'), 'success')
    } catch (err) {
      addToast(t('settings.browserUse.cookiesClearFailed', { error: err instanceof Error ? err.message : String(err) }), 'error')
    } finally {
      setClearingBrowserCookies(false)
    }
  }

  async function handleInstallBrowserUsePlugin(): Promise<void> {
    if (!browserUsePlugin) return
    setBrowserUseInstalling(true)
    try {
      await installPlugin(browserUsePlugin.id)
      await fetchPlugins()
      await fetchSkills()
      setBrowserUseInstallOpen(false)
      addToast(t('plugins.installSuccess'), 'success')
    } catch {
      addToast(t('plugins.installFailed'), 'error')
    } finally {
      setBrowserUseInstalling(false)
    }
  }

  async function handleInstallChromePlugin(): Promise<void> {
    if (!chromePlugin) return
    setChromeInstalling(true)
    try {
      await installPlugin(chromePlugin.id)
      await fetchPlugins()
      await fetchSkills()
      setChromeInstallOpen(false)
      setChromeDetailOpen(true)
      addToast(t('plugins.installSuccess'), 'success')
    } catch {
      addToast(t('plugins.installFailed'), 'error')
    } finally {
      setChromeInstalling(false)
    }
  }

  async function handleToggleChromePlugin(enabled: boolean): Promise<void> {
    if (!chromePlugin || chromeToggling) return
    setChromeToggling(true)
    try {
      await togglePluginEnabled(chromePlugin.id, enabled)
      await fetchSkills()
    } catch {
      addToast(t('plugins.updateFailed'), 'error')
    } finally {
      setChromeToggling(false)
    }
  }

  async function reloadChromeSetupStatus(): Promise<void> {
    if (!window.api.chrome?.checkSetup) return
    setChromeSetupLoading(true)
    setChromeSetupError('')
    try {
      const status = await window.api.chrome.checkSetup()
      setChromeSetupStatus(normalizeChromeSetupStatus(status as ChromeSetupStatus))
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err)
      setChromeSetupError(message)
      addToast(t('settings.chrome.checkFailed', { error: message }), 'error')
    } finally {
      setChromeSetupLoading(false)
    }
  }

  async function handleInstallChromeNativeHost(): Promise<void> {
    if (!window.api.chrome?.installNativeHost) return
    setChromeNativeHostInstalling(true)
    try {
      const result = await window.api.chrome.installNativeHost()
      if (!setupResultOk(result)) {
        throw new Error(setupResultText(result, 'error') || setupResultText(result, 'stderr') || 'Chrome connection component install failed.')
      }
      addToast(t('settings.chrome.nativeHostInstalled'), 'success')
      await reloadChromeSetupStatus()
    } catch (err) {
      addToast(t('settings.chrome.nativeHostInstallFailed', { error: err instanceof Error ? err.message : String(err) }), 'error')
    } finally {
      setChromeNativeHostInstalling(false)
    }
  }

  async function handleOpenChrome(url?: string): Promise<void> {
    if (!window.api.chrome?.openChrome) return
    setChromeOpening(true)
    try {
      const result = await window.api.chrome.openChrome({ url })
      if (!setupResultOk(result)) {
        throw new Error(setupResultText(result, 'error') || 'Google Chrome was not found.')
      }
      addToast(t('settings.chrome.opened'), 'success')
      await reloadChromeSetupStatus()
    } catch (err) {
      addToast(t('settings.chrome.openFailed', { error: err instanceof Error ? err.message : String(err) }), 'error')
    } finally {
      setChromeOpening(false)
    }
  }

  function handleTryBrowserUseInChat(): void {
    const prompt = browserUsePlugin?.interface?.defaultPrompt || ''
    // The segments are the content, so the default prompt needs one of its own —
    // see the note on RichInputArea's setContent.
    const segments: ComposerDraftSegment[] = [{ type: 'skill', skillName: 'browser' }]
    if (prompt) segments.push({ type: 'text', value: ` ${prompt}` })
    const text = stringifyComposerDraftSegments(segments)
    const ui = useUIStore.getState()
    const existing = ui.welcomeDraft
    ui.setWelcomeDraft({
      text,
      segments,
      selectionStart: text.length,
      selectionEnd: text.length,
      images: [],
      files: [],
      mode: existing?.mode ?? 'agent',
      model: existing?.model || 'Default',
      approvalPolicy: existing?.approvalPolicy ?? 'default'
    })
    ui.goToNewChat()
  }

  function normalizePortOrDefault(raw: string, defaultPort: number): number {
    const parsed = Number.parseInt(raw.trim(), 10)
    return Number.isInteger(parsed) && parsed > 0 && parsed <= 65535 ? parsed : defaultPort
  }

  function buildConnectionSettingsDraft(): ConnectionSettingsDraft {
    const normalizedPort = normalizePortOrDefault(wsPort, DEFAULT_WS_PORT)
    return {
      binarySource,
      appServerBinaryPath: binaryPath.trim() || undefined,
      connectionMode,
      webSocket: {
        host: wsHost.trim() || DEFAULT_WS_HOST,
        port: normalizedPort
      },
      remote: {
        url: remoteUrl.trim() || undefined,
        token: remoteToken.trim() || undefined
      }
    }
  }

  async function applyConnectionSettings(): Promise<void> {
    const normalizedPort = normalizePortOrDefault(wsPort, DEFAULT_WS_PORT)
    await window.api.appServer.applyConnectionSettings(buildConnectionSettingsDraft())
    setSavedConnectionMode(connectionMode)
    setBaselineConnection({
      binarySource,
      binaryPath,
      connectionMode,
      wsHost,
      wsPort: String(normalizedPort),
      remoteUrl,
      remoteToken
    })
    if (connectionMode !== 'remote') {
      setActiveRemoteStack(null)
    }
  }

  async function handlePickBinary(): Promise<void> {
    try {
      const picked = await window.api.appServer.pickBinary()
      if (picked) {
        setBinaryPath(picked)
      }
    } catch (err) {
      addToast(
        t('settings.pickBinaryFailed', {
          error: err instanceof Error ? err.message : String(err)
        }),
        'error'
      )
    }
  }

  async function handleApplyAndRestartAll(): Promise<void> {
    const targetIsRemote = connectionMode === 'remote'
    let needsAppServerRestart = selfLearningRestartPending && !targetIsRemote
    let appServerRestartAttempted = false
    let connectionApplied = false
    let latestCore: WorkspaceCoreConfigResult | null = null
    setSaving(true)
    setRestartingAppServer(connectionDirty || needsAppServerRestart)
    try {
      if (connectionDirty) {
        connectionApplied = true
        appServerRestartAttempted = !targetIsRemote
        if (!targetIsRemote) {
          setExpectedRestart(true)
        }
        await applyConnectionSettings()
        needsAppServerRestart = false
      }

      if (needsAppServerRestart) {
        appServerRestartAttempted = true
        setExpectedRestart(true)
        await window.api.appServer.restartManaged()
        if (!latestCore) {
          latestCore = await readWorkspaceCoreStrict()
        }
        applyWorkspaceCoreBaseline(latestCore, false)
        setSelfLearningRestartPending(false)
        addToast(t('settings.restartAppServerSuccess'), 'success')
      } else if (connectionApplied) {
        if (targetIsRemote && selfLearningRestartPending) {
          setSelfLearningRestartPending(false)
          addToast(t('settings.personalization.selfLearningRestartBannerRemote'), 'warning')
        } else {
          setSelfLearningRestartPending(false)
        }
        addToast(
          t(targetIsRemote ? 'settings.connection.applyConnectSuccess' : 'settings.restartAppServerSuccess'),
          'success'
        )
      } else if (targetIsRemote && selfLearningRestartPending) {
        setSelfLearningRestartPending(false)
        addToast(t('settings.personalization.selfLearningRestartBannerRemote'), 'warning')
      }
      usePendingRestartStore.getState().clear()
    } catch (err) {
      if (appServerRestartAttempted) {
        setExpectedRestart(false)
      }
      addToast(
        t(appServerRestartAttempted ? 'settings.restartAppServerFailed' : 'settings.saveFailed', {
          error: err instanceof Error ? err.message : String(err)
        }),
        'error'
      )
    } finally {
      setRestartingAppServer(false)
      setSaving(false)
    }
  }

  const pendingRestartSignature = useMemo(() => {
    const parts: string[] = []
    if (connectionDirty) {
      parts.push([
        'connection',
        binarySource,
        binaryPath.trim(),
        connectionMode,
        wsHost.trim(),
        wsPort.trim(),
        remoteUrl.trim(),
        remoteToken.trim()
      ].join(':'))
    }
    if (selfLearningRestartPending) {
      parts.push(`selfLearning:${selfLearningEnabled}`)
    }
    return parts.join('|')
  }, [
    binaryPath,
    binarySource,
    connectionDirty,
    connectionMode,
    remoteToken,
    remoteUrl,
    selfLearningEnabled,
    selfLearningRestartPending,
    wsHost,
    wsPort
  ])

  const pendingRestartLabels = useMemo(() => {
    if (connectionDirty && connectionMode === 'remote') {
      return {
        messageKey: 'settings.pendingReconnect.message',
        applyKey: 'settings.pendingReconnect.apply',
        applyingKey: 'settings.action.connecting'
      }
    }
    return undefined
  }, [connectionDirty, connectionMode])

  useEffect(() => {
    if (pendingRestartSignature) {
      usePendingRestartStore.getState().setPending(
        pendingRestartSignature,
        handleApplyAndRestartAll,
        pendingRestartLabels
      )
    } else {
      usePendingRestartStore.getState().clear()
    }
  })

  useEffect(() => {
    return () => {
      usePendingRestartStore.getState().clear()
    }
  }, [])

  const dreamsIntervalOptions = useMemo(() => {
    return DREAMS_INTERVAL_OPTIONS.includes(dreamsInterval as typeof DREAMS_INTERVAL_OPTIONS[number])
      ? [...DREAMS_INTERVAL_OPTIONS]
      : [dreamsInterval, ...DREAMS_INTERVAL_OPTIONS]
  }, [dreamsInterval])

  const dreamsThreadLookbackOptions = useMemo(() => {
    return DREAMS_THREAD_LOOKBACK_OPTIONS.includes(dreamsThreadLookbackCount as typeof DREAMS_THREAD_LOOKBACK_OPTIONS[number])
      ? [...DREAMS_THREAD_LOOKBACK_OPTIONS]
      : [dreamsThreadLookbackCount, ...DREAMS_THREAD_LOOKBACK_OPTIONS]
  }, [dreamsThreadLookbackCount])

  const dreamsRunDisabled = runningDreams || dreamsStatus?.running === true || dreamsEnabled === false
  const dreamsArchiveBusy = archivingDreamRunId != null || archivingAllDreamRuns
  const archiveAllDreamRunsDisabled =
    dreamRuns.length === 0 ||
    dreamRunsLoading ||
    dreamsArchiveBusy ||
    dreamRuns.some((run) => run.status === 'running')

  return (
    <div
      aria-label={t('settings.title')}
      style={{
        display: 'flex',
        flexDirection: 'column',
        height: '100%',
        minHeight: 0,
        // Transparent so the shared ThreePanel main-surface frame (rounded card +
        // inset edge borders) shows through, matching the conversation and other
        // main views. An opaque --bg-primary here painted over and hid the frame.
        backgroundColor: 'transparent'
      }}
    >
      <main style={settingsMainStyle()}>
        <div className={SETTINGS_SURFACE_CLASS} style={settingsContentContainerStyle()}>
            {activeSettingsTab === 'profile' && (
              <ProfilePanel>
                <ProfileView />
              </ProfilePanel>
            )}

            {activeSettingsTab === 'appearance' && <AppearancePanel />}

            {activeSettingsTab === 'voice' && <VoicePanel />}

            {activeSettingsTab === 'general' && (
              <GeneralPanel>
              <SettingsPanelShell
                title={t('settings.tab.general')}
                description={t('settings.general.description')}
              >
                <SettingsGroup
                  title={t('settings.group.application')}
                  // The app name is redundant inside its own settings window, and a
                  // version is card metadata rather than a setting — it belongs in the
                  // header slot, not in a row that reads like something you can change.
                  headerAction={
                    <span style={settingsMetaTextStyle()}>
                      {t('settings.version')} {version}
                    </span>
                  }
                >
                  <SettingsRow
                    label={t('settings.language')}
                    htmlFor="settings-language"
                    control={
                      <SettingsSelect
                        id="settings-language"
                        value={locale}
                        onValueChange={(nextLocale) => {
                          void handleLocaleChange(nextLocale as AppLocale)
                        }}
                        style={{ width: SETTINGS_SELECT_WIDTH }}
                        options={SUPPORTED_LOCALES.map((item) => ({
                          value: item.value,
                          label: item.nativeName
                        }))}
                      />
                    }
                  />

                  {isMac && (
                    <SettingsRow
                      label={t('settings.general.showInMenuBar')}
                      description={t('settings.general.showInMenuBarHint')}
                      control={
                        <PillSwitch
                          checked={showInMenuBar}
                          aria-label={t('settings.general.showInMenuBar')}
                          onChange={(checked) => {
                            void handleShowInMenuBarToggle(checked)
                          }}
                        />
                      }
                    />
                  )}
                </SettingsGroup>

                <SettingsGroup title={t('settings.notifications.title')}>
                  <SettingsRow
                    label={t('settings.notifications.taskCompletion')}
                    description={t('settings.notifications.taskCompletionHint')}
                    htmlFor="settings-task-completion-notification"
                    control={
                      <SettingsSelect
                        id="settings-task-completion-notification"
                        value={taskCompletionNotificationMode}
                        onValueChange={(mode) => {
                          void handleTaskCompletionNotificationModeChange(mode as TaskCompletionNotificationMode)
                        }}
                        style={{ width: SETTINGS_SELECT_WIDTH }}
                        options={[
                          {
                            value: 'whenUnfocused',
                            label: t('settings.notifications.taskCompletion.whenUnfocused')
                          },
                          { value: 'always', label: t('settings.notifications.taskCompletion.always') },
                          { value: 'never', label: t('settings.notifications.taskCompletion.never') }
                        ]}
                      />
                    }
                  />
                </SettingsGroup>

                <SettingsGroup
                  title={t('settings.group.permissions')}
                  description={
                    <SettingsDescriptionWithLearnMore topic="security" aboutKey="settings.group.permissions">
                      {t('settings.permissions.description')}
                    </SettingsDescriptionWithLearnMore>
                  }
                >
                  <SettingsRow
                    label={t('settings.permissions.workspaceDefault.label')}
                    description={t('settings.permissions.workspaceDefault.description')}
                    htmlFor="settings-default-approval-policy"
                    control={
                      <SettingsSelect
                        id="settings-default-approval-policy"
                        value={defaultApprovalPolicy}
                        disabled={applyingDefaultApprovalPolicy}
                        ariaLabel={t('settings.permissions.workspaceDefault.label')}
                        onValueChange={(nextPolicy) => {
                          return handleDefaultApprovalPolicyChange(nextPolicy as VisibleApprovalPolicy)
                        }}
                        style={{ width: SETTINGS_SELECT_WIDTH }}
                        options={[
                          {
                            value: 'default',
                            label: t('settings.permissions.default.label'),
                            description: t('settings.permissions.default.description'),
                            icon: <Hand size={15} strokeWidth={1.9} />
                          },
                          {
                            value: 'autoApprove',
                            label: t('settings.permissions.fullAccess.label'),
                            description: t('settings.permissions.fullAccess.description'),
                            icon: <OctagonAlert size={15} strokeWidth={1.9} />
                          }
                        ]}
                      />
                    }
                  />
                </SettingsGroup>

              </SettingsPanelShell>
              </GeneralPanel>
            )}

            {activeSettingsTab === 'llmService' && (
              <GeneralPanel>
              <SettingsPanelShell
                  title={t('settings.llm.title')}
                  description={
                    providerEditorId === null ? (
                      <SettingsDescriptionWithLearnMore topic="modelProviders" aboutKey="settings.llm.title">
                        {t('settings.llm.description')}
                      </SettingsDescriptionWithLearnMore>
                    ) : undefined
                  }
                  breadcrumb={
                    providerEditorId === null ? undefined : (
                      <SettingsBreadcrumb
                        parentLabel={t('settings.llm.title')}
                        currentLabel={providerEditorIsNew ? t('settings.llm.newTitle') : t('settings.llm.editTitle')}
                        onBack={closeProviderEditor}
                      />
                    )
                  }
                  action={
                    providerEditorId === null ? (
                      <Button
                        variant="primary"
                        onClick={startCreateProvider}
                        disabled={!providerManagementEnabled}
                        iconLeft={<Plus size={14} aria-hidden="true" />}
                      >
                        {t('settings.llm.addProvider')}
                      </Button>
                    ) : undefined
                  }
                >

                {!providerManagementEnabled && (
                  <SettingsGroup>
                    <SettingsRow>
                      <div style={{ fontSize: '13px', color: 'var(--text-dimmed)' }}>
                        {t('settings.llm.unsupported')}
                      </div>
                    </SettingsRow>
                  </SettingsGroup>
                )}

                {providerManagementEnabled && providerEditorId === null && (
                  <>
                    <SettingsGroup
                      title={t('settings.llm.workspaceTitle')}
                      description={
                        selectedProvider
                          ? t('settings.llm.workspaceDescriptionForProvider', { name: selectedProvider.displayName })
                          : t('settings.llm.workspaceDescription')
                      }
                      headerAction={
                        <IconButton
                          icon={<RefreshIcon size={15} />}
                          label={t('settings.llm.refreshModels')}
                          tooltipLabel={t('settings.llm.refreshModels')}
                          disabled={providerModelLoading || !selectedProviderId}
                          onClick={() => void reloadProviderPreferences(selectedProviderId)}
                        />
                      }
                      flush
                    >
                      <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
                        <div style={{ minWidth: 0 }}>
                          <label htmlFor="settings-provider-model" style={sectionLabelStyle()}>
                            {t('settings.llm.workspaceModel')}
                          </label>
                          <PreferenceModelPicker
                            preference={workspacePreference}
                            models={providerModelCatalog}
                            loading={providerModelLoading}
                            disabled={applyingWorkspaceModel || applyingWorkspaceProvider}
                            errorMessage={providerModelError || null}
                            manualFallback={!providerModelLoading && providerModelCatalog.length === 0}
                            onRetry={() => void reloadProviderPreferences(selectedProviderId)}
                            onChange={(next) => {
                              if (providerModelCatalog.length === 0) {
                                setWorkspacePreference(next)
                                setWorkspaceManualModelDraft(next.model)
                              } else {
                                void handleWorkspacePreferenceChange(next)
                              }
                            }}
                            onManualCommit={() => void handleWorkspaceManualModelCommit()}
                            inputId="settings-provider-model"
                            inputAriaLabel={t('settings.llm.workspaceModel')}
                            placeholder={t('settings.llm.workspaceModelPlaceholder')}
                          />
                        </div>
                        {providerModelError && (
                          <div style={{ ...settingsHintStyle(false), color: 'var(--warning)' }}>
                            {providerModelError}
                          </div>
                        )}
                        {subAgentEnabled && selectedProviderId && (
                          <div style={{ minWidth: 0 }}>
                            <div style={{
                              display: 'flex',
                              alignItems: 'center',
                              justifyContent: 'space-between',
                              minHeight: '18px',
                              gap: '12px',
                              marginBottom: '7px'
                            }}>
                              <label htmlFor="settings-subagent-model" style={{ ...sectionLabelStyle(), marginBottom: 0 }}>
                                {t('settings.llm.subAgentModelTitle')}
                              </label>
                              <span style={{ display: 'inline-flex', alignItems: 'center', gap: '8px' }}>
                                <span style={{ color: 'var(--text-tertiary)', fontSize: '10px', fontWeight: 500 }}>
                                  {subAgentPreference == null
                                    ? t('settings.llm.subAgentPreferenceInherit')
                                    : t('settings.llm.subAgentPreferenceCustom')}
                                </span>
                                <PillSwitch
                                  checked={subAgentPreference != null}
                                  onChange={(checked) => void handleSubAgentInheritanceChange(checked)}
                                  disabled={
                                    applyingSubAgentModel
                                    || applyingWorkspaceProvider
                                    || providerModelLoading
                                    || workspacePreference.model.trim() === ''
                                  }
                                  aria-label={t('settings.llm.subAgentPreferenceCustomAria')}
                                  size="sm"
                                />
                              </span>
                            </div>
                            <PreferenceModelPicker
                              preference={subAgentPreference ?? workspacePreference}
                              models={providerModelCatalog}
                              loading={providerModelLoading}
                              disabled={
                                applyingSubAgentModel
                                || applyingWorkspaceProvider
                                || subAgentPreference == null
                              }
                              errorMessage={providerModelError || null}
                              manualFallback={!providerModelLoading && providerModelCatalog.length === 0}
                              onRetry={() => void reloadProviderPreferences(selectedProviderId)}
                              onChange={(next) => {
                                if (providerModelCatalog.length === 0) {
                                  setSubAgentPreference(next)
                                  setSubAgentManualModelDraft(next.model)
                                } else {
                                  void handleSubAgentPreferenceChange(next)
                                }
                              }}
                              onManualCommit={() => void handleSubAgentManualModelCommit()}
                              inputId="settings-subagent-model"
                              inputAriaLabel={t('settings.llm.subAgentModelTitle')}
                              placeholder={t('settings.llm.subAgentModelPlaceholder')}
                            />
                          </div>
                        )}
                      </div>
                    </SettingsGroup>

                    <SettingsGroup
                      title={t('settings.llm.providersTitle')}
                      description={providersLoading ? t('settings.llm.providersLoading') : providersCountLabel}
                      headerAction={
                        <IconButton
                          icon={<RefreshIcon size={15} />}
                          label={t('settings.llm.refreshProviders')}
                          tooltipLabel={t('settings.llm.refreshProviders')}
                          disabled={providersLoading}
                          onClick={() => void reloadProviders()}
                        />
                      }
                      flush
                    >
                      <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                        {providersLoading && (
                          <div style={{ fontSize: '13px', color: 'var(--text-dimmed)' }}>
                            {t('settings.llm.providersLoading')}
                          </div>
                        )}

                        {!providersLoading && providers.length === 0 && (
                          <div style={{ padding: '8px 0' }}>
                            <div style={{ fontSize: '14px', color: 'var(--text-primary)', marginBottom: '6px' }}>
                              {t('settings.llm.emptyProvidersTitle')}
                            </div>
                            <div style={settingsPlaceholderStyle()}>
                              {t('settings.llm.emptyProvidersHint')}
                            </div>
                          </div>
                        )}

                        {!providersLoading && providers.map((provider) => {
                          const active = provider.id === selectedProviderId
                          const rememberedMainAgentPreference = active
                            ? workspacePreference
                            : resolveEffectiveProviderPreference(
                              providerPreferences,
                              userDefaultCore.providerPreferences,
                              provider.id
                            )
                          const rememberedSubAgentPreference = active
                            ? subAgentPreference
                            : findProviderPreference(subAgentProviderPreferences, provider.id)
                          const formatPreference = (preference: ModelPreference): string => {
                            const reasoning = preference.reasoning.enabled
                              ? preference.reasoning.effort === 'extraHigh'
                                ? t('composer.reasoning.extraHigh')
                                : preference.reasoning.effort === 'high'
                                  ? t('composer.reasoning.high')
                                  : preference.reasoning.effort === 'medium'
                                    ? t('composer.reasoning.medium')
                                    : preference.reasoning.effort === 'low'
                                      ? t('composer.reasoning.low')
                                      : t('composer.reasoning.off')
                              : t('composer.reasoning.off')
                            const speed = preference.speed === 'fast'
                              ? t('composer.speed.fast')
                              : t('composer.speed.standard')
                            return [
                              preference.model,
                              reasoning,
                              speed,
                              preference.contextWindow.mode === 'max' ? 'MAX' : null
                            ].filter(Boolean).join(' · ')
                          }
                          return (
                            <div
                              key={provider.id}
                              role="button"
                              tabIndex={0}
                              aria-label={t('settings.llm.useProviderAria', { name: provider.displayName })}
                              aria-pressed={active}
                              aria-disabled={applyingWorkspaceProvider || undefined}
                              onClick={() => {
                                if (!active && !applyingWorkspaceProvider) {
                                  void handleWorkspaceProviderChange(provider.id)
                                }
                              }}
                              onKeyDown={(event) => {
                                if (event.key === 'Enter' || event.key === ' ') {
                                  event.preventDefault()
                                  if (!active && !applyingWorkspaceProvider) {
                                    void handleWorkspaceProviderChange(provider.id)
                                  }
                                }
                              }}
                              style={providerRowStyle(active)}
                            >
                              <ProviderProtocolIcon protocol={provider.protocol} size={30} />
                              <div style={{ flex: 1, minWidth: 0 }}>
                                <div style={{ display: 'flex', alignItems: 'center', gap: '8px', flexWrap: 'wrap' }}>
                                  <div style={{ fontSize: '15px', fontWeight: 600, color: 'var(--text-primary)' }}>
                                    {provider.displayName}
                                  </div>
                                  {provider.isImplicit && (
                                    <span style={providerBadgeStyle('neutral')}>{t('settings.llm.implicitProvider')}</span>
                                  )}
                                  {active && (
                                    <span style={providerBadgeStyle('accent')}>{t('settings.llm.selectedProvider')}</span>
                                  )}
                                </div>
                                <div
                                  style={{
                                    marginTop: '5px',
                                    fontSize: '12px',
                                    color: 'var(--text-dimmed)',
                                    display: 'flex',
                                    flexWrap: 'wrap',
                                    gap: '6px'
                                  }}
                                >
                                  <span>
                                    {provider.authMethod === 'chatgptOAuth'
                                      ? provider.chatGptPlanType
                                        ? t('settings.llm.providerChatGptStatus', { plan: formatPlanLabel(provider.chatGptPlanType, t) })
                                        : t('settings.llm.providerChatGptNotSignedIn')
                                      : provider.hasApiKey
                                        ? t('settings.llm.providerKeyConfigured')
                                        : t('settings.llm.providerKeyMissing')}
                                  </span>
                                  <span aria-hidden>·</span>
                                  <span>{providerProtocolLabel(provider.protocol)}</span>
                                  <span aria-hidden>·</span>
                                  <span style={{ minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                                    {provider.endPoint || t('settings.llm.providerDefaultEndpoint')}
                                  </span>
                                </div>
                                {(rememberedMainAgentPreference || rememberedSubAgentPreference) && (
                                  <div
                                    style={{
                                      marginTop: '4px',
                                      fontSize: '12px',
                                      color: 'var(--text-dimmed)',
                                      display: 'flex',
                                      flexWrap: 'wrap',
                                      gap: '6px'
                                    }}
                                  >
                                    {rememberedMainAgentPreference && (
                                      <span style={{ minWidth: 0, maxWidth: '100%', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                                        {t('settings.llm.providerRememberedMainAgentModel', {
                                          model: formatPreference(rememberedMainAgentPreference)
                                        })}
                                      </span>
                                    )}
                                    {rememberedMainAgentPreference && <span aria-hidden>·</span>}
                                    <span style={{ minWidth: 0, maxWidth: '100%', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                                      {rememberedSubAgentPreference
                                        ? t('settings.llm.providerRememberedSubAgentModel', {
                                            model: formatPreference(rememberedSubAgentPreference)
                                          })
                                        : t('settings.llm.providerInheritedSubAgentPreference')}
                                    </span>
                                  </div>
                                )}
                              </div>
                              <span
                                onClick={(event) => event.stopPropagation()}
                                onKeyDown={(event) => event.stopPropagation()}
                                style={{ flexShrink: 0, display: 'inline-flex' }}
                              >
                                <IconButton
                                  icon={<Pencil size={15} />}
                                  label={t('settings.llm.editProviderAria', { name: provider.displayName })}
                                  tooltipLabel={t('settings.llm.editTitle')}
                                  onClick={() => startEditProvider(provider)}
                                />
                              </span>
                            </div>
                          )
                        })}
                      </div>
                    </SettingsGroup>
                  </>
                )}

                {providerManagementEnabled && providerEditorId !== null && (
                  <div style={{ display: 'flex', flexDirection: 'column', gap: '14px' }}>
                    <SettingsGroup flush>
                      <div style={providerFieldStackStyle()}>
                        <div style={providerFieldGridStyle()}>
                          <div>
                            <label htmlFor="settings-provider-id" style={sectionLabelStyle()}>
                              {t('settings.llm.field.id')}
                            </label>
                            <Input
                              id="settings-provider-id"
                              value={providerDraft.id}
                              onChange={(e) => setProviderDraft((draft) => ({ ...draft, id: slugProviderId(e.target.value) }))}
                              readOnly={!providerEditorIsNew || providerDraft.authMethod === 'chatgptOAuth'}
                              mono
                              style={{ opacity: !providerEditorIsNew || providerDraft.authMethod === 'chatgptOAuth' ? 0.65 : 1 }}
                              placeholder="anthropic"
                            />
                            <div style={{ ...settingsHintStyle(false), marginTop: '6px' }}>
                              {providerDraft.authMethod === 'chatgptOAuth'
                                ? t('settings.llm.field.lockedForChatGpt')
                                : providerEditorIsNew
                                  ? t('settings.llm.field.idNewHint')
                                  : t('settings.llm.field.idEditHint')}
                            </div>
                          </div>
                          <div>
                            <label htmlFor="settings-provider-display-name" style={sectionLabelStyle()}>
                              {t('settings.llm.field.displayName')}
                            </label>
                            <Input
                              id="settings-provider-display-name"
                              value={providerDraft.displayName}
                              onChange={(e) => {
                                const displayName = e.target.value
                                setProviderDraft((draft) => ({
                                  ...draft,
                                  displayName,
                                  id: providerEditorIsNew ? (draft.id || slugProviderId(displayName)) : draft.id
                                }))
                              }}
                              readOnly={providerDraft.authMethod === 'chatgptOAuth'}
                              style={{ opacity: providerDraft.authMethod === 'chatgptOAuth' ? 0.65 : 1 }}
                              placeholder="Anthropic"
                            />
                            {providerDraft.authMethod === 'chatgptOAuth' && (
                              <div style={{ ...settingsHintStyle(false), marginTop: '6px' }}>
                                {t('settings.llm.field.lockedForChatGpt')}
                              </div>
                            )}
                          </div>
                        </div>

                        <div>
                          <label htmlFor="settings-provider-protocol" style={sectionLabelStyle()}>
                            {t('settings.llm.field.protocol')}
                          </label>
                          <SettingsSelect<DesktopProviderProtocol>
                            id="settings-provider-protocol"
                            value={providerDraft.protocol}
                            onValueChange={(protocol) => setProviderDraft((draft) => {
                              const nextEndPoint =
                                !draft.endPoint || draft.endPoint === defaultProviderEndpoint(draft.protocol)
                                  ? defaultProviderEndpoint(protocol)
                                  : draft.endPoint
                              // ChatGPT OAuth only works on the Responses path; switching to any
                              // other protocol must clear the OAuth selection so the UI reverts
                              // to the API-key form and the persisted payload stays consistent.
                              if (protocol !== OPENAI_RESPONSES_PROTOCOL && draft.authMethod === 'chatgptOAuth') {
                                const prev = providerEditorIsNew ? preChatGptDraftRef.current : null
                                preChatGptDraftRef.current = null
                                return withDefaultHostedImageGenerationSupport({
                                  ...draft,
                                  protocol,
                                  endPoint: nextEndPoint,
                                  authMethod: 'apiKey',
                                  id: prev ? prev.id : draft.id,
                                  displayName: prev ? prev.displayName : draft.displayName
                                })
                              }
                              return withDefaultHostedImageGenerationSupport({
                                ...draft,
                                protocol,
                                endPoint: nextEndPoint,
                                authMethod: protocol === OPENAI_RESPONSES_PROTOCOL ? draft.authMethod : 'apiKey'
                              })
                            })}
                            options={DESKTOP_PROVIDER_PROTOCOLS.map((protocol) => ({
                              value: protocol,
                              label: providerProtocolLabel(protocol)
                            }))}
                          />
                        </div>
                      </div>
                    </SettingsGroup>

                    {providerDraft.protocol === OPENAI_RESPONSES_PROTOCOL && (
                      <SettingsGroup title={t('settings.llm.field.authMethod')} flush>
                        <div style={{ display: 'grid', gap: '8px' }}>
                          <button
                            type="button"
                            onClick={() => setProviderDraft((draft) => {
                              if (draft.authMethod !== 'chatgptOAuth') return draft
                              // Restore the user's pre-OAuth id/displayName when in NEW mode.
                              if (providerEditorIsNew && preChatGptDraftRef.current) {
                                const prev = preChatGptDraftRef.current
                                preChatGptDraftRef.current = null
                                return withDefaultHostedImageGenerationSupport({
                                  ...draft,
                                  authMethod: 'apiKey',
                                  id: prev.id,
                                  displayName: prev.displayName
                                })
                              }
                              return withDefaultHostedImageGenerationSupport({ ...draft, authMethod: 'apiKey' })
                            })}
                            style={{
                              border: providerDraft.authMethod === 'apiKey'
                                ? '1px solid var(--accent)'
                                : '1px solid var(--border-default)',
                              background: providerDraft.authMethod === 'apiKey'
                                ? 'var(--bg-tertiary)'
                                : 'var(--bg-secondary)',
                              borderRadius: '8px',
                              padding: '10px 12px',
                              textAlign: 'left',
                              cursor: 'pointer'
                            }}
                          >
                            <div style={{ fontWeight: 600, fontSize: '13px', color: 'var(--text-primary)' }}>
                              {t('settings.llm.authMethod.apiKey')}
                            </div>
                            <div style={settingsDescriptionStyle()}>
                              {t('settings.llm.authMethod.apiKeyDescription')}
                            </div>
                          </button>
                          <button
                            type="button"
                            onClick={() => setProviderDraft((draft) => {
                              if (draft.authMethod === 'chatgptOAuth') return draft
                              // In NEW mode, lock id/displayName to canonical values so the OAuth bind
                              // helper on the backend can't silently rewrite a user-typed displayName.
                              // Snapshot the previous values so a toggle back restores them.
                              if (providerEditorIsNew) {
                                preChatGptDraftRef.current = { id: draft.id, displayName: draft.displayName }
                                return withDefaultHostedImageGenerationSupport({
                                  ...draft,
                                  authMethod: 'chatgptOAuth',
                                  apiKey: '',
                                  id: uniqueProviderId(OPENAI_CHATGPT_DEFAULT_ID, providers),
                                  displayName: OPENAI_CHATGPT_DISPLAY_NAME
                                })
                              }
                              return withDefaultHostedImageGenerationSupport({ ...draft, authMethod: 'chatgptOAuth', apiKey: '' })
                            })}
                            style={{
                              border: providerDraft.authMethod === 'chatgptOAuth'
                                ? '1px solid var(--accent)'
                                : '1px solid var(--border-default)',
                              background: providerDraft.authMethod === 'chatgptOAuth'
                                ? 'var(--bg-tertiary)'
                                : 'var(--bg-secondary)',
                              borderRadius: '8px',
                              padding: '10px 12px',
                              textAlign: 'left',
                              cursor: 'pointer'
                            }}
                          >
                            <div style={{ fontWeight: 600, fontSize: '13px', color: 'var(--text-primary)' }}>
                              {t('settings.llm.authMethod.chatgpt')}
                            </div>
                            <div style={settingsDescriptionStyle()}>
                              {t('settings.llm.authMethod.chatgptDescription')}
                            </div>
                          </button>
                        </div>
                      </SettingsGroup>
                    )}

                    {providerDraft.protocol === OPENAI_RESPONSES_PROTOCOL &&
                     providerDraft.authMethod === 'chatgptOAuth' ? (
                      <SettingsGroup title={t('settings.llm.connectionTitle')} flush>
                        <ChatGptOAuthPanel
                          providerId={providerDraft.id}
                          providerInfo={providerEditorProvider}
                          selectedProviderId={selectedProviderId || null}
                          selectedProviderHasApiKey={
                            providers.find((p) => p.id === selectedProviderId)?.hasApiKey ?? false
                          }
                          onAfterMutation={() => void reloadProviders()}
                          onProviderActivated={(activatedId) => {
                            selectedProviderIdRef.current = activatedId
                            setSelectedProviderId(activatedId)
                            setWorkspaceCoreBaseline((current) => ({ ...current, providerId: activatedId }))
                          }}
                        />
                      </SettingsGroup>
                    ) : (
                      <SettingsGroup title={t('settings.llm.connectionTitle')} flush>
                        <div style={providerFieldStackStyle()}>
                          <div>
                            <label htmlFor="settings-provider-api-key" style={sectionLabelStyle()}>
                              {t('settings.llm.field.apiKey')}
                            </label>
                            <SecretInput
                              value={providerDraft.apiKey}
                              ariaLabel={t('settings.llm.field.apiKey')}
                              onChange={(apiKey) => setProviderDraft((draft) => ({ ...draft, apiKey }))}
                              mono
                              placeholder={t('settings.llm.field.apiKeyPlaceholder')}
                            />
                            {providerEditorProvider?.hasApiKey === true && providerDraft.apiKey === '********' && (
                              <div style={{ ...settingsHintStyle(false), marginTop: '6px' }}>
                                {t('settings.llm.field.apiKeyKeepHint')}
                              </div>
                            )}
                          </div>

                          <div>
                            <label htmlFor="settings-provider-endpoint" style={sectionLabelStyle()}>
                              {t('settings.llm.field.endpoint')}
                            </label>
                            <Input
                              id="settings-provider-endpoint"
                              type="url"
                              value={providerDraft.endPoint}
                              onChange={(e) => setProviderDraft((draft) => withDefaultHostedImageGenerationSupport({
                                ...draft,
                                endPoint: e.target.value
                              }))}
                              mono
                              placeholder={defaultProviderEndpoint(providerDraft.protocol)}
                            />
                            <div style={{ ...settingsHintStyle(false), marginTop: '6px' }}>
                              {providerDraft.protocol === ANTHROPIC_PROTOCOL
                                ? t('settings.llm.field.endpointAnthropicHint')
                                : t('settings.llm.field.endpointOpenAiHint')}
                            </div>
                          </div>
                        </div>
                      </SettingsGroup>
                    )}

                    <SettingsGroup title={t('settings.group.advanced')}>
                      <SettingsRow
                        label={t('settings.llm.field.timeout')}
                        description={t('settings.llm.field.timeoutHint')}
                        htmlFor="settings-provider-timeout"
                        orientation="block"
                        control={
                          <Input
                            id="settings-provider-timeout"
                            type="number"
                            className="dc-plain-number"
                            min={1}
                            value={providerDraft.networkTimeoutSeconds}
                            onChange={(e) => setProviderDraft((draft) => ({ ...draft, networkTimeoutSeconds: e.target.value }))}
                            mono
                            placeholder={t('settings.llm.field.timeoutPlaceholder')}
                          />
                        }
                      />
                      {canConfigureHostedImageGeneration(providerDraft) && (
                        <SettingsRow
                          label={t('settings.llm.field.hostedImageGeneration')}
                          description={t('settings.llm.field.hostedImageGenerationHint')}
                          controlMinWidth={48}
                          control={
                            <PillSwitch
                              checked={providerDraft.supportsHostedImageGeneration}
                              aria-label={t('settings.llm.field.hostedImageGeneration')}
                              onChange={(supportsHostedImageGeneration) => {
                                setProviderDraft((draft) => ({
                                  ...draft,
                                  supportsHostedImageGeneration,
                                  supportsHostedImageGenerationTouched: true
                                }))
                              }}
                            />
                          }
                        />
                      )}
                    </SettingsGroup>

                    <div style={providerFooterStyle()}>
                      <div style={{ flex: '1 1 260px', minWidth: 0, display: 'flex', alignItems: 'center', gap: '12px' }}>
                        {canDeleteProviderInEditor && providerEditorProvider && (
                          <Button
                            variant="danger"
                            onClick={() => void handleProviderDelete(providerEditorProvider)}
                            disabled={deletingProvider || savingProvider}
                          >
                            {deletingProvider ? t('settings.llm.deletingProvider') : t('settings.llm.deleteProvider')}
                          </Button>
                        )}
                        {(() => {
                          if (testingProvider) {
                            return (
                              <div role="status" aria-live="polite" style={providerInlineStatusStyle('neutral')}>
                                <span style={providerInlineStatusDotStyle('neutral')} />
                                <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                                  {t('settings.llm.testRunningInline')}
                                </span>
                              </div>
                            )
                          }
                          if (!providerTestResult) return null
                          const tone = providerTestResult.success
                            ? 'success'
                            : providerTestResult.errorCode === 'EndpointNotSupported'
                              ? 'warning'
                              : 'error'
                          const message = providerTestResult.success
                            ? t('settings.llm.testSuccessInline', { count: providerTestResult.models?.length ?? 0 })
                            : providerTestResult.errorCode === 'EndpointNotSupported'
                              ? t('settings.llm.testUnsupportedInline')
                              : t('settings.llm.testFailedInline', {
                                  error: providerTestResult.errorMessage ?? providerTestResult.errorCode ?? t('settings.llm.unknownError')
                                })
                          return (
                            <ActionTooltip
                              label={message}
                              wrapperStyle={{ display: 'block', minWidth: 0, overflow: 'hidden', flexShrink: 1 }}
                            >
                              <div role="status" aria-live="polite" style={providerInlineStatusStyle(tone)}>
                                <span style={providerInlineStatusDotStyle(tone)} />
                                <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                                  {message}
                                </span>
                              </div>
                            </ActionTooltip>
                          )
                        })()}
                      </div>
                      <div style={{ display: 'flex', gap: '8px', flexWrap: 'wrap' }}>
                        <Button
                          onClick={() => void handleProviderTest()}
                          disabled={testingProvider || !providerManagementEnabled}
                          iconLeft={<TestTube2 size={14} aria-hidden="true" />}
                        >
                          {testingProvider ? t('settings.llm.testingProvider') : t('settings.llm.testProvider')}
                        </Button>
                        {/* In NEW + ChatGPT-OAuth mode, the Sign In button inside ChatGptOAuthPanel
                            is the commit action (it calls BindProviderToOAuth on the backend), so
                            a redundant Save here would either no-op or fail with "already exists". */}
                        {!(providerEditorIsNew && providerDraft.authMethod === 'chatgptOAuth') && (
                          <Button
                            variant="primary"
                            onClick={() => void handleProviderSave()}
                            disabled={savingProvider || !providerManagementEnabled}
                            iconLeft={<ListChecks size={14} aria-hidden="true" />}
                          >
                            {savingProvider
                              ? t('settings.llm.savingProvider')
                              : providerEditorIsNew
                                ? t('settings.llm.createProvider')
                                : t('settings.llm.updateProvider')}
                          </Button>
                        )}
                      </div>
                    </div>
                  </div>
                )}
              </SettingsPanelShell>
              </GeneralPanel>
            )}

            {personalizationAvailable && activeSettingsTab === 'personalization' && (
              <GeneralPanel>
              <SettingsPanelShell
                  title={t('settings.tab.personalization')}
                  description={
                    <SettingsDescriptionWithLearnMore topic="memory" aboutKey="settings.tab.personalization">
                      {t('settings.personalization.description')}
                    </SettingsDescriptionWithLearnMore>
                  }
                >
                <SettingsGroup
                  title={t('settings.personalization.group.conversation')}
                >
                  {workspaceCoreApiAvailable && (
                    <SettingsRow
                      label={t('settings.personalization.welcomeSuggestions')}
                      description={t('settings.personalization.welcomeSuggestionsHint')}
                      control={
                        <PillSwitch
                          checked={welcomeSuggestionsEnabled}
                          disabled={applyingWelcomeSuggestions}
                          aria-label={t('settings.personalization.welcomeSuggestions')}
                          onChange={(checked) => {
                            void handleWelcomeSuggestionsToggle(checked)
                          }}
                        />
                      }
                    />
                  )}
                  <SettingsRow
                    label={t('settings.personalization.showThinkingContent')}
                    description={t('settings.personalization.showThinkingContentHint')}
                    control={
                      <PillSwitch
                        checked={showThinkingContent}
                        aria-label={t('settings.personalization.showThinkingContent')}
                        onChange={(checked) => {
                          void handleShowThinkingContentToggle(checked)
                        }}
                      />
                    }
                  />
                </SettingsGroup>
                {workspaceCoreApiAvailable && (
                  <SettingsGroup
                    title={t('settings.personalization.group.learning')}
                  >
                    <SettingsRow
                      label={t('settings.personalization.selfLearning')}
                      description={t('settings.personalization.selfLearningHint')}
                      control={
                        <PillSwitch
                          checked={selfLearningEnabled}
                          disabled={applyingSelfLearning}
                          aria-label={t('settings.personalization.selfLearning')}
                          onChange={(checked) => {
                            void handleSelfLearningToggle(checked)
                          }}
                        />
                      }
                    />
                  </SettingsGroup>
                )}
                {(workspaceCoreApiAvailable || memoryManagementEnabled) && (
                  <SettingsGroup
                    title={t('settings.personalization.group.memory')}
                  >
                    {workspaceCoreApiAvailable && (
                      <SettingsRow
                        label={t('settings.personalization.longTermMemory')}
                        description={t('settings.personalization.longTermMemoryHint')}
                        control={
                          <PillSwitch
                            checked={memoryAutoConsolidateEnabled}
                            disabled={applyingMemoryAutoConsolidate}
                            aria-label={t('settings.personalization.longTermMemory')}
                            onChange={(checked) => {
                              void handleMemoryAutoConsolidateToggle(checked)
                            }}
                          />
                        }
                      />
                    )}
                    {memoryManagementEnabled && (
                      <SettingsRow
                        label={t('settings.personalization.resetMemory')}
                        description={t('settings.personalization.resetMemoryHint')}
                        control={
                          <Button
                            variant="danger"
                            disabled={resettingMemory}
                            onClick={() => void handleResetMemory()}
                          >
                            {resettingMemory
                              ? t('settings.personalization.resettingMemory')
                              : t('settings.personalization.resetMemoryButton')}
                          </Button>
                        }
                      />
                    )}
                  </SettingsGroup>
                )}
                {dreamsCapabilityEnabled && (
                  <SettingsGroup
                    title={t('settings.personalization.group.dreams')}
                    headerAction={
                      <div style={{ display: 'flex', alignItems: 'center', gap: '8px', flexWrap: 'wrap', justifyContent: 'flex-end' }}>
                        <Button
                          onClick={() => setActiveSettingsTab('dreams')}
                        >
                          {t('settings.personalization.dreamsManage')}
                        </Button>
                        <Button
                          disabled={dreamsRunDisabled}
                          onClick={() => void handleRunDreamsNow()}
                        >
                          {runningDreams || dreamsStatus?.running === true
                            ? t('settings.personalization.dreamsRunning')
                            : t('settings.personalization.dreamsRunNow')}
                        </Button>
                      </div>
                    }
                  >
                    <SettingsRow
                      label={t('settings.personalization.dreams')}
                      description={t('settings.personalization.dreamsHint')}
                      control={
                        <PillSwitch
                          checked={dreamsEnabled}
                          disabled={applyingDreams || runningDreams}
                          aria-label={t('settings.personalization.dreams')}
                          onChange={(checked) => {
                            void handleDreamsEnabledToggle(checked)
                          }}
                        />
                      }
                    />
                    <SettingsRow
                      label={t('settings.personalization.dreamsAutoApply')}
                      description={t('settings.personalization.dreamsAutoApplyHint')}
                      control={
                        <PillSwitch
                          checked={dreamsAutoApply}
                          disabled={applyingDreams || runningDreams}
                          aria-label={t('settings.personalization.dreamsAutoApply')}
                          onChange={(checked) => {
                            void handleDreamsAutoApplyToggle(checked)
                          }}
                        />
                      }
                    />
                    <SettingsRow
                      label={t('settings.personalization.dreamsInterval')}
                      description={t('settings.personalization.dreamsIntervalHint')}
                      control={
                        <SettingsSelect
                          ariaLabel={t('settings.personalization.dreamsInterval')}
                          value={dreamsInterval}
                          disabled={applyingDreams || runningDreams}
                          onValueChange={(nextInterval) => {
                            void handleDreamsIntervalChange(nextInterval)
                          }}
                          style={{ minWidth: '140px' }}
                          options={dreamsIntervalOptions.map((optionValue) => ({
                            value: optionValue,
                            label: formatDreamsIntervalOption(optionValue, t)
                          }))}
                        />
                      }
                    />
                    <SettingsRow
                      label={t('settings.personalization.dreamsThreadLookback')}
                      description={t('settings.personalization.dreamsThreadLookbackHint')}
                      control={
                        <SettingsSelect
                          ariaLabel={t('settings.personalization.dreamsThreadLookback')}
                          value={String(dreamsThreadLookbackCount)}
                          disabled={applyingDreams || runningDreams}
                          onValueChange={(nextCount) => {
                            void handleDreamsThreadLookbackChange(Number(nextCount))
                          }}
                          style={{ minWidth: '120px' }}
                          options={dreamsThreadLookbackOptions.map((optionValue) => ({
                            value: String(optionValue),
                            label: optionValue
                          }))}
                        />
                      }
                    />
                  </SettingsGroup>
                )}
              </SettingsPanelShell>
              </GeneralPanel>
            )}

            {dreamsCapabilityEnabled && activeSettingsTab === 'dreams' && (
              <GeneralPanel>
              <SettingsPanelShell
                  title={t('settings.dreams.title')}
                  description={
                    <SettingsDescriptionWithLearnMore topic="memory" aboutKey="settings.dreams.title">
                      {t('settings.dreams.description')}
                    </SettingsDescriptionWithLearnMore>
                  }
                  breadcrumb={
                    <SettingsBreadcrumb
                      parentLabel={t('settings.tab.personalization')}
                      currentLabel={t('settings.dreams.title')}
                      onBack={() => setActiveSettingsTab('personalization')}
                    />
                  }
                  action={
                    <IconButton
                      icon={<RefreshIcon size={15} />}
                      label={t('settings.dreams.refresh')}
                      tooltipLabel={t('settings.dreams.refresh')}
                      disabled={dreamRunsLoading}
                      onClick={() => void reloadDreamRuns()}
                    />
                  }
                >
                <SettingsGroup
                  title={t('settings.dreams.runs')}
                  headerAction={
                    <Button
                      variant="danger"
                      disabled={archiveAllDreamRunsDisabled}
                      onClick={() => void handleArchiveAllDreamRuns()}
                    >
                      {t('settings.dreams.archiveAll')}
                    </Button>
                  }
                >
                  {dreamRunsLoading && dreamRuns.length === 0 &&
                    ['58%', '44%', '64%'].map((labelWidth, index) => (
                      <SettingsRow
                        key={`dream-run-skeleton-${index}`}
                        label={
                          <span
                            role={index === 0 ? 'status' : undefined}
                            aria-label={index === 0 ? t('settings.dreams.loading') : undefined}
                          >
                            <Skeleton width={labelWidth} height={13} />
                          </span>
                        }
                        description={<Skeleton width="34%" height={11} />}
                        control={<Skeleton width={99} height={32} radius={8} />}
                      />
                    ))}

                  {!dreamRunsLoading && dreamRuns.length === 0 && (
                    <SettingsRow>
                      <div style={settingsPlaceholderStyle()}>{t('settings.dreams.empty')}</div>
                    </SettingsRow>
                  )}

                  {!dreamRunsLoading && dreamRuns.map((run) => {
                    const runTime = run.endedAt ?? run.startedAt
                    const statusColor = run.status === 'succeeded'
                      ? 'var(--success)'
                      : run.status === 'failed'
                        ? 'var(--error)'
                        : run.status === 'running'
                          ? 'var(--info)'
                          : 'var(--text-secondary)'
                    const running = run.status === 'running'
                    return (
                      <SettingsRow
                        key={run.id}
                        label={
                          <span style={{ display: 'block', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                            {runTime
                              ? new Date(runTime).toLocaleString(locale)
                              : t('settings.personalization.dreamsStatus.unknownTime')}
                          </span>
                        }
                        description={
                          <span style={{ display: 'inline-flex', alignItems: 'center', flexWrap: 'wrap', gap: '6px' }}>
                            <span style={{ color: statusColor }}>
                              {t(`settings.personalization.dreamsStatus.${run.status}`)}
                            </span>
                            <span aria-hidden>·</span>
                            <span>{t('settings.dreams.threadCount', { count: run.processedThreadCount })}</span>
                          </span>
                        }
                        control={
                          <div style={{ display: 'inline-flex', alignItems: 'center', gap: '8px' }}>
                            <ActionTooltip
                              label={dashboardUrl ? t('settings.dreams.openReview') : t('settings.dreams.dashboardUnavailable')}
                            >
                              <Button
                                disabled={!dashboardUrl || running}
                                onClick={() => void openDreamReview(run.id)}
                              >
                                {t('settings.dreams.openReview')}
                              </Button>
                            </ActionTooltip>
                            <IconButton
                              icon={<Archive size={14} aria-hidden />}
                              label={t('settings.dreams.archive')}
                              tooltipLabel={t('settings.dreams.archive')}
                              size={24}
                              radius={8}
                              className="dc-thread-list-icon-button"
                              disabled={running || dreamsArchiveBusy}
                              onClick={() => void handleArchiveDreamRun(run)}
                            />
                          </div>
                        }
                      />
                    )
                  })}
                </SettingsGroup>
              </SettingsPanelShell>
              </GeneralPanel>
            )}

            {activeSettingsTab === 'connection' && (
              <ConnectionPanel>
              <SettingsPanelShell
                title={t('settings.tab.connection')}
                description={
                  <SettingsDescriptionWithLearnMore topic="connection" aboutKey="settings.tab.connection">
                    {t('settings.connection.description')}
                  </SettingsDescriptionWithLearnMore>
                }
              >
                <SettingsGroup title={t('settings.group.connectionMode')}>
                  <SettingsRow
                    orientation="block"
                    label={t('settings.connectionMode')}
                    description={t('settings.connectionModeHint')}
                    htmlFor="settings-connection-mode"
                  >
                    <SettingsSelect
                      id="settings-connection-mode"
                      value={connectionMode}
                      onValueChange={(mode) => {
                        setConnectionMode(mode as ConnectionMode)
                      }}
                      options={[
                        { value: 'local', label: t('settings.connectionMode.local') },
                        { value: 'remote', label: t('settings.connectionMode.remote') }
                      ]}
                    />
                  </SettingsRow>

                  {activeRemoteStackConnection && (
                    <SettingsRow
                      orientation="block"
                      label={t('settings.remoteStackManaged.title')}
                    >
                      <div
                        style={{
                          border: '1px solid var(--border-default)',
                          borderLeft: '3px solid var(--accent-blue)',
                          borderRadius: '8px',
                          background: 'var(--bg-secondary)',
                          color: 'var(--text-secondary)',
                          fontSize: '12px',
                          lineHeight: 1.5,
                          padding: '10px 12px'
                        }}
                      >
                        {t('settings.remoteStackManaged.description')}
                      </div>
                    </SettingsRow>
                  )}

                  {manualRemoteConnection && (
                    <SettingsRow
                      orientation="block"
                      label={t('settings.remoteUrl')}
                      htmlFor="settings-remote-url"
                    >
                      <Input
                        id="settings-remote-url"
                        value={remoteUrl}
                        onChange={(e) => setRemoteUrl(e.target.value)}
                        placeholder="ws://127.0.0.1:9100/ws"
                        mono
                      />
                      {remoteConnectionValidation && !remoteConnectionValidation.ok && (
                        <div style={{ ...settingsErrorTextStyle(false), marginTop: '6px' }}>
                          {t(REMOTE_URL_ERROR_KEYS[remoteConnectionValidation.code])}
                        </div>
                      )}
                      <label style={{ ...sectionLabelStyle(), marginTop: '10px' }}>
                        {t('settings.remoteToken')}
                      </label>
                      <SecretInput
                        value={remoteToken}
                        onChange={setRemoteToken}
                        placeholder={t('settings.remoteTokenPlaceholder')}
                        mono
                      />
                    </SettingsRow>
                  )}
                </SettingsGroup>

                <SettingsGroup
                  title={t('settings.group.localAppServer')}
                  description={connectionMode === 'remote'
                    ? t('settings.localAppServerRemoteHint')
                    : t('settings.binaryHint')}
                  style={connectionMode === 'remote'
                    ? { opacity: 0.55, pointerEvents: 'none' }
                    : undefined}
                >
                  <SettingsRow orientation="block">
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '10px', width: '100%' }}>
                      {(['bundled', 'path', 'custom'] as BinarySource[]).map((source) => {
                        const active = binarySource === source
                        const titleKey =
                          source === 'bundled'
                            ? 'settings.binarySource.bundled'
                            : source === 'path'
                              ? 'settings.binarySource.path'
                              : 'settings.binarySource.custom'
                        const descKey =
                          source === 'bundled'
                            ? 'settings.binarySource.bundledDesc'
                            : source === 'path'
                              ? 'settings.binarySource.pathDesc'
                              : 'settings.binarySource.customDesc'
                        const showResolved = !resolvingBinary && !!resolvedBinaryPath
                        const showError = !resolvingBinary && !resolvedBinaryPath
                        const errorText =
                          source === 'bundled'
                            ? t('settings.binaryNotFound.bundled')
                            : source === 'path'
                              ? t('settings.binaryNotFound.path')
                              : t('settings.binaryNotFound.custom')
                        return (
                          <SelectionCard
                            key={source}
                            name="settings-binary-source"
                            value={source}
                            active={active}
                            onSelect={() => setBinarySource(source)}
                            title={t(titleKey)}
                            description={t(descKey)}
                            resolvedBadge={
                              showResolved ? <ResolvedPill label={t('settings.binaryResolved')} /> : undefined
                            }
                            errorHint={showError ? errorText : undefined}
                            extra={
                              source === 'custom' ? (
                                <InputWithAction
                                  id="settings-binary-path"
                                  inputRef={inputRef}
                                  mono
                                  value={binaryPath}
                                  onChange={(e) => setBinaryPath(e.target.value)}
                                  placeholder={t('settings.binaryPlaceholder')}
                                  onInputClick={(e) => e.stopPropagation()}
                                  actionIcon={<FolderIcon size={16} />}
                                  actionLabel={t('settings.binaryBrowse')}
                                  onAction={(e) => {
                                    e.stopPropagation()
                                    void handlePickBinary()
                                  }}
                                />
                              ) : undefined
                            }
                          />
                        )
                      })}
                      {resolvingBinary && (
                        <div style={settingsHintStyle(false)}>
                          {t('settings.binaryResolving')}
                        </div>
                      )}
                    </div>
                  </SettingsRow>

                  {connectionDirty && (
                    <SettingsRow
                      description={t(connectionMode === 'remote'
                        ? 'settings.pendingChanges.connectionRemote'
                        : 'settings.pendingChanges.connection')}
                      control={
                        <Button
                          onClick={() => {
                            if (!baselineConnection) return
                            setBinarySource(baselineConnection.binarySource)
                            setBinaryPath(baselineConnection.binaryPath)
                            setConnectionMode(baselineConnection.connectionMode)
                            setWsHost(baselineConnection.wsHost)
                            setWsPort(baselineConnection.wsPort)
                            setRemoteUrl(baselineConnection.remoteUrl)
                            setRemoteToken(baselineConnection.remoteToken)
                          }}
                          disabled={restartingAppServer || saving}
                        >
                          {t('settings.llm.revert')}
                        </Button>
                      }
                    />
                  )}
                </SettingsGroup>
              </SettingsPanelShell>
              </ConnectionPanel>
            )}

            {activeSettingsTab === 'servers' && <ServersPanel />}

            {activeSettingsTab === 'browserUse' && (
              <GeneralPanel>
              <SettingsPanelShell
                  title={t('settings.browserUse.pageTitle')}
                  description={t('settings.browserUse.pageDescription')}
                >
                {pluginManagementEnabled && browserUsePlugin && (
                  <SettingsGroup title={t('settings.browserUse.plugin')}>
                    <SettingsRow orientation="block">
                      <PluginCatalogItem
                        plugin={browserUsePlugin}
                        tryLabel={t('plugins.tryInChat')}
                        installLabel={t('plugins.install')}
                        onTryInChat={handleTryBrowserUseInChat}
                        onInstall={() => setBrowserUseInstallOpen(true)}
                        style={{ height: 54, padding: '0 4px' }}
                      />
                    </SettingsRow>
                  </SettingsGroup>
                )}

                <SettingsGroup title={t('settings.browserUse.browsingData')}>
                  <SettingsRow
                    label={t('settings.browserUse.cookies')}
                    description={t('settings.browserUse.cookiesHint')}
                    control={
                      <Button
                        onClick={() => void handleClearBrowserUseCookies()}
                        disabled={clearingBrowserCookies}
                      >
                        {clearingBrowserCookies ? t('settings.saving') : t('settings.browserUse.clearCookies')}
                      </Button>
                    }
                  />
                </SettingsGroup>

                {browserUsePluginReady && (
                  <>
                    <SettingsGroup title={t('settings.browserUse.permissions')}>
                      <SettingsRow
                        label={t('settings.browserUse.approval')}
                        description={t('settings.browserUse.approvalHint')}
                        control={
                          <SettingsSelect
                            value={browserUseApprovalMode}
                            ariaLabel={t('settings.browserUse.approval')}
                            onValueChange={(mode) => void handleBrowserUseApprovalModeChange(mode as BrowserUseApprovalMode)}
                            style={{ width: SETTINGS_SELECT_WIDTH }}
                            options={[
                              { value: 'alwaysAsk', label: t('settings.browserUse.approval.alwaysAsk') },
                              { value: 'askUnknown', label: t('settings.browserUse.approval.askUnknown') },
                              { value: 'neverAsk', label: t('settings.browserUse.approval.neverAsk') }
                            ]}
                          />
                        }
                      />
                    </SettingsGroup>

                    <SettingsGroup
                      title={t('settings.browserUse.blockedDomains')}
                      description={t('settings.browserUse.blockedDomainsHint')}
                      headerAction={
                        <Button onClick={() => openBrowserUseDomainDialog('blocked')}>
                          {t('settings.browserUse.add')}
                        </Button>
                      }
                    >
                      {browserUseBlockedDomains.length === 0 ? (
                        <SettingsRow>
                          <div style={{ ...settingsPlaceholderStyle(), width: '100%', textAlign: 'center' }}>
                            {t('settings.browserUse.noBlockedDomains')}
                          </div>
                        </SettingsRow>
                      ) : browserUseBlockedDomains.map((domain) => (
                        <SettingsRow
                          key={domain}
                          label={domain}
                          control={
                            <ActionTooltip label={t('settings.browserUse.remove')} placement="top">
                              <Button
                                variant="danger"
                                size="icon"
                                onClick={() => void handleRemoveBrowserUseDomain('blocked', domain)}
                                aria-label={t('settings.browserUse.remove')}
                              >
                                <Trash2 size={14} strokeWidth={2} aria-hidden />
                              </Button>
                            </ActionTooltip>
                          }
                        />
                      ))}
                    </SettingsGroup>

                    <SettingsGroup
                      title={t('settings.browserUse.allowedDomains')}
                      description={t('settings.browserUse.allowedDomainsHint')}
                      headerAction={
                        <Button onClick={() => openBrowserUseDomainDialog('allowed')}>
                          {t('settings.browserUse.add')}
                        </Button>
                      }
                    >
                      {browserUseAllowedDomains.length === 0 ? (
                        <SettingsRow>
                          <div style={{ ...settingsPlaceholderStyle(), width: '100%', textAlign: 'center' }}>
                            {t('settings.browserUse.noAllowedDomains')}
                          </div>
                        </SettingsRow>
                      ) : browserUseAllowedDomains.map((domain) => (
                        <SettingsRow
                          key={domain}
                          label={domain}
                          control={
                            <ActionTooltip label={t('settings.browserUse.remove')} placement="top">
                              <Button
                                variant="danger"
                                size="icon"
                                onClick={() => void handleRemoveBrowserUseDomain('allowed', domain)}
                                aria-label={t('settings.browserUse.remove')}
                              >
                                <Trash2 size={14} strokeWidth={2} aria-hidden />
                              </Button>
                            </ActionTooltip>
                          }
                        />
                      ))}
                    </SettingsGroup>
                  </>
                )}
              </SettingsPanelShell>
              {browserUsePlugin && browserUseInstallOpen && (
                <PluginInstallDialog
                  plugin={browserUsePlugin}
                  installing={browserUseInstalling}
                  onClose={() => setBrowserUseInstallOpen(false)}
                  onInstall={() => void handleInstallBrowserUsePlugin()}
                />
              )}
              </GeneralPanel>
            )}

            {activeSettingsTab === 'computerControl' && (
              <GeneralPanel>
                {!chromeDetailOpen ? (
                  <SettingsPanelShell
                      title={t('settings.chrome.pageTitle')}
                      description={t('settings.chrome.pageDescription')}
                    >
                    <SettingsGroup title={t('settings.chrome.control')}>
                      {!pluginManagementEnabled && (
                        <SettingsRow>
                          <div style={{ width: '100%', fontSize: 12, color: 'var(--text-dimmed)' }}>
                            {t('plugins.unavailable')}
                          </div>
                        </SettingsRow>
                      )}
                      {pluginManagementEnabled && !chromePlugin && (
                        <SettingsRow>
                          <div style={{ width: '100%', fontSize: 12, color: 'var(--text-dimmed)' }}>
                            {t('plugins.loading')}
                          </div>
                        </SettingsRow>
                      )}
                      {pluginManagementEnabled && chromePlugin && !chromePlugin.installed && (
                        <SettingsRow orientation="block">
                          <PluginCatalogItem
                            plugin={chromePlugin}
                            tryLabel={t('plugins.tryInChat')}
                            installLabel={t('plugins.install')}
                            onInstall={() => setChromeInstallOpen(true)}
                            style={{ height: 54, padding: '0 4px' }}
                          />
                        </SettingsRow>
                      )}
                      {pluginManagementEnabled && chromePlugin?.installed && (
                        <SettingsRow>
                          <div style={{ display: 'flex', alignItems: 'center', gap: 12, minWidth: 0, flex: 1 }}>
                            <PluginIcon plugin={chromePlugin} size={38} />
                            <div style={{ display: 'flex', flexDirection: 'column', minWidth: 0, flex: 1 }}>
                              <strong style={{ fontSize: 13, color: 'var(--text-primary)' }}>
                                {pluginTitle(chromePlugin)}
                              </strong>
                              <span style={{ fontSize: 12, color: 'var(--text-dimmed)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                                {pluginSubtitle(chromePlugin)}
                              </span>
                            </div>
                          </div>
                          <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexShrink: 0 }}>
                            <Button onClick={() => setChromeDetailOpen(true)}>
                              {t('settings.chrome.manage')}
                            </Button>
                            <PillSwitch
                              checked={chromePlugin.enabled}
                              disabled={chromeToggling}
                              onChange={(checked) => void handleToggleChromePlugin(checked)}
                              aria-label={t('settings.chrome.toggleAria')}
                            />
                          </div>
                        </SettingsRow>
                      )}
                    </SettingsGroup>

                  </SettingsPanelShell>
                ) : (
                  <SettingsPanelShell
                      title={t('settings.chrome.detailTitle')}
                      description={chromePlugin ? pluginSubtitle(chromePlugin) || t('settings.chrome.pageDescription') : t('settings.chrome.pageDescription')}
                      breadcrumb={
                        <SettingsBreadcrumb
                          parentLabel={t('settings.chrome.pageTitle')}
                          currentLabel={t('settings.chrome.detailTitle')}
                          onBack={() => setChromeDetailOpen(false)}
                        />
                      }
                    >
                    <SettingsGroup title={t('settings.chrome.connectionStatus')} flush>
                      <div
                        style={{
                          display: 'flex',
                          alignItems: 'center',
                          justifyContent: 'space-between',
                          gap: 16,
                          flexWrap: 'wrap'
                        }}
                      >
                      <div style={{ display: 'flex', alignItems: 'center', gap: 14, flex: '1 1 260px', minWidth: 0 }}>
                        {chromePlugin && <PluginIcon plugin={chromePlugin} size={38} />}
                        <div style={{ flex: 1, minWidth: 0 }}>
                          <div>
                            <ChromeStatusPill label={chromeSetup.label} tone={chromeSetup.tone} />
                          </div>
                        </div>
                      </div>
                      <div style={chromeActionToolbarStyle()}>
                        <IconButton
                          icon={<RefreshIcon size={15} />}
                          label={t('settings.chrome.refreshStatus')}
                          tooltipLabel={t('settings.chrome.refreshStatus')}
                          tooltipPlacement="top"
                          onClick={() => void reloadChromeSetupStatus()}
                          disabled={chromeSetupLoading}
                          disabledReason={chromeSetupLoading ? t('settings.loading') : undefined}
                        />
                        <IconButton
                          icon={<OpenInBrowserIcon size={15} />}
                          label={t('settings.chrome.openChrome')}
                          tooltipLabel={t('settings.chrome.openChrome')}
                          tooltipPlacement="top"
                          onClick={() => void handleOpenChrome()}
                          disabled={chromeOpening}
                          disabledReason={chromeOpening ? t('settings.chrome.opening') : undefined}
                        />
                        {chromeSetupStatus && !setupResultOk(chromeSetupStatus.extension) && (
                          <IconButton
                            icon={<ExtensionsIcon size={15} />}
                            label={t('settings.chrome.openExtensions')}
                            tooltipLabel={t('settings.chrome.openExtensions')}
                            tooltipPlacement="top"
                            onClick={() => void handleOpenChrome(chromeExtensionManagementUrl(chromeSetupStatus))}
                            disabled={chromeOpening}
                            disabledReason={chromeOpening ? t('settings.chrome.opening') : undefined}
                          />
                        )}
                        <IconButton
                          icon={<WrenchIcon size={15} />}
                          label={chromeNativeHostActionLabel(chromeSetupStatus, false, t)}
                          tooltipLabel={chromeNativeHostActionLabel(chromeSetupStatus, false, t)}
                          tooltipPlacement="top"
                          onClick={() => void handleInstallChromeNativeHost()}
                          disabled={chromeNativeHostInstalling}
                          disabledReason={chromeNativeHostInstalling ? t('settings.chrome.installingNativeHost') : undefined}
                        />
                      </div>
                    </div>
                    </SettingsGroup>

                  </SettingsPanelShell>
                )}
              {chromePlugin && chromeInstallOpen && (
                <PluginInstallDialog
                  plugin={chromePlugin}
                  installing={chromeInstalling}
                  onClose={() => setChromeInstallOpen(false)}
                  onInstall={() => void handleInstallChromePlugin()}
                />
              )}
              </GeneralPanel>
            )}

            {activeSettingsTab === 'usage' && (
              <UsagePanel>
              <SettingsPanelShell
                title={t('settings.tab.usage')}
                description={t('settings.usage.description')}
              >
                <UsageOverview />
                <SettingsGroup
                  title={t('settings.usage.dashboardTitle')}
                  description={t('settings.usage.dashboardHint')}
                  headerAction={
                    <IconButton
                      icon={<OpenInBrowserIcon size={16} />}
                      label={t('settings.openDashboard')}
                      onClick={() => {
                        if (dashboardUrl) void window.api.shell.openExternal(dashboardUrl)
                      }}
                      disabled={!dashboardUrl}
                    />
                  }
                >
                  <SettingsRow>
                    <div style={settingsPlaceholderStyle()}>
                      {dashboardUrl ? dashboardUrl : t('settings.usage.dashboardUnavailable')}
                    </div>
                  </SettingsRow>
                </SettingsGroup>
              </SettingsPanelShell>
              </UsagePanel>
            )}

            {activeSettingsTab === 'sourceControl' && (
              <SourceControlPanel workspacePath={workspacePath} />
            )}

            {activeSettingsTab === 'hooks' && <HooksPanel />}

            {activeSettingsTab === 'mcp' && (
              <McpPanel>
              <SettingsPanelShell
                title={t('settings.mcp.title')}
                description={
                  <SettingsDescriptionWithLearnMore topic="mcp" aboutKey="settings.mcp.title">
                    {mcpEnabled && editingServerName !== null
                      ? t('settings.mcp.editIntro')
                      : t('settings.mcp.description')}
                  </SettingsDescriptionWithLearnMore>
                }
                breadcrumb={
                  mcpEnabled && editingServerName !== null ? (
                    <SettingsBreadcrumb
                      parentLabel={t('settings.mcp.title')}
                      currentLabel={editingServerName === '__new__'
                        ? t('settings.mcp.addTitle')
                        : t('settings.mcp.editTitle')}
                      onBack={cancelMcpEdit}
                    />
                  ) : undefined
                }
                action={
                  !mcpEnabled
                    ? undefined
                    : editingServerName === null ? (
                      <Button
                        variant="primary"
                        onClick={() => startMcpDraft()}
                        iconLeft={<Plus size={14} aria-hidden="true" />}
                      >
                        {t('settings.mcp.addServer')}
                      </Button>
                    ) : undefined
                }
                headerChildren={
                  mcpSavedHint && editingServerName === null ? (
                    <div style={{ fontSize: '12px', color: 'var(--success)', marginTop: '6px' }}>
                      {mcpSavedHint}
                    </div>
                  ) : undefined
                }
              >
                {!mcpEnabled && (
                  <SettingsGroup>
                    <SettingsRow>
                    <div style={{ fontSize: '14px', color: 'var(--text-primary)' }}>
                      {t('settings.mcp.unsupported')}
                    </div>
                    </SettingsRow>
                  </SettingsGroup>
                )}

                {mcpEnabled && editingServerName === null && (
                  <>
                    {mcpLoading && (
                      <SettingsGroup title={t('settings.group.servers')}>
                        <SettingsRow>
                        <div style={{ fontSize: '13px', color: 'var(--text-dimmed)' }}>
                          {t('settings.mcp.loading')}
                        </div>
                        </SettingsRow>
                      </SettingsGroup>
                    )}

                    {!mcpLoading && mcpError && (
                      <SettingsGroup title={t('settings.group.servers')}>
                        <SettingsRow>
                        <div style={{ fontSize: '13px', color: '#f85149' }}>{mcpError}</div>
                        </SettingsRow>
                      </SettingsGroup>
                    )}

                    {!mcpLoading && !mcpError && mergedMcpServers.length === 0 && (
                      <SettingsGroup title={t('settings.group.servers')}>
                        <SettingsRow>
                          <div style={settingsPlaceholderStyle()}>{t('settings.mcp.empty.title')}</div>
                        </SettingsRow>
                      </SettingsGroup>
                    )}

                    {!mcpLoading && !mcpError && mergedMcpServers.length > 0 && (
                      <SettingsGroup title={t('settings.group.servers')} flush>
                      <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                      {mergedMcpServers.map((server) => {
                        const status = mcpStatuses[server.name.trim().toLowerCase()]
                        const tone = getStatusTone(t, status)
                        const isToggling = togglingServerName === server.name
                        const isPluginManaged = isPluginManagedMcpServer(server, mcpOriginsEnabled)
                        const transportLabel =
                          server.transport === 'stdio'
                            ? t('settings.mcp.transport.stdio')
                            : t('settings.mcp.transport.http')
                        const toolCountLabel =
                          typeof status?.toolCount === 'number'
                            ? t('settings.mcp.toolsCountSuffix', { count: status.toolCount }).replace(/^ · /, '')
                            : null
                        return (
                          <div
                            key={server.name}
                            role={isPluginManaged ? undefined : 'button'}
                            tabIndex={isPluginManaged ? undefined : 0}
                            aria-label={isPluginManaged ? `MCP server ${server.name}` : `Edit MCP server ${server.name}`}
                            onClick={isPluginManaged ? undefined : () => startMcpDraft(server)}
                            onKeyDown={(event) => {
                              if (isPluginManaged) return
                              if (event.key === 'Enter' || event.key === ' ') {
                                event.preventDefault()
                                startMcpDraft(server)
                              }
                            }}
                            style={{
                              ...cardStyle(),
                              display: 'flex',
                              alignItems: 'center',
                              justifyContent: 'space-between',
                              gap: '16px',
                              cursor: isPluginManaged ? 'default' : 'pointer',
                              textAlign: 'left',
                              opacity: isToggling ? 0.7 : 1
                            }}
                          >
                            <div style={{ flex: 1, minWidth: 0 }}>
                              <div style={{ fontSize: '15px', fontWeight: 600, color: 'var(--text-primary)' }}>
                                {server.name}
                              </div>
                              <div
                                style={{
                                  marginTop: '4px',
                                  fontSize: '12px',
                                  color: 'var(--text-dimmed)',
                                  display: 'flex',
                                  flexWrap: 'wrap',
                                  alignItems: 'center',
                                  gap: '6px'
                                }}
                              >
                                <span>{transportLabel}</span>
                                {isPluginManaged && (
                                  <>
                                    <span aria-hidden>·</span>
                                    <span style={mcpSourcePillStyle()}>
                                      {mcpPluginSourceLabel(server, t)}
                                    </span>
                                  </>
                                )}
                                <span aria-hidden>·</span>
                                <span style={{ color: tone.color, fontWeight: 500 }}>{tone.label}</span>
                                {toolCountLabel && (
                                  <>
                                    <span aria-hidden>·</span>
                                    <span>{toolCountLabel}</span>
                                  </>
                                )}
                              </div>
                              {status?.lastError && (
                                <div style={{ fontSize: '12px', color: '#f85149', marginTop: '8px' }}>
                                  {status.lastError}
                                </div>
                              )}
                            </div>
                            <div style={{ display: 'inline-flex', alignItems: 'center', gap: '8px', flexShrink: 0 }}>
                            {status?.authStatus === 'notLoggedIn' && (
                              <Button
                                disabled={authenticatingMcpName === server.name}
                                onClick={(event) => {
                                  event.stopPropagation()
                                  void handleMcpOAuthLogin(server)
                                }}
                              >
                                {authenticatingMcpName === server.name
                                  ? t('settings.mcp.authenticating')
                                  : status.failureReason === 'reauthenticationRequired'
                                    ? t('settings.mcp.reauthenticate')
                                    : t('settings.mcp.authenticate')}
                              </Button>
                            )}
                            {isPluginManaged ? (
                              <Button
                                onClick={(event) => {
                                  event.stopPropagation()
                                  void handleViewPluginMcp(server)
                                }}
                              >
                                {t('settings.mcp.viewPlugin')}
                              </Button>
                            ) : (
                              <span
                                onClick={(event) => event.stopPropagation()}
                                onKeyDown={(event) => event.stopPropagation()}
                                style={{ flexShrink: 0, display: 'inline-flex' }}
                              >
                                <PillSwitch
                                  checked={server.enabled}
                                  disabled={isToggling}
                                  onChange={(checked) => {
                                    void handleMcpQuickToggle(server, checked)
                                  }}
                                  aria-label={`Toggle MCP server ${server.name}`}
                                />
                              </span>
                            )}
                            </div>
                          </div>
                        )
                      })}
                      </div>
                      </SettingsGroup>
                    )}
                  </>
                )}

                {mcpEnabled && editingServerName !== null && (
                  <div style={{ display: 'flex', flexDirection: 'column', gap: '14px' }}>
                    <SettingsGroup title={t('settings.group.identity')} flush>
                      <label style={sectionLabelStyle()}>{t('settings.mcp.field.name')}</label>
                      <Input
                        value={mcpDraft.name}
                        onChange={(e) => setMcpDraft((prev) => ({ ...prev, name: e.target.value }))}
                        placeholder={t('settings.mcp.field.namePlaceholder')}
                      />
                      <div style={{ marginTop: '12px', display: 'flex', gap: '8px' }}>
                        {(['stdio', 'streamableHttp'] as const).map((transport) => {
                          const active = mcpDraft.transport === transport
                          return (
                            <button
                              key={transport}
                              type="button"
                              onClick={() =>
                                setMcpDraft((prev) => ({
                                  ...prev,
                                  transport: transport as McpTransport
                                }))
                              }
                              style={{
                                flex: 1,
                                padding: '8px 12px',
                                borderRadius: '8px',
                                border: active ? '1px solid var(--accent)' : '1px solid var(--border-default)',
                                background: active ? 'var(--bg-tertiary)' : 'transparent',
                                color: 'var(--text-primary)',
                                fontSize: '13px',
                                fontWeight: 600,
                                cursor: 'pointer'
                              }}
                            >
                              {transport === 'stdio'
                                ? t('settings.mcp.transport.stdio')
                                : t('settings.mcp.transport.http')}
                            </button>
                          )
                        })}
                      </div>
                    </SettingsGroup>

                    {mcpDraft.transport === 'stdio' && (
                      <>
                        <SettingsGroup title={t('settings.group.command')} flush>
                          <div style={{ display: 'flex', flexDirection: 'column', gap: '14px' }}>
                            <div>
                              <label style={sectionLabelStyle()}>{t('settings.mcp.field.command')}</label>
                              <Input
                                value={mcpDraft.command ?? ''}
                                onChange={(e) => setMcpDraft((prev) => ({ ...prev, command: e.target.value }))}
                                placeholder="npx"
                                mono
                              />
                            </div>

                            <div>
                              <div style={sectionLabelStyle()}>{t('settings.mcp.field.args')}</div>
                              <EditableValueList
                                rows={argRows}
                                setRows={setArgRows}
                                placeholder={t('settings.mcp.field.argsPlaceholder')}
                              />
                            </div>

                            <div>
                              <label style={sectionLabelStyle()}>{t('settings.mcp.field.cwd')}</label>
                              <Input
                                value={mcpDraft.cwd ?? ''}
                                onChange={(e) => setMcpDraft((prev) => ({ ...prev, cwd: e.target.value }))}
                                placeholder="~/code"
                                mono
                              />
                            </div>
                          </div>
                        </SettingsGroup>

                        <SettingsGroup title={t('settings.group.environment')} flush>
                          <div style={{ display: 'flex', flexDirection: 'column', gap: '14px' }}>
                            <div>
                              <div style={sectionLabelStyle()}>{t('settings.mcp.field.env')}</div>
                              <EditableKeyValueList
                                rows={envRows}
                                setRows={setEnvRows}
                                keyPlaceholder={t('settings.mcp.keyPlaceholder')}
                                valuePlaceholder={t('settings.mcp.valuePlaceholder')}
                              />
                            </div>

                            <div>
                              <div style={sectionLabelStyle()}>{t('settings.mcp.field.envForwarding')}</div>
                              <EditableValueList
                                rows={envVarRows}
                                setRows={setEnvVarRows}
                                placeholder={t('settings.mcp.field.envForwardingPlaceholder')}
                              />
                            </div>
                          </div>
                        </SettingsGroup>
                      </>
                    )}

                    {mcpDraft.transport === 'streamableHttp' && (
                      <SettingsGroup title={t('settings.group.http')} flush>
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '14px' }}>
                        <div>
                          <label style={sectionLabelStyle()}>{t('settings.mcp.field.url')}</label>
                          <Input
                            value={mcpDraft.url ?? ''}
                            onChange={(e) => setMcpDraft((prev) => ({ ...prev, url: e.target.value }))}
                            placeholder="https://example.com/mcp"
                            mono
                          />
                        </div>

                        <div>
                          <label style={sectionLabelStyle()}>{t('settings.mcp.field.bearerEnv')}</label>
                          <Input
                            value={mcpDraft.bearerTokenEnvVar ?? ''}
                            onChange={(e) =>
                              setMcpDraft((prev) => ({ ...prev, bearerTokenEnvVar: e.target.value }))
                            }
                            placeholder={t('settings.mcp.field.bearerEnvPlaceholder')}
                            mono
                          />
                        </div>

                        <div>
                          <div style={sectionLabelStyle()}>{t('settings.mcp.field.httpHeaders')}</div>
                          <EditableKeyValueList
                            rows={httpHeaderRows}
                            setRows={setHttpHeaderRows}
                            keyPlaceholder={t('settings.mcp.headerPlaceholder')}
                            valuePlaceholder={t('settings.mcp.valuePlaceholder')}
                          />
                        </div>

                        <div>
                          <div style={sectionLabelStyle()}>{t('settings.mcp.field.envHeaders')}</div>
                          <EditableKeyValueList
                            rows={envHttpHeaderRows}
                            setRows={setEnvHttpHeaderRows}
                            keyPlaceholder={t('settings.mcp.headerPlaceholder')}
                            valuePlaceholder={t('settings.mcp.field.envForwardingPlaceholder')}
                          />
                        </div>
                        </div>
                      </SettingsGroup>
                    )}

                    {mcpTestResult && (
                      <div style={cardStyle()}>
                        <div
                          style={{
                            fontSize: '13px',
                            fontWeight: 600,
                            color: mcpTestResult.success ? '#3fb950' : '#f85149'
                          }}
                        >
                          {mcpTestResult.success ? t('settings.mcp.testSuccess') : t('settings.mcp.testFailed')}
                        </div>
                        {typeof mcpTestResult.toolCount === 'number' && (
                          <div style={{ ...settingsPlaceholderStyle(), marginTop: '4px' }}>
                            {t('settings.mcp.toolsDiscovered', { count: mcpTestResult.toolCount })}
                          </div>
                        )}
                        {mcpTestResult.errorMessage && (
                          <div style={{ ...settingsPlaceholderStyle(), marginTop: '4px' }}>
                            {mcpTestResult.errorMessage}
                          </div>
                        )}
                      </div>
                    )}

                    <div style={{ display: 'flex', justifyContent: 'space-between', gap: '12px' }}>
                      <div>
                        {editingServerName !== '__new__' && (
                          <Button
                            variant="danger"
                            onClick={() => {
                              void handleMcpDelete()
                            }}
                            disabled={deletingMcp || savingMcp}
                          >
                            {deletingMcp ? t('settings.mcp.deleting') : t('settings.mcp.delete')}
                          </Button>
                        )}
                      </div>
                      <div style={{ display: 'flex', gap: '8px' }}>
                        <Button
                          onClick={() => {
                            void handleMcpTest()
                          }}
                          disabled={testingMcp || savingMcp}
                        >
                          {testingMcp ? t('settings.mcp.testing') : t('settings.mcp.test')}
                        </Button>
                        <Button
                          variant="primary"
                          onClick={() => {
                            void handleMcpSave()
                          }}
                          disabled={savingMcp || deletingMcp}
                        >
                          {savingMcp ? t('settings.mcp.saving') : t('settings.mcp.save')}
                        </Button>
                      </div>
                    </div>
                  </div>
                )}
              </SettingsPanelShell>
              </McpPanel>
            )}

            {activeSettingsTab === 'archivedThreads' && (
              <ArchivedThreadsSettingsView
                workspacePath={identityWorkspacePath || workspacePath}
                onThreadListRefreshRequested={onThreadListRefreshRequested}
              />
            )}

            {activeSettingsTab === 'subAgents' && (
              <SubAgentsPanel
                enabled={subAgentEnabled}
                refreshTick={subAgentRefreshTick}
              />
            )}
            {activeExtensionSettings && (
              <DesktopExtensionSettingsPanel entry={activeExtensionSettings} />
            )}
        </div>
      </main>
      {browserUseDomainTarget && (
        <div
          role="dialog"
          aria-modal="true"
          style={{
            position: 'fixed',
            inset: 0,
            zIndex: 10000,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            background: 'var(--overlay-scrim)'
          }}
        >
          <div
            style={{
              width: '420px',
              maxWidth: 'calc(100vw - 48px)',
              border: '1px solid var(--border-default)',
              borderRadius: '12px',
              background: 'var(--bg-secondary)',
              boxShadow: 'var(--shadow-level-3)',
              padding: '22px'
            }}
          >
            <h2 style={{ margin: 0, fontSize: '16px', fontWeight: 700, color: 'var(--text-primary)' }}>
              {browserUseDomainTarget === 'blocked'
                ? t('settings.browserUse.addBlockedDomain')
                : t('settings.browserUse.addAllowedDomain')}
            </h2>
            <p style={{ margin: '8px 0 14px', fontSize: '13px', lineHeight: 1.5, color: 'var(--text-secondary)' }}>
              {browserUseDomainTarget === 'blocked'
                ? t('settings.browserUse.addBlockedDomainHint')
                : t('settings.browserUse.addAllowedDomainHint')}
            </p>
            <Input
              value={browserUseDomainDraft}
              onChange={(e) => {
                setBrowserUseDomainDraft(e.target.value)
                setBrowserUseDomainError('')
              }}
              onKeyDown={(e) => {
                if (e.key === 'Enter') void handleAddBrowserUseDomain()
                if (e.key === 'Escape') setBrowserUseDomainTarget(null)
              }}
              placeholder="example.com"
              autoFocus
              mono
            />
            {browserUseDomainError && (
              <div style={{ marginTop: '8px', fontSize: '12px', color: 'var(--error)' }}>
                {browserUseDomainError}
              </div>
            )}
            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '8px', marginTop: '18px' }}>
              <Button onClick={() => setBrowserUseDomainTarget(null)}>
                {t('settings.browserUse.cancel')}
              </Button>
              <Button variant="primary" onClick={() => void handleAddBrowserUseDomain()}>
                {t('settings.browserUse.add')}
              </Button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
