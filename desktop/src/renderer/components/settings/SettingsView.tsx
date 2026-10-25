import { normalizeProviderList, type ProviderInfoWire } from './providerInfo'
import { createProvider, deleteProvider, ProviderExistsError, providerIdExists, updateProvider } from './providerConfigWrites'
import { BrowserDownloadsSettings } from './BrowserDownloadsSettings'
import { BrowserDownloadHistory } from './BrowserDownloadHistory'
import { BrowserUseDomainDialog } from './BrowserUseDomainDialog'
import { ProviderEditor, type ProviderTestResultWire } from './providers/ProviderEditor'
import {
  createProviderDraft,
  isStoredApiKey,
  providerConnectionMode,
  providerDetails,
  providerDraftFromInfo,
  providerFieldsFromDraft,
  providerProblem,
  providerUsable,
  type ProviderDraft,
  type ProviderEditorId
} from './providers/providerDraft'
import { Suspense, useCallback, useEffect, useMemo, useRef, useState, type CSSProperties, type JSX } from 'react'
import {
  Hand,
  Image as ImageIcon,
  MessageCircle,
  OctagonAlert,
  Pencil,
  Plus,
  Trash2
} from 'lucide-react'
import { addToast } from '../../stores/toastStore'
import { normalizeLocale, SUPPORTED_LOCALES, type AppLocale } from '../../../shared/locales'
import { useSetUiLocale, useT } from '../../contexts/LocaleContext'
import type { MessageKey } from '../../../shared/locales'
import {
  resolveRemoteWebSocketConfig,
  type ConnectionSettingsDraft,
  type RemoteConnectionValidationCode
} from '../../../shared/remoteConnection'
import { useUIStore } from '../../stores/uiStore'
import { useConnectionStore } from '../../stores/connectionStore'
import {
  readConfigValue,
  useConfig,
  useConfigSetting,
  useConfigStore,
  type ConfigSetting
} from '../../stores/configStore'
import { usePluginStore } from '../../stores/pluginStore'
import { useSkillsStore } from '../../stores/skillsStore'
import { usePendingRestartStore } from '../../stores/pendingRestartStore'
import {
  replaceCurrentAppNavigationLocation,
  runWithoutAppNavigationRecording
} from '../../stores/appNavigationStore'
import { useSettingsWorkspaceConfigChangeEffects } from '../../hooks/useSettingsWorkspaceConfigChangeEffects'
import { stringifyComposerDraftSegments } from '../conversation/richInputSerialization'
import type { ComposerDraftSegment } from '../../types/composerDraft'
import { ActionTooltip } from '../ui/ActionTooltip'
import { IconButton } from '../ui/IconButton'
import { Button } from '../ui/Button'
import { PillSwitch } from '../ui/PillSwitch'
import { useConfirmDialog } from '../ui/ConfirmDialog'
import { SettingsGroup, SettingsRow } from './SettingsGroup'
import {
  SETTINGS_SURFACE_CLASS,
  settingsHintStyle,
  settingsLabelStyle,
  settingsMetaTextStyle,
  settingsPlaceholderStyle,
  settingsSectionLabelStyle as sectionLabelStyle
} from './settingsTypography'
import { SettingsDescriptionWithLearnMore } from './SettingsLearnMoreLink'
import { SettingsPanelShell } from './SettingsPanelShell'
import { SettingsBreadcrumb } from './SettingsBreadcrumb'
import { PluginIcon, pluginSubtitle, pluginTitle } from '../plugins/PluginCatalogItem'
import { PluginInstallDialog } from '../plugins/PluginInstallDialog'
import controlRowStyles from './panels/computerUse/ComputerUsePanel.module.css'
import { showPluginInstalledToast } from '../plugins/pluginToasts'

import { SettingsSelect } from './ui/SettingsSelect'
import { DreamsPanel } from './panels/memory/DreamsPanel'
import { MemorySettingsGroup } from './panels/memory/MemorySettingsGroup'
import { useDreamsSettings } from './panels/memory/useDreamsSettings'
import { ImageGenerationSettingsGroup } from './panels/imageGeneration/ImageGenerationSettingsGroup'
import { WebSearchSettingsGroup } from './panels/webSearch/WebSearchSettingsGroup'
import { canProviderCreateImages } from './panels/imageGeneration/imageGenerationModel'
import { CodeModeSettingsGroup } from './panels/codeMode/CodeModeSettingsGroup'
import { InstantInterruptRow } from './panels/instantInterrupt/InstantInterruptRow'
import { SegmentedControl } from './ui/SegmentedControl'
import { GeneralPanel } from './panels/GeneralPanel'
import { FollowUpBehaviorRow } from './panels/FollowUpBehaviorRow'
import { NewTaskBehaviorRow } from './panels/NewTaskBehaviorRow'
import { DefaultOpenTargetRow } from './panels/DefaultOpenTargetRow'
import { ChatsFolderRow } from './panels/ChatsFolderRow'
import { NotificationsSettingsGroup } from './panels/NotificationsSettingsGroup'
import { useComposerPreferencesStore } from '../../stores/composerPreferencesStore'
import { ProviderProtocolIcon } from './panels/ProviderProtocolIcon'
import { UsagePanel } from './panels/UsagePanel'
import { UsageView } from './usage/UsageView'
import { ProfilePanel } from './panels/ProfilePanel'
import { ProfileView } from './ProfileView'
import { ProviderModelSummary } from './ProviderModelSummary'
import { McpPanel } from './panels/McpPanel'
import { McpServerEditor } from './panels/mcp/McpServerEditor'
import { isPluginManagedMcpServer, McpServerList } from './panels/mcp/McpServerList'
import { oauthPayload, useMcpDraft } from './panels/mcp/useMcpDraft'
import {
  StatusIndicator,
  statusTextStyle
} from './panels/settingsStatusStyles'
import { coreSettingsPanels } from './coreSettingsPanels'
import { settingsContentContainerStyle, settingsMainStyle } from './settingsLayout'
import { SettingsPanelSkeleton } from './SettingsSkeleton'
import { DesktopPluginSettingsPageOutlet } from '../desktopPlugins/DesktopPluginOutlets'
import {
  isDesktopPluginSettingsTab,
  useDesktopPluginRegistry
} from '../../plugins/desktopPluginRegistry'
import {
  useMcpStore,
  type McpServerConfigWire,
  type McpServerStatusWire
} from '../../stores/mcpStore'
import type {
  BinarySource,
  BrowserUseApprovalMode,
  ConnectionMode
} from '../../../preload/api'
import type { ConfigChangedPayload } from '../../utils/configChanged'
import { isSessionImportAvailable } from '../../utils/sessionImport'
import { providerPreferenceEdit, resolveWorkspaceProviderFromConfig } from '../../utils/workspaceCoreConfig'
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
import type { ProviderBackendInfo } from '@dotcraft/sdk/contracts'
import {
  parseModelCatalogItems,
  useModelCatalogStore,
  type ModelCatalogItem
} from '../../stores/modelCatalogStore'
import {
  createCatalogDefaultPreference,
  normalizePreferenceForModel,
  PreferenceModelPicker
} from '../conversation/PreferenceModelPicker'

declare const __APP_VERSION__: string | undefined

const AppearancePanel = coreSettingsPanels.appearance
const PetPanel = coreSettingsPanels.pet
const VoicePanel = coreSettingsPanels.voice
const ComputerUsePanel = coreSettingsPanels.computerUse
const ConnectionsPanel = coreSettingsPanels.connections
const SourceControlPanel = coreSettingsPanels.sourceControl
const WorktreesPanel = coreSettingsPanels.worktrees
const HooksPanel = coreSettingsPanels.hooks
const SubAgentsPanel = coreSettingsPanels.subAgents
const ImportPanel = coreSettingsPanels.import
const ArchivedThreadsSettingsView = coreSettingsPanels.archivedThreads

interface SettingsViewProps {
  workspacePath?: string
  identityWorkspacePath?: string
  onThreadListRefreshRequested?: () => void
  workspaceConfigChange?: ConfigChangedPayload | null
  workspaceConfigChangeSeq?: number
  openChromeSettingsSeq?: number
}

interface McpTestResultWire {
  success: boolean
  errorCode?: string
  errorMessage?: string
  toolCount?: number
}

type VisibleApprovalPolicy = 'default' | 'autoApprove'
const SETTINGS_SELECT_WIDTH = '240px'
const APPROVAL_MENU_MIN_WIDTH = 288

const DEFAULT_WS_HOST = '127.0.0.1'
const DEFAULT_WS_PORT = 9100

const REMOTE_URL_ERROR_KEYS: Record<RemoteConnectionValidationCode, MessageKey> = {
  'missing-url': 'settings.remoteUrlError.missing',
  'invalid-url': 'settings.remoteUrlError.invalid',
  'unsupported-protocol': 'settings.remoteUrlError.protocol'
}

function toContractMcpServer(server: McpServerConfigWire) {
  return {
    ...server,
    oauth: oauthPayload(server.oauth),
    origin: server.origin ? { ...server.origin } : server.origin
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
  const browserDownloadHistoryOpen = useUIStore(state => state.browserDownloadHistoryOpen)
  const activeSettingsTab = useUIStore((s) => s.activeSettingsTab)
  const setActiveSettingsTab = useUIStore((s) => s.setActiveSettingsTab)
  const settingsCloseRequestSeq = useUIStore((s) => s.settingsCloseRequestSeq)
  const showThinkingContent = useUIStore((s) => s.showThinkingContent)
  const setShowThinkingContent = useUIStore((s) => s.setShowThinkingContent)
  const capabilities = useConnectionStore((s) => s.capabilities)
  const setExpectedRestart = useConnectionStore((s) => s.setExpectedRestart)
  const dashboardUrl = useConnectionStore((s) => s.dashboardUrl)
  const plugins = usePluginStore((s) => s.plugins)
  const activeDesktopPluginSettingsPage = useDesktopPluginRegistry((state) =>
    state.settingsPages.find((entry) => entry.settingsKey === activeSettingsTab) ?? null)
  const fetchPlugins = usePluginStore((s) => s.fetchPlugins)
  const installPlugin = usePluginStore((s) => s.installPlugin)
  const fetchSkills = useSkillsStore((s) => s.fetchSkills)
  const mcpStatuses = useMcpStore((s) => s.statuses)
  const setMcpStatuses = useMcpStore((s) => s.setStatuses)
  const [binarySource, setBinarySource] = useState<BinarySource>('bundled')
  const [binaryPath, setBinaryPath] = useState('')
  const [resolvedBinaryPath, setResolvedBinaryPath] = useState<string | null>(null)
  const [resolvingBinary, setResolvingBinary] = useState(false)
  const [connectionMode, setConnectionMode] = useState<ConnectionMode>('local')
  const [wsHost, setWsHost] = useState(DEFAULT_WS_HOST)
  const [wsPort, setWsPort] = useState(String(DEFAULT_WS_PORT))
  const [remoteUrl, setRemoteUrl] = useState('')
  const [remoteToken, setRemoteToken] = useState('')
  const [sshManagedSession, setSshManagedSession] = useState(false)
  const [locale, setLocale] = useState<AppLocale>(normalizeLocale(undefined))
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
  const [chromeDetailRequested, setChromeDetailRequested] = useState(false)
  const [baselineConnection, setBaselineConnection] = useState<{
    binarySource: BinarySource
    binaryPath: string
    connectionMode: ConnectionMode
    wsHost: string
    wsPort: string
    remoteUrl: string
    remoteToken: string
  } | null>(null)
  const [providersManagedRemotely, setProvidersManagedRemotely] = useState(false)
  const [providers, setProviders] = useState<ProviderInfoWire[]>([])
  const [providersLoading, setProvidersLoading] = useState(false)
  const [providerBackends, setProviderBackends] = useState<ProviderBackendInfo[] | null>(null)
  const [providerDraft, setProviderDraft] = useState<ProviderDraft>(() => createProviderDraft())
  const [providerEditorId, setProviderEditorId] = useState<ProviderEditorId>(null)
  const providerEditorIdRef = useRef(providerEditorId)
  providerEditorIdRef.current = providerEditorId
  const [selectedProviderId, setSelectedProviderId] = useState('')
  const selectedProviderIdRef = useRef('')
  const [workspacePreference, setWorkspacePreference] = useState<ModelPreference>(
    () => createManualModelPreference('')
  )
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
  const [skillsRestartPending, setSkillsRestartPending] = useState(false)
  const [resettingMemory, setResettingMemory] = useState(false)
  const inputRef = useRef<HTMLInputElement>(null)
  const settingsCloseRequestSeqRef = useRef(settingsCloseRequestSeq)
  const workspaceConfig = useConfig()
  const configProviderPreferences = useMemo(
    () => readProviderPreferences(readConfigValue(workspaceConfig, 'ProviderPreferences')),
    [workspaceConfig]
  )
  const configProviderId = resolveWorkspaceProviderFromConfig(workspaceConfig ?? {})
  const configLlmKey = workspaceConfig == null
    ? null
    : JSON.stringify([configProviderId, findProviderPreference(configProviderPreferences, configProviderId)])
  const configLlm = useMemo(
    () => configLlmKey == null ? null : JSON.parse(configLlmKey) as [string, ModelPreference | null],
    [configLlmKey]
  )
  const welcomeSuggestions = useConfigSetting('WelcomeSuggestions.Enabled', (error) =>
    t('settings.personalization.welcomeSuggestionsSaveFailed', { error }))
  const promptSuggestions = useConfigSetting('PromptSuggestions.Enabled', (error) =>
    t('settings.personalization.promptSuggestionsSaveFailed', { error }))
  const selfLearning = useConfigSetting('Skills.SelfLearning.Enabled', (error) =>
    t('settings.personalization.selfLearningSaveFailed', { error }))
  const sharedSkills = useConfigSetting('Skills.IncludeSharedSkills', (error) =>
    t('settings.personalization.sharedSkillsSaveFailed', { error }))
  const memory = useConfigSetting('Memory.Enabled', (error) =>
    t('settings.personalization.memory.saveFailed', { error }))
  const memoryAutoExtract = useConfigSetting('Memory.AutoExtract', (error) =>
    t('settings.personalization.memory.saveFailed', { error }))
  const defaultApproval = useConfigSetting('Permissions.DefaultApprovalPolicy', (error) =>
    t('settings.permissions.saveFailed', { error }))
  const selfLearningEnabled = selfLearning.value === true
  const includeSharedSkills = sharedSkills.value === true
  const memoryEnabled = memory.value === true
  const defaultApprovalPolicy = typeof defaultApproval.value === 'string' ? defaultApproval.value : ''

  const [mcpServers, setMcpServers] = useState<McpServerConfigWire[]>([])
  const [mcpLoading, setMcpLoading] = useState(false)
  const [mcpError, setMcpError] = useState<string | null>(null)
  const mcpEditor = useMcpDraft()
  const { editingServerName, mcpDraft, buildDraftPayload } = mcpEditor
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
  const sessionImportEnabled = isSessionImportAvailable(capabilities)
  const configManagementEnabled = capabilities?.workspaceConfigManagement === true
  const personalizationAvailable = configManagementEnabled || memoryManagementEnabled || dreamsCapabilityEnabled
  const dreams = useDreamsSettings({
    available: dreamsCapabilityEnabled,
    personalizationActive: activeSettingsTab === 'personalization',
    dreamsPageActive: activeSettingsTab === 'dreams',
    dashboardUrl
  })
  const browserUsePlugin = plugins.find((plugin) => plugin.id === 'browser') ?? null
  const browserUsePluginReady = !pluginManagementEnabled || browserUsePlugin?.installed === true
  const selectedProvider = providers.find((provider) => provider.id === selectedProviderId) ?? null
  const providerEditorProvider =
    providerEditorId != null && providerEditorId !== '__new__'
      ? providers.find((provider) => provider.id === providerEditorId) ?? null
      : null
  const providerEditorIsNew = providerEditorId === '__new__'
  const providerEditingEnabled = providerManagementEnabled && configManagementEnabled && !providersManagedRemotely
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
  const sshManagedConnection = connectionMode === 'remote' && sshManagedSession
  const manualRemoteConnection = connectionMode === 'remote' && !sshManagedConnection
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
  useEffect(() => {
    if (!configLlm) return
    const [providerId, preference] = configLlm
    selectedProviderIdRef.current = providerId
    setSelectedProviderId(providerId)
    const resolvedPreference = preference ?? createManualModelPreference('')
    setWorkspacePreference(resolvedPreference)
    setWorkspaceManualModelDraft(resolvedPreference.model)
  }, [configLlm])

  async function reloadProviders(): Promise<boolean> {
    if (!providerManagementEnabled) {
      setProviders([])
      return false
    }
    setProvidersLoading(true)
    try {
      const [result, backendResult] = await Promise.all([
        window.api.appServer.sendRequest('provider/list', {}, 20_000),
        window.api.appServer.sendRequest('provider/backend/list', {}, 20_000).catch(() => null)
      ])
      setProvidersManagedRemotely((result as { managedBy?: string }).managedBy === 'modelService')
      setProviders(normalizeProviderList(result))
      const backends = (backendResult as { backends?: unknown } | null)?.backends
      setProviderBackends(Array.isArray(backends) ? backends as ProviderBackendInfo[] : null)
      return true
    } catch (err) {
      addToast(t('settings.llm.toast.loadProvidersFailed', {
        error: err instanceof Error ? err.message : String(err)
      }), 'error')
      return false
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
    providerEditorIdRef.current = '__new__'
    setProviderEditorId('__new__')
    setProviderDraft(createProviderDraft())
    setProviderTestResult(null)
  }

  function startEditProvider(provider: ProviderInfoWire): void {
    providerEditorIdRef.current = provider.id
    setProviderEditorId(provider.id)
    setProviderDraft(providerDraftFromInfo(provider))
    setProviderTestResult(null)
  }

  function closeProviderEditor(): void {
    providerEditorIdRef.current = null
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
      const rememberedPreference = findProviderPreference(configProviderPreferences, normalized)
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

      await useConfigStore.getState().write([
        { keyPath: 'ProviderId', value: normalized },
        ...(nextPreference.model ? [providerPreferenceEdit(normalized, nextPreference)] : [])
      ])
      selectedProviderIdRef.current = normalized
      setSelectedProviderId(normalized)
      if (listedModels != null) {
        setProviderModelCatalog(listedModels)
        setProviderModelError('')
      }
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
      await useConfigStore.getState().write([providerPreferenceEdit(activeProviderId, normalized)])
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
    const persistedPreference = findProviderPreference(configProviderPreferences, selectedProviderId)
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
      const apiKeyUnchanged = editing?.hasApiKey === true && isStoredApiKey(providerDraft)
      let payload: Record<string, unknown>
      if (providerConnectionMode(providerDraft, providerBackends ?? []) !== 'apiKey') {
        payload = { providerId: editing?.id ?? providerDraft.id.trim() }
      } else if (editing && apiKeyUnchanged) {
        payload = { providerId: editing.id }
      } else {
        payload = {
          protocol: providerDraft.protocol,
          apiKey: isStoredApiKey(providerDraft) ? null : providerDraft.apiKey.trim(),
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

  async function persistProviderDraft(): Promise<{ id: string; created: boolean } | null> {
    const id = providerDraft.id.trim()
    if (!id) {
      addToast(t('settings.llm.toast.providerIdRequired'), 'warning')
      return null
    }
    const editing = providerEditorProvider
    if (editing == null && providerIdExists(providers, id)) {
      addToast(t('settings.llm.toast.providerIdExists', { providerId: id }), 'warning')
      return null
    }
    try {
      const fields = providerFieldsFromDraft(providerDraft, editing)
      if (editing != null) await updateProvider(editing, fields)
      else await createProvider(id, fields)
      return { id: editing?.id ?? id, created: editing == null }
    } catch (err) {
      if (err instanceof ProviderExistsError) {
        addToast(t('settings.llm.toast.providerIdExists', { providerId: err.providerId }), 'warning')
      } else {
        addToast(t('settings.llm.toast.saveProviderFailed', {
          error: err instanceof Error ? err.message : String(err)
        }), 'error')
      }
      return null
    }
  }

  async function handleProviderSave(): Promise<void> {
    setSavingProvider(true)
    try {
      const saved = await persistProviderDraft()
      if (!saved) return
      addToast(t(saved.created ? 'settings.llm.toast.providerCreated' : 'settings.llm.toast.providerUpdated'), 'success')
      if (saved.created && providerConnectionMode(providerDraft, providerBackends ?? []) !== 'signIn') {
        const previous = providers.find((p) => p.id === selectedProviderId)
        const shouldActivate = !selectedProviderId || !previous || !providerUsable(previous)
        if (shouldActivate && selectedProviderId !== saved.id) {
          await handleWorkspaceProviderChange(saved.id)
        }
      }
      await reloadProviders()
      closeProviderEditor()
    } finally {
      setSavingProvider(false)
    }
  }

  async function prepareProviderSignIn(): Promise<string | null> {
    const editor = providerEditorIdRef.current
    setSavingProvider(true)
    try {
      const saved = await persistProviderDraft()
      if (!saved || providerEditorIdRef.current !== editor) return null
      providerEditorIdRef.current = saved.id
      setProviderEditorId(saved.id)
      await reloadProviders()
      return saved.id
    } finally {
      setSavingProvider(false)
    }
  }

  async function refreshAfterProviderSignIn(providerId: string): Promise<void> {
    await reloadProviders()
    void useModelCatalogStore.getState().handleConfigChanged(['providers'])
    if (selectedProviderIdRef.current === providerId) void reloadProviderPreferences(providerId)
  }

  async function handleProviderSignedIn(providerId: string): Promise<void> {
    const selected = selectedProviderIdRef.current
    const previous = providers.find((provider) => provider.id === selected)
    const shouldActivate = !selected || selected === providerId || !previous || !providerUsable(previous)
    if (shouldActivate && selected !== providerId) await handleWorkspaceProviderChange(providerId)
    const name = providers.find((provider) => provider.id === providerId)?.displayName ?? providerId
    if (selectedProviderIdRef.current === providerId) {
      addToast(t('settings.llm.toast.signInActivated', { name }), 'success')
    } else if (!shouldActivate) {
      addToast(t('settings.llm.toast.signInActivateSkipped'), 'info')
    }
    await refreshAfterProviderSignIn(providerId)
  }

  async function handleProviderSignedOut(providerId: string): Promise<void> {
    await refreshAfterProviderSignIn(providerId)
  }

  async function handleProviderDelete(provider: ProviderInfoWire): Promise<void> {
    if (provider.id.toLowerCase() === selectedProviderId.trim().toLowerCase()) {
      addToast(t('settings.llm.toast.providerInUse'), 'warning')
      return
    }
    setDeletingProvider(true)
    try {
      await deleteProvider(provider.id)
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

  async function handleSkillsToggle(setting: ConfigSetting, checked: boolean): Promise<void> {
    if (await setting.set(checked)) setSkillsRestartPending(true)
  }

  const handleResetMemory = useCallback(
    async (): Promise<void> => {
      if (resettingMemory) return

      const confirmed = await confirm({
        title: t('settings.personalization.memory.deleteConfirmTitle'),
        message: t('settings.personalization.memory.deleteConfirmMessage'),
        confirmLabel: t('settings.personalization.memory.deleteButton'),
        cancelLabel: t('common.cancel'),
        danger: true
      })
      if (!confirmed) return

      setResettingMemory(true)
      try {
          await window.api.appServer.sendRequest('memory/reset', {}, 20_000)
        addToast(t('settings.personalization.memory.deleteSuccess'), 'success')
      } catch {
        addToast(t('settings.personalization.memory.deleteFailed'), 'error')
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

  async function handleDefaultApprovalPolicyChange(nextPolicy: VisibleApprovalPolicy): Promise<boolean> {
    if (nextPolicy === defaultApprovalPolicy || defaultApproval.pending) return false

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

    return defaultApproval.set(nextPolicy)
  }

  useSettingsWorkspaceConfigChangeEffects({
    change: workspaceConfigChange,
    changeSeq: workspaceConfigChangeSeq,
    mcpEnabled,
    subAgentEnabled,
    reloadDreamsStatus: dreams.reloadStatus,
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
      || (activeSettingsTab === 'import' && !sessionImportEnabled)
      || (isDesktopPluginSettingsTab(activeSettingsTab) && !activeDesktopPluginSettingsPage)
    if (!unavailable) return
    runWithoutAppNavigationRecording(() => setActiveSettingsTab('general'))
    replaceCurrentAppNavigationLocation()
  }, [activeDesktopPluginSettingsPage, activeSettingsTab, hooksEnabled, mcpEnabled, subAgentEnabled, sourceControlEnabled, sessionImportEnabled])

  useEffect(() => {
    if (activeSettingsTab === 'browserUse' && pluginManagementEnabled) {
      void fetchPlugins()
    }
  }, [activeSettingsTab, fetchPlugins, pluginManagementEnabled])

  useEffect(() => {
    if (openChromeSettingsSeq <= 0) return
    setActiveSettingsTab('computerControl')
    setChromeDetailRequested(true)
  }, [openChromeSettingsSeq])

  useEffect(() => {
    inputRef.current?.focus()
    window.api.settings
      .get()
      .then(async (s) => {
        const loadedMode = s.connectionMode === 'remote' ? 'remote' : 'local'
        setBinarySource((s.binarySource ?? 'bundled') as BinarySource)
        setBinaryPath(s.appServerBinaryPath ?? '')
        setConnectionMode(loadedMode)
        setWsHost(s.webSocket?.host ?? DEFAULT_WS_HOST)
        setWsPort(String(s.webSocket?.port ?? DEFAULT_WS_PORT))
        setRemoteUrl(s.remote?.url ?? '')
        setRemoteToken(s.remote?.token ?? '')
        setSshManagedSession(
          Boolean(s.activeRemoteStack?.hostId && s.activeRemoteStack.stackId) ||
            Boolean(s.activeRemoteProject?.machineId && s.activeRemoteProject.projectId)
        )
        setLocale(normalizeLocale(s.locale))
        setShowInMenuBar(isMac ? s.showInMenuBar !== false : false)
        setShowThinkingContent(s.showThinkingContent === true)
        useComposerPreferencesStore.getState().hydrate(s)
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

  function restoreConnectionBaseline(): void {
    if (!baselineConnection) return
    setBinarySource(baselineConnection.binarySource)
    setBinaryPath(baselineConnection.binaryPath)
    setConnectionMode(baselineConnection.connectionMode)
    setWsHost(baselineConnection.wsHost)
    setWsPort(baselineConnection.wsPort)
    setRemoteUrl(baselineConnection.remoteUrl)
    setRemoteToken(baselineConnection.remoteToken)
  }

  function closeSettings(): void {
    if (connectionDirty) {
      const shouldDiscard = window.confirm(t('settings.pendingChanges.leaveConfirm'))
      if (!shouldDiscard) return
      restoreConnectionBaseline()
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
    mcpEditor.startMcpDraft(server)
    setMcpTestResult(null)
  }

  function cancelMcpEdit(): void {
    mcpEditor.cancelMcpEdit()
    setMcpTestResult(null)
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
        scope: mcpServers.find(server => server.name === editingServerName)?.origin?.kind === 'user' ? 'user' : 'workspace',
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
      await window.api.appServer.sendRequest('mcp/upsert', {
        scope: mcpServers.find(server => server.name === editingServerName)?.origin?.kind === 'user' ? 'user' : 'workspace',
        server: toContractMcpServer(payload)
      })
      await Promise.all([reloadMcpServers(), reloadMcpStatuses()])
      setMcpSavedHint(t('settings.savedToast'))
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
        scope: server.origin?.kind === 'user' ? 'user' : 'workspace',
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
      await window.api.appServer.sendRequest('mcp/remove', { name, scope: mcpServers.find(server => server.name === name)?.origin?.kind === 'user' ? 'user' : 'workspace' })
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
      showPluginInstalledToast(browserUsePlugin, {
        message: t('plugins.installSuccess', { name: pluginTitle(browserUsePlugin) })
      })
    } catch {
      addToast(t('plugins.installFailed'), 'error')
    } finally {
      setBrowserUseInstalling(false)
    }
  }

  function handleOpenBrowserUsePlugin(): void {
    if (!browserUsePlugin) return
    void usePluginStore.getState().selectPlugin(browserUsePlugin.id)
    const ui = useUIStore.getState()
    ui.setPluginCatalogSurface('plugins')
    ui.setActiveMainView('skills')
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
      setSshManagedSession(false)
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
    let needsAppServerRestart = skillsRestartPending && !targetIsRemote
    let appServerRestartAttempted = false
    let connectionApplied = false
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
        setSkillsRestartPending(false)
        addToast(t('settings.restartAppServerSuccess'), 'success')
      } else if (connectionApplied) {
        if (targetIsRemote && skillsRestartPending) {
          setSkillsRestartPending(false)
          addToast(t('settings.personalization.skillsRestartBannerRemote'), 'warning')
        } else {
          setSkillsRestartPending(false)
        }
        addToast(
          t(targetIsRemote ? 'settings.connection.applyConnectSuccess' : 'settings.restartAppServerSuccess'),
          'success'
        )
      } else if (targetIsRemote && skillsRestartPending) {
        setSkillsRestartPending(false)
        addToast(t('settings.personalization.skillsRestartBannerRemote'), 'warning')
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
    if (skillsRestartPending) {
      parts.push(`selfLearning:${selfLearningEnabled}`)
      parts.push(`sharedSkills:${includeSharedSkills}`)
    }
    return parts.join('|')
  }, [
    binaryPath,
    binarySource,
    connectionDirty,
    connectionMode,
    includeSharedSkills,
    remoteToken,
    remoteUrl,
    selfLearningEnabled,
    skillsRestartPending,
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

  return (
    <div
      aria-label={t('settings.title')}
      style={{
        display: 'flex',
        flexDirection: 'column',
        height: '100%',
        minHeight: 0,
        // Transparent so the shared ThreePanel main-surface frame shows through;
        // an opaque --bg-primary here hides that frame.
        backgroundColor: 'transparent'
      }}
    >
      <main className="dc-scrollbar-stable" style={settingsMainStyle()}>
        <div className={SETTINGS_SURFACE_CLASS} style={settingsContentContainerStyle()}>
          <Suspense fallback={<SettingsPanelSkeleton />}>
            {activeSettingsTab === 'profile' && (
              <ProfilePanel>
                <ProfileView />
              </ProfilePanel>
            )}

            {activeSettingsTab === 'appearance' && <AppearancePanel />}

            {activeSettingsTab === 'pet' && <PetPanel />}

            {activeSettingsTab === 'voice' && <VoicePanel />}

            {activeSettingsTab === 'general' && (
              <GeneralPanel>
              <SettingsPanelShell
                title={t('settings.tab.general')}
                description={t('settings.general.description')}
              >
                <SettingsGroup
                  title={t('settings.group.application')}
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

                  <DefaultOpenTargetRow width={SETTINGS_SELECT_WIDTH} />

                  <ChatsFolderRow />

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

                <NotificationsSettingsGroup />

                <SettingsGroup title={t('settings.group.composer')}>
                  <FollowUpBehaviorRow />
                  <NewTaskBehaviorRow />
                  <InstantInterruptRow />
                </SettingsGroup>

                {configManagementEnabled && <CodeModeSettingsGroup />}

                <SettingsGroup title={t('settings.group.permissions')}>
                  <SettingsRow
                    label={t('settings.permissions.workspaceDefault.label')}
                    description={t('settings.permissions.workspaceDefault.description')}
                    htmlFor="settings-default-approval-policy"
                    control={
                      <SettingsSelect
                        id="settings-default-approval-policy"
                        value={defaultApprovalPolicy}
                        disabled={defaultApproval.pending}
                        ariaLabel={t('settings.permissions.workspaceDefault.label')}
                        onValueChange={(nextPolicy) => {
                          return handleDefaultApprovalPolicyChange(nextPolicy as VisibleApprovalPolicy)
                        }}
                        style={{ width: SETTINGS_SELECT_WIDTH }}
                        menuMinWidth={APPROVAL_MENU_MIN_WIDTH}
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
                        {t(providersManagedRemotely ? 'settings.llm.managedByService' : 'settings.llm.description')}
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
                        disabled={!providerEditingEnabled}
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
                              <SegmentedControl<'inherit' | 'custom'>
                                value={subAgentPreference == null ? 'inherit' : 'custom'}
                                options={[
                                  { value: 'inherit', label: t('settings.llm.subAgentPreferenceInherit') },
                                  { value: 'custom', label: t('settings.llm.subAgentPreferenceCustom') }
                                ]}
                                onChange={(mode) => void handleSubAgentInheritanceChange(mode === 'custom')}
                                disabled={
                                  applyingSubAgentModel
                                  || applyingWorkspaceProvider
                                  || providerModelLoading
                                  || workspacePreference.model.trim() === ''
                                }
                                ariaLabel={t('settings.llm.subAgentPreferenceAria')}
                              />
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

                    <ImageGenerationSettingsGroup
                      providers={providers}
                      providersLoading={providersLoading}
                      workspaceProviderId={selectedProviderId}
                      onEditProvider={providerEditingEnabled ? startEditProvider : undefined}
                      onAddProvider={providerEditingEnabled ? startCreateProvider : undefined}
                    />

                    <WebSearchSettingsGroup providers={providers} providersLoading={providersLoading} />

                    <SettingsGroup
                      title={t('settings.llm.providersTitle')}
                      description={providersLoading ? t('settings.llm.providersLoading') : providersCountLabel}
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
                            : findProviderPreference(configProviderPreferences, provider.id)
                          const rememberedSubAgentPreference = active
                            ? subAgentPreference
                            : findProviderPreference(subAgentProviderPreferences, provider.id)
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
                              <ActionTooltip label={providerDetails(provider, providerBackends, t)}>
                                <span style={{ display: 'inline-flex', flexShrink: 0 }}>
                                  <ProviderProtocolIcon protocol={provider.protocol} size={30} />
                                </span>
                              </ActionTooltip>
                              <div style={{ flex: 1, minWidth: 0 }}>
                                <div style={{ display: 'flex', alignItems: 'center', gap: '8px', minWidth: 0 }}>
                                  <div style={{
                                    flexShrink: 0,
                                    maxWidth: '60%',
                                    overflow: 'hidden',
                                    textOverflow: 'ellipsis',
                                    whiteSpace: 'nowrap',
                                    fontSize: '15px',
                                    fontWeight: 600,
                                    color: 'var(--text-primary)'
                                  }}>
                                    {provider.displayName}
                                  </div>
                                  {canProviderCreateImages(provider) && (
                                    <ActionTooltip label={t('settings.llm.imageGeneration.createsImages')}>
                                      <span
                                        role="img"
                                        aria-label={t('settings.llm.imageGeneration.createsImages')}
                                        style={{ display: 'inline-flex', flexShrink: 0, color: 'var(--text-dimmed)' }}
                                      >
                                        <ImageIcon aria-hidden size={14} strokeWidth={1.75} />
                                      </span>
                                    </ActionTooltip>
                                  )}
                                  {providerProblem(provider, providerBackends, t) && (
                                    <span style={{
                                      flex: 1,
                                      minWidth: 0,
                                      overflow: 'hidden',
                                      textOverflow: 'ellipsis',
                                      whiteSpace: 'nowrap',
                                      fontSize: '12px',
                                      color: 'var(--warning-text)'
                                    }}>
                                      {providerProblem(provider, providerBackends, t)}
                                    </span>
                                  )}
                                </div>
                                {rememberedMainAgentPreference || rememberedSubAgentPreference ? (
                                  <ProviderModelSummary
                                    main={rememberedMainAgentPreference}
                                    subAgent={rememberedSubAgentPreference ?? undefined}
                                  />
                                ) : (
                                  <div style={{ marginTop: '4px', fontSize: '12px', color: 'var(--text-dimmed)' }}>
                                    {t('settings.llm.providerSummaryNoModel')}
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
                                  disabled={!providerEditingEnabled}
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
                  <ProviderEditor
                    draft={providerDraft}
                    setDraft={setProviderDraft}
                    isNew={providerEditorIsNew}
                    savedProvider={providerEditorProvider}
                    providers={providers}
                    backends={providerBackends}
                    managementEnabled={providerManagementEnabled}
                    testing={testingProvider}
                    testResult={providerTestResult}
                    saving={savingProvider}
                    deleting={deletingProvider}
                    onTest={() => void handleProviderTest()}
                    onSave={() => void handleProviderSave()}
                    onDelete={(provider) => void handleProviderDelete(provider)}
                    prepareSignIn={prepareProviderSignIn}
                    onSignedIn={handleProviderSignedIn}
                    onSignedOut={handleProviderSignedOut}
                  />
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
                  {configManagementEnabled && (
                    <SettingsRow
                      label={t('settings.personalization.welcomeSuggestions')}
                      description={t('settings.personalization.welcomeSuggestionsHint')}
                      control={
                        <ActionTooltip
                          label=""
                          disabledReason={memoryEnabled ? undefined : t('settings.personalization.memory.requiredForSuggestions')}
                        >
                          <PillSwitch
                            checked={welcomeSuggestions.value === true}
                            disabled={welcomeSuggestions.pending || !memoryEnabled}
                            aria-label={t('settings.personalization.welcomeSuggestions')}
                            onChange={(checked) => {
                              void welcomeSuggestions.set(checked)
                            }}
                          />
                        </ActionTooltip>
                      }
                    />
                  )}
                  {configManagementEnabled && (
                    <SettingsRow
                      label={t('settings.personalization.promptSuggestions')}
                      description={t('settings.personalization.promptSuggestionsHint')}
                      control={
                        <PillSwitch
                          checked={promptSuggestions.value === true}
                          disabled={promptSuggestions.pending}
                          aria-label={t('settings.personalization.promptSuggestions')}
                          onChange={(checked) => { void promptSuggestions.set(checked) }}
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
                {configManagementEnabled && (
                  <SettingsGroup
                    title={t('settings.personalization.group.skills')}
                  >
                    <SettingsRow
                      label={t('settings.personalization.selfLearning')}
                      description={t('settings.personalization.selfLearningHint')}
                      control={
                        <PillSwitch
                          checked={selfLearningEnabled}
                          disabled={selfLearning.pending}
                          aria-label={t('settings.personalization.selfLearning')}
                          onChange={(checked) => {
                            void handleSkillsToggle(selfLearning, checked)
                          }}
                        />
                      }
                    />
                    <SettingsRow
                      label={t('settings.personalization.sharedSkills')}
                      description={t('settings.personalization.sharedSkillsHint')}
                      control={
                        <PillSwitch
                          checked={includeSharedSkills}
                          disabled={sharedSkills.pending}
                          aria-label={t('settings.personalization.sharedSkills')}
                          onChange={(checked) => {
                            void handleSkillsToggle(sharedSkills, checked)
                          }}
                        />
                      }
                    />
                  </SettingsGroup>
                )}
                {personalizationAvailable && (
                  <MemorySettingsGroup
                    memoryToggle={configManagementEnabled
                      ? {
                          enabled: memoryEnabled,
                          applying: memory.pending,
                          onToggle: (checked) => {
                            void memory.set(checked)
                          }
                        }
                      : null}
                    autoExtractToggle={configManagementEnabled
                      ? {
                          enabled: memoryAutoExtract.value === true,
                          applying: memoryAutoExtract.pending,
                          onToggle: (checked) => {
                            void memoryAutoExtract.set(checked)
                          }
                        }
                      : null}
                    dreams={dreamsCapabilityEnabled ? dreams : null}
                    onManageDreams={() => setActiveSettingsTab('dreams')}
                    deleteMemories={memoryManagementEnabled
                      ? {
                          deleting: resettingMemory,
                          onDelete: () => {
                            void handleResetMemory()
                          }
                        }
                      : null}
                  />
                )}
              </SettingsPanelShell>
              </GeneralPanel>
            )}

            {dreamsCapabilityEnabled && activeSettingsTab === 'dreams' && (
              <GeneralPanel>
                <DreamsPanel
                  dreams={dreams}
                  memoryEnabled={memoryEnabled}
                  dashboardUrl={dashboardUrl}
                  locale={locale}
                  onBack={() => setActiveSettingsTab('personalization')}
                />
              </GeneralPanel>
            )}

            {activeSettingsTab === 'connections' && (
              <ConnectionsPanel
                workspace={{
                  connectionMode,
                  onConnectionModeChange: setConnectionMode,
                  sshManagedConnection,
                  manualRemoteConnection,
                  remoteUrl,
                  onRemoteUrlChange: setRemoteUrl,
                  remoteUrlErrorKey:
                    remoteConnectionValidation && !remoteConnectionValidation.ok
                      ? REMOTE_URL_ERROR_KEYS[remoteConnectionValidation.code]
                      : null,
                  remoteToken,
                  onRemoteTokenChange: setRemoteToken,
                  binarySource,
                  onBinarySourceChange: setBinarySource,
                  binaryPath,
                  onBinaryPathChange: setBinaryPath,
                  binaryPathInputRef: inputRef,
                  onPickBinary: () => void handlePickBinary(),
                  resolvingBinary,
                  resolvedBinaryPath,
                  connectionDirty,
                  onRevert: restoreConnectionBaseline,
                  revertDisabled: restartingAppServer || saving
                }}
              />
            )}

            {activeSettingsTab === 'browserUse' && browserDownloadHistoryOpen && <GeneralPanel><BrowserDownloadHistory /></GeneralPanel>}
            {activeSettingsTab === 'browserUse' && !browserDownloadHistoryOpen && (
              <GeneralPanel>
              <SettingsPanelShell
                  title={t('settings.browserUse.pageTitle')}
                  description={t('settings.browserUse.pageDescription')}
                >
                {pluginManagementEnabled && browserUsePlugin && (
                  <SettingsGroup title={t('settings.browserUse.plugin')}>
                    <SettingsRow>
                      <div className={controlRowStyles.controlRow}>
                        <button type="button" className={controlRowStyles.controlOpen} onClick={handleOpenBrowserUsePlugin}>
                          <PluginIcon plugin={browserUsePlugin} role="list" size={38} />
                          <div className={controlRowStyles.controlText}>
                            <strong style={settingsLabelStyle()}>{pluginTitle(browserUsePlugin)}</strong>
                            <span className={controlRowStyles.controlDetail} style={settingsHintStyle()}>
                              {pluginSubtitle(browserUsePlugin)}
                            </span>
                          </div>
                        </button>
                        <div className={controlRowStyles.controlActions}>
                          {!browserUsePlugin.installed ? (
                            <Button onClick={() => setBrowserUseInstallOpen(true)}>{t('plugins.install')}</Button>
                          ) : browserUsePlugin.enabled ? (
                            <Button iconLeft={<MessageCircle size={14} aria-hidden />} onClick={handleTryBrowserUseInChat}>
                              {t('plugins.tryInChat')}
                            </Button>
                          ) : null}
                        </div>
                      </div>
                    </SettingsRow>
                  </SettingsGroup>
                )}

                <BrowserDownloadsSettings />
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
                        <Button iconLeft={<Plus size={15} />} onClick={() => openBrowserUseDomainDialog('blocked')}>
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
                            <IconButton
                              icon={<Trash2 size={14} aria-hidden />}
                              label={t('settings.browserUse.remove')}
                              tooltipLabel={t('settings.browserUse.remove')}
                              tooltipPlacement="top"
                              onClick={() => void handleRemoveBrowserUseDomain('blocked', domain)}
                            />
                          }
                        />
                      ))}
                    </SettingsGroup>

                    <SettingsGroup
                      title={t('settings.browserUse.allowedDomains')}
                      description={t('settings.browserUse.allowedDomainsHint')}
                      headerAction={
                        <Button iconLeft={<Plus size={15} />} onClick={() => openBrowserUseDomainDialog('allowed')}>
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
                            <IconButton
                              icon={<Trash2 size={14} aria-hidden />}
                              label={t('settings.browserUse.remove')}
                              tooltipLabel={t('settings.browserUse.remove')}
                              tooltipPlacement="top"
                              onClick={() => void handleRemoveBrowserUseDomain('allowed', domain)}
                            />
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
              <ComputerUsePanel
                chromeDetailRequested={chromeDetailRequested}
                onChromeDetailRequestHandled={() => setChromeDetailRequested(false)}
              />
            )}

            {activeSettingsTab === 'usage' && (
              <UsagePanel>
                <UsageView dashboardUrl={dashboardUrl} />
              </UsagePanel>
            )}

            {activeSettingsTab === 'sourceControl' && (
              <SourceControlPanel workspacePath={workspacePath} />
            )}

            {activeSettingsTab === 'worktrees' && <WorktreesPanel workspacePath={workspacePath} />}

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
                  <McpServerList
                    servers={mergedMcpServers}
                    statuses={mcpStatuses}
                    loading={mcpLoading}
                    error={mcpError}
                    originsEnabled={mcpOriginsEnabled}
                    togglingServerName={togglingServerName}
                    authenticatingServerName={authenticatingMcpName}
                    onEdit={startMcpDraft}
                    onToggle={(server, enabled) => void handleMcpQuickToggle(server, enabled)}
                    onAuthenticate={(server) => void handleMcpOAuthLogin(server)}
                    onViewPlugin={(server) => void handleViewPluginMcp(server)}
                  />
                )}

                {mcpEnabled && editingServerName !== null && (
                  <div style={{ display: 'flex', flexDirection: 'column', gap: '20px' }}>
                    <McpServerEditor editor={mcpEditor} />

                    {mcpTestResult && (
                      <div style={cardStyle()}>
                        <div style={{ ...statusTextStyle(), fontSize: '13px', fontWeight: 600 }}>
                          <StatusIndicator tone={mcpTestResult.success ? 'success' : 'error'} />
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

            {activeSettingsTab === 'import' && sessionImportEnabled && (
              <ImportPanel workspacePath={identityWorkspacePath || workspacePath} />
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
          </Suspense>
          {activeDesktopPluginSettingsPage && (
            <DesktopPluginSettingsPageOutlet contribution={activeDesktopPluginSettingsPage} />
          )}
        </div>
      </main>
      {browserUseDomainTarget && (
        <BrowserUseDomainDialog
          target={browserUseDomainTarget}
          value={browserUseDomainDraft}
          error={browserUseDomainError}
          onChange={(value) => {
            setBrowserUseDomainDraft(value)
            setBrowserUseDomainError('')
          }}
          onCancel={() => setBrowserUseDomainTarget(null)}
          onSubmit={() => void handleAddBrowserUseDomain()}
        />
      )}
    </div>
  )
}

