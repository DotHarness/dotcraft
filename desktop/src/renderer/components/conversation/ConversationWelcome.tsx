import { useCallback, useEffect, useMemo, useRef, useState, type ComponentType, type CSSProperties } from 'react'
import { BookText, Bot, Bug, FileText, Link2, ListChecks, Sparkles, Target } from 'lucide-react'
import { useLocale, useT } from '../../contexts/LocaleContext'
import { useConnectionStore } from '../../stores/connectionStore'
import { useModelCatalogStore, type InferenceSpeedWire, type ReasoningEffortWire, type ReasoningOutputWire } from '../../stores/modelCatalogStore'
import { useProvidersStore, useChatGptOAuthSummary } from '../../stores/providersStore'
import { useThreadStore } from '../../stores/threadStore'
import { usePerforceChangelistStore } from '../../stores/perforceChangelistStore'
import { useUIStore } from '../../stores/uiStore'
import { useComposerDraftStore, type ThreadComposerDraftInput } from '../../stores/composerDraftStore'
import { useSkillsStore } from '../../stores/skillsStore'
import { useAppBindingStore, type AppInfo } from '../../stores/appBindingStore'
import { addToast } from '../../stores/toastStore'
import { useCustomCommandCatalog } from '../../hooks/useCustomCommandCatalog'
import type { ComposerFileAttachment, ImageAttachment, ThreadMode } from '../../types/conversation'
import type { ComposerDraftSegment } from '../../types/composerDraft'
import type { ContextWindowConfigurationWire, ContextWindowMode, ThreadSummary } from '../../types/thread'
import { parseJsonConfig } from '../../../shared/jsonConfig'
import {
  classifyDroppedComposerFiles,
  isImageFile,
  mergeComposerFileAttachments
} from '../../utils/composerAttachments'
import { buildComposerInputParts } from '../../utils/composeInputParts'
import { runtimeWorkspaceRootsFor } from '../../utils/workspaceRuntimeRoots'
import { buildGoalObjective, extractGoal, parseGoalSlashCommand, type GoalSlashCommand } from '../../utils/threadGoal'
import { expandInitCommand } from '../../utils/initCommand'
import { CommandSearchPopover } from './CommandSearchPopover'
import { GoalComposePill } from './GoalComposePill'
import { FileSearchPopover } from './FileSearchPopover'
import { AttachmentStrip } from './AttachmentStrip'
import { ComposerCommandTrigger } from './ComposerCommandTrigger'
import { SparkIcon } from '../ui/AppIcons'
import { RichInputArea, type RichInputAreaHandle } from './RichInputArea'
import { ModelPicker, type ReasoningQuickValue } from './ModelPicker'
import { ChatGptUsageBadge } from './ChatGptUsageBadge'
import { ApprovalPolicyPicker, type VisibleApprovalPolicy } from './ApprovalPolicyPicker'
import {
  ComposerCustomProfileLabel,
  ComposerPlanModeLabel,
  ComposerSendButton,
  ComposerShell,
  SendIcon,
  SendProcessingIcon,
  composerModelPillStyle
} from './ComposerShell'
import { ComposerWorkspaceFooter, type ComposerWorkspaceMode } from './ComposerWorkspaceFooter'
import { ProfilePickerPopover } from './ProfilePickerPopover'
import { useResolvedProfileAvatar } from '../../stores/agentProfileAvatarStore'
import { ActionTooltip } from '../ui/ActionTooltip'
import { Skeleton } from '../ui/Skeleton'
import { PillSwitch } from '../ui/PillSwitch'
import { ACTION_SHORTCUTS } from '../ui/shortcutKeys'
import { VoiceInputControl, VoiceInputStatus } from './VoiceInputControl'
import { registerComposerVoiceTarget } from '../../voice/composerDraftBridge'
import { isVoiceProcessingForThread, shouldUseCompactVoiceFooter, useVoiceStore } from '../../voice/voiceStore'
import type { WorkspaceConfigChangedPayload } from '../../utils/workspaceConfigChanged'
import { openAppHandoff } from '../plugins/AppBindingPanel'
import { AppBindingPickerRow, AppBindingsPicker, isAppReadyForBindingPicker } from './AppBindingsPicker'
import {
  configObjectFromWorkspaceCore,
  resolveConcreteApprovalPolicyFromConfig,
  resolveWorkspaceProviderFromConfig,
  type WorkspaceCoreConfigLike
} from '../../utils/workspaceCoreConfig'
import {
  createManualModelPreference,
  findProviderPreference,
  readProviderPreferences,
  setProviderPreference,
  toContractProviderPreferences,
  type ModelPreference,
  type ProviderPreferences
} from '../../../shared/modelPreference'
import {
  createCatalogDefaultPreference,
  normalizePreferenceForModel
} from './PreferenceModelPicker'

interface ConversationWelcomeProps {
  workspacePath: string
  identityWorkspacePath?: string
  projectKey?: string
  remoteWorkspace?: boolean
  workspaceConfigChange?: WorkspaceConfigChangedPayload | null
  workspaceConfigChangeSeq?: number
}

interface ResolvedReasoningConfig {
  enabled: boolean
  effort: ReasoningEffortWire
  output: ReasoningOutputWire
}

const DEFAULT_REASONING_CONFIG: ResolvedReasoningConfig = {
  enabled: false,
  effort: 'medium',
  output: 'full'
}

interface Suggestion {
  icon: ComponentType<{ size?: number; strokeWidth?: number; style?: CSSProperties }>
  title: string
  prompt: string
}

interface WelcomeSuggestionWireItem {
  title?: string
  prompt?: string
  reason?: string
}

interface WelcomeSuggestionsWireResult {
  items?: WelcomeSuggestionWireItem[]
  source?: string
  fingerprint?: string
}

type SuggestionsStatus = 'idle' | 'loading' | 'ready'

const MAX_TEXT_LENGTH = 100_000
const MAX_IMAGES = 5
const MAX_IMAGE_BYTES = 10 * 1024 * 1024
const WELCOME_DRAFT_DEBOUNCE_MS = 250
function sanitizeSuggestionTitle(raw: string): string {
  const original = raw.trim()
  if (!original) return ''

  let sanitized = original
    .replace(/`+/g, '')
    .replace(/\*\*([^*]+)\*\*/g, '$1')
    .replace(/\*([^*]+)\*/g, '$1')
    .replace(/(^|\s)__([^_]+)__(?=\s|$)/g, '$1$2')
    .replace(/(^|\s)_([^_]+)_(?=\s|$)/g, '$1$2')
    .replace(/\s+/g, ' ')
    .trim()

  if (!sanitized) sanitized = original
  return sanitized
}

function normalizeWelcomeApprovalPolicy(value: unknown): VisibleApprovalPolicy | null {
  if (value === 'autoApprove') return 'autoApprove'
  if (value === 'prompt') return 'prompt'
  return null
}

function normalizeContextWindowMode(value: unknown): ContextWindowMode | null {
  if (typeof value !== 'string') return null
  const normalized = value.trim().toLowerCase()
  if (normalized === 'max' || normalized === 'maximum') return 'max'
  if (normalized === 'default') return 'default'
  return null
}

function normalizeContextWindowConfig(value: unknown): ContextWindowConfigurationWire | null {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return null
  const obj = value as Record<string, unknown>
  const mode = normalizeContextWindowMode(obj.mode ?? obj.Mode)
  return mode == null ? null : { mode }
}

/**
 * Welcome state when the workspace is connected but no thread is selected.
 * Keeps the composer centered in the page so users can start a conversation
 * without clicking New Thread first; quick-start rows prefill the composer.
 */
export function ConversationWelcome({
  workspacePath,
  identityWorkspacePath,
  projectKey,
  remoteWorkspace = false,
  workspaceConfigChange = null,
  workspaceConfigChangeSeq = 0
}: ConversationWelcomeProps): JSX.Element {
  const t = useT()
  const identityPath = identityWorkspacePath || workspacePath
  const draftProjectKey = projectKey || workspacePath
  const voiceThreadId = `welcome-composer:${draftProjectKey}`
  const [contentRevision, setContentRevision] = useState(0)
  const [images, setImages] = useState<ImageAttachment[]>([])
  const [files, setFiles] = useState<ComposerFileAttachment[]>([])
  const [dragOver, setDragOver] = useState(false)
  const [editorFocused, setEditorFocused] = useState(false)
  const [hoveredIdx, setHoveredIdx] = useState<number | null>(null)
  const [starting, setStarting] = useState(false)
  const [mascotBounce, setMascotBounce] = useState(0)
  const [dynamicSuggestions, setDynamicSuggestions] = useState<Suggestion[] | null>(null)
  const [suggestionsStatus, setSuggestionsStatus] = useState<SuggestionsStatus>('idle')
  const [atQuery, setAtQuery] = useState<string | null>(null)
  const [mentionDismissed, setMentionDismissed] = useState(false)
  const [slashQuery, setSlashQuery] = useState<string | null>(null)
  const [slashDismissed, setSlashDismissed] = useState(false)
  const [commandQuery, setCommandQuery] = useState<string | null>(null)
  const [skillQuery, setSkillQuery] = useState<string | null>(null)
  const [skillDismissed, setSkillDismissed] = useState(false)
  const [goalComposeMode, setGoalComposeMode] = useState(false)
  /** Agent/plan before a thread exists; applied when the first thread is created. */
  const [welcomeMode, setWelcomeMode] = useState<ThreadMode>('agent')
  const [welcomeWorkspaceMode, setWelcomeWorkspaceMode] = useState<ComposerWorkspaceMode>('local')
  const [welcomeBaseRef, setWelcomeBaseRef] = useState<string | null>(null)
  const [welcomeWorktreeBranchName, setWelcomeWorktreeBranchName] = useState<string | null>(null)
  /** Perforce changelist pre-selected on the welcome screen; applied when the first thread is created. */
  const [welcomeChangelist, setWelcomeChangelist] = useState<string>('default')
  useEffect(() => {
    setWelcomeChangelist('default')
  }, [identityPath])
  const [welcomeApprovalPolicy, setWelcomeApprovalPolicy] = useState<VisibleApprovalPolicy>('prompt')
  const [welcomeDefaultApprovalPolicy, setWelcomeDefaultApprovalPolicy] = useState<VisibleApprovalPolicy>('prompt')
  const [welcomeApprovalPolicyDirty, setWelcomeApprovalPolicyDirty] = useState(false)
  const [modelName, setModelName] = useState<string>('Default')
  const [providerId, setProviderId] = useState<string>('')
  const [reasoningConfig, setReasoningConfig] = useState<ResolvedReasoningConfig>(DEFAULT_REASONING_CONFIG)
  const [speedValue, setSpeedValue] = useState<InferenceSpeedWire>('standard')
  const [welcomeContextMode, setWelcomeContextMode] = useState<ContextWindowMode>('default')
  const [welcomeContextExplicit, setWelcomeContextExplicit] = useState(false)
  const [modelApplying, setModelApplying] = useState(false)
  const [welcomeSuggestionsConfigReady, setWelcomeSuggestionsConfigReady] = useState(false)
  const [welcomeSuggestionsEnabled, setWelcomeSuggestionsEnabled] = useState(true)
  const [skillCatalogReady, setSkillCatalogReady] = useState(false)
  const sendInFlightRef = useRef(false)
  const skipDraftPersistRef = useRef(false)
  const draftHydratedRef = useRef(false)
  const draftHydratingRef = useRef(false)
  const userEditedBeforeHydrationRef = useRef(false)
  const latestDraftTextRef = useRef('')
  const latestDraftSegmentsRef = useRef<ComposerDraftSegment[]>([])
  const latestDraftSelectionRef = useRef<{ start: number; end: number } | null>(null)
  const welcomeApprovalPolicyRef = useRef<VisibleApprovalPolicy>('prompt')
  const welcomeApprovalPolicyDirtyRef = useRef(false)
  const initialWelcomeDraftRef = useRef(useUIStore.getState().getWelcomeDraftForWorkspace(draftProjectKey))
  const workspaceLlmConfigResolvedRef = useRef(false)
  const workspaceProviderFromConfigRef = useRef<string | null>(null)
  const workspaceModelFromConfigRef = useRef<string | null>(null)
  const suggestionFingerprintRef = useRef<string | null>(null)
  const suggestionRequestSeqRef = useRef(0)
  const richRef = useRef<RichInputAreaHandle>(null)
  const voiceRecording = useVoiceStore((state) => state.recording?.threadId === voiceThreadId)
  const voiceProcessing = useVoiceStore((state) => isVoiceProcessingForThread(
    state.snapshot,
    state.finalizing?.threadId,
    voiceThreadId
  ))
  const compactVoiceFooter = useVoiceStore((state) => shouldUseCompactVoiceFooter(
    state.snapshot,
    state.recording?.threadId,
    state.finalizing?.threadId,
    voiceThreadId
  ))
  useEffect(() => {
    welcomeApprovalPolicyRef.current = welcomeApprovalPolicy
  }, [welcomeApprovalPolicy])
  const setWelcomeApprovalPolicyFromUser = useCallback((nextPolicy: VisibleApprovalPolicy): void => {
    const dirty = nextPolicy !== welcomeDefaultApprovalPolicy
    welcomeApprovalPolicyDirtyRef.current = dirty
    setWelcomeApprovalPolicyDirty(dirty)
    setWelcomeApprovalPolicy(nextPolicy)
  }, [welcomeDefaultApprovalPolicy])
  const connectionStatus = useConnectionStore((s) => s.status)
  const capabilities = useConnectionStore((s) => s.capabilities)
  const locale = useLocale()
  const modelCatalog = useModelCatalogStore((s) => s.models)
  const modelOptions = useModelCatalogStore((s) => s.modelOptions)
  const modelCatalogStatus = useModelCatalogStore((s) => s.status)
  const modelListUnsupportedEndpoint = useModelCatalogStore((s) => s.modelListUnsupportedEndpoint)
  const modelCatalogErrorCode = useModelCatalogStore((s) => s.errorCode)
  const modelCatalogErrorMessage = useModelCatalogStore((s) => s.errorMessage)
  const loadModels = useModelCatalogStore((s) => s.loadIfNeeded)
  const activeCatalogProviderId = useModelCatalogStore((s) => s.providerId)
  const activeChatGptProvider = useChatGptOAuthSummary(activeCatalogProviderId)
  const reloadProviders = useProvidersStore((s) => s.reload)
  const providerOptions = useProvidersStore((s) => s.providers)
  const { addThread, setActiveThreadId } = useThreadStore()
  const setWelcomeDraft = useUIStore((s) => s.setWelcomeDraft)
  const clearWelcomeDraft = useUIStore((s) => s.clearWelcomeDraft)
  const setWelcomeDraftWorkspace = useUIStore((s) => s.setWelcomeDraftWorkspace)
  const appBindingApps = useAppBindingStore((s) => s.apps)
  const appBindingAppsLoading = useAppBindingStore((s) => s.appsSurface === 'welcome' && s.appsLoading)
  const appBindingAppsError = useAppBindingStore((s) => s.appsSurface === 'welcome' ? s.appsError : null)
  const fetchAppBindings = useAppBindingStore((s) => s.fetchApps)
  const createAppBindingRequest = useAppBindingStore((s) => s.createBindingRequest)
  const waitForThreadAppBinding = useAppBindingStore((s) => s.waitForThreadBinding)
  const [welcomeAppIds, setWelcomeAppIds] = useState<string[]>([])
  const [welcomeAppSelectionTouched, setWelcomeAppSelectionTouched] = useState(false)

  const isConnected = connectionStatus === 'connected'
  const openingWorkspace = connectionStatus === 'connecting'
  const busy = starting || !isConnected
  const showMentionPopover = atQuery !== null && !mentionDismissed && !remoteWorkspace
  const canUseCommandPicker = capabilities?.commandManagement === true
  const canUseSkillPicker = capabilities?.skillsManagement === true
  const canUseThreadGoals = capabilities?.threadGoals === true
  const canUseAppBinding = capabilities?.appBindingVersion === 2
  const canUseAgentProfiles = capabilities?.agentProfileManagement === true
  // A profile chosen via /Profile before sending; applied to the thread that the first message creates.
  const [profilePickerOpen, setProfilePickerOpen] = useState(false)
  const [selectedProfileId, setSelectedProfileId] = useState<string | null>(null)
  // Honor the profile's configured (stored) avatar, falling back to the derived
  // one — same resolution as the composer/picker/gallery (see store).
  const resolvedProfileAvatar = useResolvedProfileAvatar(selectedProfileId ?? undefined, workspacePath)
  const canUseSystemActions = true
  const canUseSlashPicker = canUseCommandPicker || canUseSkillPicker || canUseThreadGoals || canUseSystemActions
  const normalizedSlashQuery = slashQuery?.toLowerCase() ?? null
  const isExactSystemSlashQuery = normalizedSlashQuery === 'plan' || normalizedSlashQuery === 'agent' || normalizedSlashQuery === 'init'
  const showSlashPopover = slashQuery !== null && !slashDismissed && canUseSlashPicker && !isExactSystemSlashQuery
  const showCommandQueryPopover = commandQuery !== null && canUseSlashPicker
  const showCommandPopover = showSlashPopover || showCommandQueryPopover
  const commandPopoverQuery = commandQuery ?? slashQuery ?? ''
  const showSkillPopover = skillQuery !== null && !skillDismissed && canUseSkillPicker
  const { commands: customCommands, initAvailable, status: customCommandStatus } = useCustomCommandCatalog({
    enabled: canUseCommandPicker,
    locale
  })
  const skills = useSkillsStore((s) => s.skills)
  const skillsLoading = useSkillsStore((s) => s.loading)
  const fetchSkills = useSkillsStore((s) => s.fetchSkills)
  const availableSkills = useMemo(
    () =>
      skills
        .filter((skill) => skill.available)
        .map((skill) => ({
          name: skill.name.replace(/^\/+/, ''),
          description: skill.description
        }))
        .sort((a, b) => a.name.localeCompare(b.name)),
    [skills]
  )

  useEffect(() => {
    setWelcomeDraftWorkspace(draftProjectKey)
  }, [draftProjectKey, setWelcomeDraftWorkspace])
  const richRefCatalog = useMemo(
    () => ({
      commands: customCommands,
      skills: availableSkills
    }),
    [availableSkills, customCommands]
  )
  const systemActions = useMemo(
    () => {
      const actions = []
      if (canUseCommandPicker && initAvailable) {
        actions.push({
          id: 'init',
          label: t('cmd.init'),
          description: t('composer.system.init.description'),
          keywords: ['init', 'agents'],
          icon: <FileText size={15} strokeWidth={2} aria-hidden />
        })
      }
      // A profile-backed thread runs its agent's fixed capability scope, so it has no Plan/Agent mode.
      if (!selectedProfileId) {
        actions.push({
          id: 'planMode',
          label: t('composer.system.plan'),
          description: welcomeMode === 'agent'
            ? t('composer.system.plan.enable')
            : t('composer.system.plan.disable'),
          keywords: ['plan', 'agent', '计划'],
          icon: <ListChecks size={15} strokeWidth={2} aria-hidden />
        })
      }
      if (canUseAgentProfiles) {
        actions.push({
          id: 'profile',
          label: t('composer.system.profile'),
          description: t('composer.system.profile.description'),
          keywords: ['profile', 'agent', 'custom'],
          icon: <Bot size={15} strokeWidth={2} aria-hidden />
        })
      }
      if (canUseThreadGoals) {
        actions.push({
          id: 'goal',
          label: t('goal.system.label'),
          description: t('goal.system.description'),
          keywords: ['goal', '目标'],
          icon: <Target size={15} strokeWidth={2} aria-hidden />
        })
      }
      return actions
    },
    [canUseAgentProfiles, canUseCommandPicker, canUseThreadGoals, initAvailable, selectedProfileId, t, welcomeMode]
  )
  const modelApiAvailable =
    isConnected &&
    capabilities?.modelCatalogManagement === true &&
    capabilities?.workspaceConfigManagement === true
  const modelLoading = modelApiAvailable && modelCatalogStatus === 'loading'
  const activeCatalogItem = modelCatalog.find((item) => item.id === modelName)
  const contextSupportsMax = activeCatalogItem?.contextWindow?.supportsMax === true
  const contextConfiguredWindow = activeCatalogItem?.contextWindow?.configuredWindow ?? 0
  const workspaceConfigPath = useMemo(() => {
    if (!workspacePath) return ''
    const normalized = workspacePath.replace(/[\\/]+$/, '')
    const sep = normalized.includes('\\') ? '\\' : '/'
    return `${normalized}${sep}.craft${sep}config.json`
  }, [workspacePath])

  useEffect(() => {
    if (!canUseAppBinding || !isConnected) return
    void fetchAppBindings(null, false, 'welcome')
  }, [canUseAppBinding, fetchAppBindings, isConnected])

  useEffect(() => {
    if (!isConnected || capabilities?.providerManagement !== true) return
    void reloadProviders()
  }, [capabilities?.providerManagement, isConnected, reloadProviders])

  const welcomeApps = useMemo(
    () => appBindingApps
      .filter(isAppReadyForBindingPicker)
      .sort((a, b) => a.displayName.localeCompare(b.displayName)),
    [appBindingApps]
  )

  useEffect(() => {
    setWelcomeAppIds((current) => {
      const appsById = new Map(welcomeApps.map((app) => [app.appId, app]))
      const next = welcomeAppSelectionTouched
        ? current.filter((appId) => {
            const app = appsById.get(appId)
            return app != null
          })
        : welcomeApps
            .filter((app) => app.connectionState === 'connected'
              && app.requiresExternalConnection !== false
              && app.managed !== true)
            .map((app) => app.appId)
      return sameStringArray(current, next) ? current : next
    })
  }, [welcomeAppSelectionTouched, welcomeApps])

  const toggleWelcomeApp = useCallback((appId: string, selected: boolean): void => {
    setWelcomeAppSelectionTouched(true)
    setWelcomeAppIds((current) => {
      if (selected) return current.includes(appId) ? current : [...current, appId]
      return current.filter((candidate) => candidate !== appId)
    })
  }, [])

  const retryWelcomeApps = useCallback(async (): Promise<void> => {
    await fetchAppBindings(null, true, 'welcome')
  }, [fetchAppBindings])

  const startWelcomeAppBindings = useCallback(async (threadId: string): Promise<void> => {
    if (welcomeAppIds.length === 0) return
    for (const appId of welcomeAppIds) {
      const selectedApp = useAppBindingStore.getState().apps.find((app) => app.appId === appId)
        ?? welcomeApps.find((app) => app.appId === appId)
      if (!selectedApp) {
        throw new Error(t('appBinding.welcomeAppNotConnected', { name: appId }))
      }
      if (selectedApp.requiresExternalConnection !== false && selectedApp.connectionState !== 'connected') {
        throw new Error(t('appBinding.welcomeAppNotConnected', { name: selectedApp.displayName || appId }))
      }

      const result = await createAppBindingRequest({
        threadId,
        appId: selectedApp.appId,
        source: 'welcome'
      })
      if (result.handoff?.uri) await openAppHandoff(result.handoff, t)
      if (result.state !== 'active') addToast(t('appBinding.bindingStarted'), 'info')
      await waitForThreadAppBinding({
        threadId,
        appId: selectedApp.appId,
        bindingRequestId: result.bindingRequestId
      })
    }
  }, [createAppBindingRequest, t, waitForThreadAppBinding, welcomeAppIds, welcomeApps])

  const readWorkspaceConfig = useCallback(async (): Promise<Record<string, unknown>> => {
    if (remoteWorkspace) {
      const getCore = window.api.workspaceConfig?.getCore
      if (typeof getCore !== 'function') return {}
      return configObjectFromWorkspaceCore(await getCore() as WorkspaceCoreConfigLike)
    }
    if (!workspaceConfigPath) return {}
    const raw = await window.api.file.readFile(workspaceConfigPath)
    return parseJsonConfig<Record<string, unknown>>(raw, {})
  }, [remoteWorkspace, workspaceConfigPath])

  const readEffectiveWorkspaceConfig = useCallback(async (): Promise<Record<string, unknown>> => {
    const getCore = window.api.workspaceConfig?.getCore
    if (typeof getCore === 'function') {
      return configObjectFromWorkspaceCore(await getCore() as WorkspaceCoreConfigLike)
    }
    return readWorkspaceConfig()
  }, [readWorkspaceConfig])

  const readWorkspaceProviderPreferences = useCallback(async (): Promise<ProviderPreferences> => {
    if (remoteWorkspace) {
      const getCore = window.api.workspaceConfig?.getCore
      if (typeof getCore !== 'function') return {}
      const core = await getCore() as WorkspaceCoreConfigLike
      return readProviderPreferences(core.workspace?.providerPreferences)
    }
    const config = await readWorkspaceConfig()
    return readProviderPreferences(getCaseInsensitiveConfigValue(config, 'ProviderPreferences'))
  }, [readWorkspaceConfig, remoteWorkspace])

  const getCaseInsensitiveValue = useCallback((record: Record<string, unknown>, key: string): unknown => {
    const expected = key.toLowerCase()
    for (const [candidate, value] of Object.entries(record)) {
      if (candidate.toLowerCase() === expected) return value
    }
    return undefined
  }, [])

  const resolveWelcomeSuggestionsEnabled = useCallback((cfg: Record<string, unknown>): boolean => {
    const section = getCaseInsensitiveValue(cfg, 'WelcomeSuggestions')
    if (section == null || typeof section !== 'object' || Array.isArray(section)) {
      return true
    }
    const enabled = getCaseInsensitiveValue(section as Record<string, unknown>, 'Enabled')
    return typeof enabled === 'boolean' ? enabled : true
  }, [getCaseInsensitiveValue])

  useEffect(() => {
    let disposed = false
    const applyResolvedDefault = (nextDefault: VisibleApprovalPolicy): void => {
      setWelcomeDefaultApprovalPolicy(nextDefault)

      const explicitDraftPolicy = normalizeWelcomeApprovalPolicy(initialWelcomeDraftRef.current?.approvalPolicy)
      if (explicitDraftPolicy) {
        if (!welcomeApprovalPolicyDirtyRef.current) {
          welcomeApprovalPolicyDirtyRef.current = true
          setWelcomeApprovalPolicyDirty(true)
          setWelcomeApprovalPolicy(explicitDraftPolicy)
        }
        return
      }

      if (!welcomeApprovalPolicyDirtyRef.current) {
        setWelcomeApprovalPolicy(nextDefault)
        return
      }

      if (welcomeApprovalPolicyRef.current === nextDefault) {
        welcomeApprovalPolicyDirtyRef.current = false
        setWelcomeApprovalPolicyDirty(false)
      }
    }

    const loadDefaultApprovalPolicy = async (): Promise<void> => {
      try {
        const cfg = await readWorkspaceConfig()
        if (!disposed) applyResolvedDefault(resolveConcreteApprovalPolicyFromConfig(cfg))
      } catch {
        if (!disposed) applyResolvedDefault('prompt')
      }
    }

    void loadDefaultApprovalPolicy()
    return () => {
      disposed = true
    }
  }, [readWorkspaceConfig, workspaceConfigChange, workspaceConfigChangeSeq])

  const suggestions: Suggestion[] = useMemo(
    () => [
      {
        icon: FileText,
        title: t('welcome.suggestion.explore'),
        prompt:
          'Give me a quick overview of this project: what it does, its structure, and where the main entry points are.'
      },
      {
        icon: Bug,
        title: t('welcome.suggestion.bug'),
        prompt:
          'Scan the codebase for potential bugs, error-prone patterns, or unhandled edge cases and suggest fixes.'
      },
      {
        icon: Sparkles,
        title: t('welcome.suggestion.feature'),
        prompt:
          'Help me design and implement a new feature for this project. Describe what you want to build.'
      },
      {
        icon: BookText,
        title: t('welcome.suggestion.docs'),
        prompt:
          'Generate clear documentation for this codebase: README sections, inline comments, and API docs.'
      }
    ],
    [t]
  )

  const welcomeSuggestionsSupported = capabilities?.extensions != null
    && typeof capabilities.extensions === 'object'
    && capabilities.extensions !== null
    && (capabilities.extensions as Record<string, unknown>).welcomeSuggestions === true

  useEffect(() => {
    let disposed = false
    const loadFlag = async (): Promise<void> => {
      if (!workspaceConfigPath) {
        if (!disposed) {
          setWelcomeSuggestionsEnabled(true)
          setWelcomeSuggestionsConfigReady(true)
        }
        return
      }

      try {
        const cfg = await readWorkspaceConfig()
        if (!disposed) {
          setWelcomeSuggestionsEnabled(resolveWelcomeSuggestionsEnabled(cfg))
          setWelcomeSuggestionsConfigReady(true)
        }
      } catch {
        if (!disposed) {
          setWelcomeSuggestionsEnabled(true)
          setWelcomeSuggestionsConfigReady(true)
        }
      }
    }

    void loadFlag()
    return () => {
      disposed = true
    }
  }, [readWorkspaceConfig, resolveWelcomeSuggestionsEnabled, workspaceConfigPath])

  useEffect(() => {
    if (workspaceConfigChange == null || workspaceConfigChangeSeq === 0) return
    if (!workspaceConfigChange.regions.includes('welcomeSuggestions')) return

    let disposed = false
    void readWorkspaceConfig()
      .then((cfg) => {
        if (!disposed) {
          setWelcomeSuggestionsEnabled(resolveWelcomeSuggestionsEnabled(cfg))
          setWelcomeSuggestionsConfigReady(true)
        }
      })
      .catch(() => {
        if (!disposed) {
          setWelcomeSuggestionsEnabled(true)
          setWelcomeSuggestionsConfigReady(true)
        }
      })

    return () => {
      disposed = true
    }
  }, [
    readWorkspaceConfig,
    readWorkspaceProviderPreferences,
    resolveWelcomeSuggestionsEnabled,
    workspaceConfigChange,
    workspaceConfigChangeSeq
  ])

  useEffect(() => {
    if (welcomeSuggestionsEnabled) return
    suggestionRequestSeqRef.current += 1
    suggestionFingerprintRef.current = null
    setDynamicSuggestions(null)
    setSuggestionsStatus('idle')
  }, [welcomeSuggestionsEnabled])

  useEffect(() => {
    const requestSeq = ++suggestionRequestSeqRef.current

    if (
      !welcomeSuggestionsConfigReady ||
      !isConnected ||
      !identityPath ||
      !welcomeSuggestionsSupported ||
      !welcomeSuggestionsEnabled
    ) {
      setDynamicSuggestions(null)
      suggestionFingerprintRef.current = null
      setSuggestionsStatus('idle')
      return
    }

    setSuggestionsStatus('loading')
    void window.api.appServer.sendRequest('welcome/suggestions', {
      identity: {
        channelName: 'dotcraft-desktop',
        userId: 'local',
        channelContext: `workspace:${identityPath}`,
        workspacePath: identityPath
      },
      maxItems: 4
    }).then((raw) => {
      if (requestSeq !== suggestionRequestSeqRef.current) return

      const result = raw as WelcomeSuggestionsWireResult
      if (result.source !== 'dynamic' || !Array.isArray(result.items) || result.items.length === 0) {
        suggestionFingerprintRef.current = null
        setDynamicSuggestions(null)
        setSuggestionsStatus('idle')
        return
      }
      if (result.fingerprint && result.fingerprint === suggestionFingerprintRef.current) {
        setSuggestionsStatus('ready')
        return
      }

      const mapped = result.items
        .map((item) => {
          const title = typeof item.title === 'string' ? sanitizeSuggestionTitle(item.title) : ''
          const prompt = typeof item.prompt === 'string' ? item.prompt.trim() : ''
          if (!title || !prompt) return null
          return {
            icon: SparkIcon,
            title,
            prompt
          } satisfies Suggestion
        })
        .filter((item): item is Suggestion => item !== null)

      if (mapped.length === 0) {
        setSuggestionsStatus('idle')
        return
      }
      suggestionFingerprintRef.current = typeof result.fingerprint === 'string' ? result.fingerprint : null
      setDynamicSuggestions(mapped)
      setSuggestionsStatus('ready')
    }).catch(() => {
      if (requestSeq !== suggestionRequestSeqRef.current) return
      suggestionFingerprintRef.current = null
      setDynamicSuggestions(null)
      setSuggestionsStatus('idle')
    })
  }, [
    isConnected,
    welcomeSuggestionsConfigReady,
    welcomeSuggestionsEnabled,
    welcomeSuggestionsSupported,
    identityPath,
    workspacePath
  ])

  const displayedSuggestions = dynamicSuggestions ?? suggestions

  const handleAtQuery = useCallback((q: string | null): void => {
    if (remoteWorkspace) {
      setAtQuery(null)
      setMentionDismissed(true)
      return
    }
    setAtQuery(q)
    if (q !== null) setMentionDismissed(false)
  }, [remoteWorkspace])

  const handleSlashQuery = useCallback((q: string | null): void => {
    setSlashQuery(q)
    if (q !== null) setSlashDismissed(false)
  }, [])

  const handleCommandQuery = useCallback((q: string | null): void => {
    setCommandQuery(q)
  }, [])

  const handleSkillQuery = useCallback((q: string | null): void => {
    setSkillQuery(q)
    if (q !== null) setSkillDismissed(false)
  }, [])

  const onSelectFile = useCallback((relativePath: string): void => {
    richRef.current?.insertFileTag(relativePath)
  }, [])

  const onSelectCommand = useCallback((commandName: string): void => {
    richRef.current?.insertCommandTag(commandName)
  }, [])

  const toggleWelcomeMode = useCallback((): void => {
    setWelcomeMode((m) => (m === 'agent' ? 'plan' : 'agent'))
  }, [])

  const enterGoalComposeMode = useCallback((): void => {
    setGoalComposeMode(true)
    window.setTimeout(() => richRef.current?.focus(), 0)
  }, [])

  const onSelectSkill = useCallback((skillName: string): void => {
    richRef.current?.insertSkillTag(skillName)
  }, [])

  useEffect(() => {
    if (isConnected) {
      richRef.current?.focus()
    }
  }, [isConnected])

  useEffect(() => {
    if (!canUseSkillPicker) {
      setSkillCatalogReady(true)
      return
    }
    setSkillCatalogReady(false)
    void fetchSkills().finally(() => {
      setSkillCatalogReady(true)
    })
  }, [canUseSkillPicker, fetchSkills])

  useEffect(() => {
    const welcomeDraft = initialWelcomeDraftRef.current
    if (draftHydratedRef.current) return
    if (!welcomeDraft) {
      draftHydratedRef.current = true
      return
    }
    if (userEditedBeforeHydrationRef.current) {
      draftHydratedRef.current = true
      return
    }
    const hasStructuredSegments = Array.isArray(welcomeDraft.segments) && welcomeDraft.segments.length > 0
    const commandCatalogReady =
      !canUseCommandPicker ||
      customCommandStatus === 'ready' ||
      customCommandStatus === 'error'
    const refsCatalogReady = hasStructuredSegments || (commandCatalogReady && skillCatalogReady)
    if (!refsCatalogReady) return

    draftHydratingRef.current = true
    try {
      richRef.current?.setContent({
        text: welcomeDraft.text,
        segments: welcomeDraft.segments
      })
      richRef.current?.setSelectionRange({
        start: welcomeDraft.selectionStart ?? welcomeDraft.text.length,
        end: welcomeDraft.selectionEnd ?? welcomeDraft.selectionStart ?? welcomeDraft.text.length
      })
    } finally {
      draftHydratingRef.current = false
    }
    latestDraftSelectionRef.current = {
      start: welcomeDraft.selectionStart ?? welcomeDraft.text.length,
      end: welcomeDraft.selectionEnd ?? welcomeDraft.selectionStart ?? welcomeDraft.text.length
    }
    latestDraftTextRef.current = welcomeDraft.text
    latestDraftSegmentsRef.current = richRef.current?.getSegments() ?? [...(welcomeDraft.segments ?? [])]
    setImages(welcomeDraft.images)
    setFiles([...(welcomeDraft.files ?? [])])
    setWelcomeMode(welcomeDraft.mode)
    const explicitDraftApprovalPolicy = normalizeWelcomeApprovalPolicy(welcomeDraft.approvalPolicy)
    if (explicitDraftApprovalPolicy && !welcomeApprovalPolicyDirtyRef.current) {
      welcomeApprovalPolicyDirtyRef.current = true
      setWelcomeApprovalPolicyDirty(true)
      setWelcomeApprovalPolicy(explicitDraftApprovalPolicy)
    }
    const explicitDraftContextWindow = normalizeContextWindowConfig(welcomeDraft.contextWindow)
    if (explicitDraftContextWindow) {
      setWelcomeContextMode(explicitDraftContextWindow.mode ?? 'default')
      setWelcomeContextExplicit(true)
    }
    const useResolvedWorkspacePair = workspaceLlmConfigResolvedRef.current
    setProviderId(
      useResolvedWorkspacePair && workspaceProviderFromConfigRef.current != null
        ? workspaceProviderFromConfigRef.current
        : (welcomeDraft.providerId?.trim() ?? '')
    )
    setModelName(
      useResolvedWorkspacePair && workspaceModelFromConfigRef.current != null
        ? workspaceModelFromConfigRef.current
        : (welcomeDraft.model || 'Default')
    )
    setReasoningConfig(readReasoningObject(welcomeDraft.reasoning) ?? DEFAULT_REASONING_CONFIG)
    if (welcomeDraft.speed != null) setSpeedValue(welcomeDraft.speed === 'fast' ? 'fast' : 'standard')
    if (Array.isArray(welcomeDraft.appIds)) {
      setWelcomeAppIds([...welcomeDraft.appIds])
      setWelcomeAppSelectionTouched(true)
    }
    setContentRevision((n) => n + 1)
    draftHydratedRef.current = true
  }, [canUseCommandPicker, customCommandStatus, skillCatalogReady])

  useEffect(() => {
    let disposed = false
    const loadWorkspaceDefaults = async (): Promise<void> => {
      const workspacePreferenceChanged =
        workspaceConfigChangeSeq > 0 &&
        workspaceConfigChange?.regions.some((region) =>
          region === 'workspace.provider' || region === 'workspace.providerPreferences'
        ) === true
      const hasInitialDraft = initialWelcomeDraftRef.current != null
      if (!workspaceConfigPath) {
        if (!hasInitialDraft || workspacePreferenceChanged) {
          workspaceLlmConfigResolvedRef.current = true
          workspaceProviderFromConfigRef.current = ''
          workspaceModelFromConfigRef.current = 'Default'
          setProviderId('')
          setModelName('Default')
          setReasoningConfig(DEFAULT_REASONING_CONFIG)
          setSpeedValue('standard')
          setWelcomeContextMode('default')
          setWelcomeContextExplicit(false)
        }
        return
      }

      try {
        const cfg = await readEffectiveWorkspaceConfig()
        if (disposed) return
        const nextProviderId = resolveWorkspaceProviderFromConfig(cfg)
        // A concrete workspace provider is authoritative even when a draft exists. This keeps
        // Settings changes from reviving a stale provider/model pair on the Welcome screen.
        if (!hasInitialDraft || workspacePreferenceChanged || nextProviderId !== '') {
          let nextPreference = findProviderPreference(
            readProviderPreferences(getCaseInsensitiveConfigValue(cfg, 'ProviderPreferences')),
            nextProviderId
          )
          workspaceLlmConfigResolvedRef.current = true
          workspaceProviderFromConfigRef.current = nextProviderId
          if (nextProviderId) await loadModels(false, nextProviderId)
          if (disposed) return
          if (!nextPreference) {
            const catalogState = useModelCatalogStore.getState()
            const firstModel = catalogState.models[0]
            const nextModel = firstModel?.id ?? catalogState.modelOptions[0] ?? ''
            if (nextModel && nextProviderId) {
              nextPreference = createCatalogDefaultPreference(firstModel, nextModel)
              const providerPreferences = setProviderPreference(
                await readWorkspaceProviderPreferences(),
                nextProviderId,
                nextPreference
              )
              await window.api.appServer.sendRequest('workspace/config/update', {
                providerId: nextProviderId,
                providerPreferences: toContractProviderPreferences(providerPreferences)
              })
            }
          }
          const resolved = nextPreference ?? createManualModelPreference('')
          workspaceModelFromConfigRef.current = resolved.model || 'Default'
          setProviderId(nextProviderId)
          setModelName(resolved.model || 'Default')
          setReasoningConfig(resolved.reasoning)
          setSpeedValue(resolved.speed)
          setWelcomeContextMode(resolved.contextWindow.mode)
          setWelcomeContextExplicit(false)
        }
      } catch {
        if (!disposed) {
          if (!hasInitialDraft || workspacePreferenceChanged) {
            workspaceLlmConfigResolvedRef.current = true
            workspaceProviderFromConfigRef.current = ''
            workspaceModelFromConfigRef.current = 'Default'
            setProviderId('')
            setModelName('Default')
            setReasoningConfig(DEFAULT_REASONING_CONFIG)
            setSpeedValue('standard')
            setWelcomeContextMode('default')
            setWelcomeContextExplicit(false)
          }
        }
      }
    }

    void loadWorkspaceDefaults()
    return () => {
      disposed = true
    }
  }, [
    readEffectiveWorkspaceConfig,
    loadModels,
    readWorkspaceProviderPreferences,
    workspaceConfigChange,
    workspaceConfigChangeSeq,
    workspaceConfigPath
  ])

  const buildWelcomeContextWindowConfig = useCallback((): ContextWindowConfigurationWire | undefined => {
    return welcomeContextExplicit ? { mode: welcomeContextMode } : undefined
  }, [welcomeContextExplicit, welcomeContextMode])

  const flushWelcomeDraft = useCallback((): void => {
    if (skipDraftPersistRef.current) return
    const text = richRef.current?.getText() ?? latestDraftTextRef.current
    const segments = richRef.current?.getSegments() ?? latestDraftSegmentsRef.current
    const selection = latestDraftSelectionRef.current ?? richRef.current?.getSelectionRange()
    const hasText = text.trim().length > 0
    const hasImages = images.length > 0
    const hasFiles = files.length > 0
    const model = modelName || 'Default'
    const hasCustomReasoning = reasoningConfig.enabled
      || reasoningConfig.effort !== DEFAULT_REASONING_CONFIG.effort
      || reasoningConfig.output !== DEFAULT_REASONING_CONFIG.output
    const hasCustomSettings = welcomeMode !== 'agent'
      || model !== 'Default'
      || welcomeApprovalPolicyDirty
      || hasCustomReasoning
      || welcomeContextExplicit
      || welcomeAppSelectionTouched
    const fallbackCaret = text.length

    if (!hasText && !hasImages && !hasFiles && !hasCustomSettings) {
      clearWelcomeDraft(draftProjectKey)
      return
    }

    setWelcomeDraft({
      text,
      segments: [...segments],
      selectionStart: selection?.start ?? fallbackCaret,
      selectionEnd: selection?.end ?? fallbackCaret,
      images: [...images],
      files: [...files],
      mode: welcomeMode,
      providerId,
      model,
      reasoning: reasoningConfig,
      speed: speedValue,
      contextWindow: buildWelcomeContextWindowConfig(),
      approvalPolicy: welcomeApprovalPolicy,
      appIds: welcomeAppSelectionTouched ? [...welcomeAppIds] : undefined
    }, draftProjectKey)
  }, [buildWelcomeContextWindowConfig, clearWelcomeDraft, draftProjectKey, files, images, modelName, providerId, reasoningConfig, setWelcomeDraft, speedValue, welcomeAppIds, welcomeAppSelectionTouched, welcomeApprovalPolicy, welcomeApprovalPolicyDirty, welcomeContextExplicit, welcomeMode])

  useEffect(() => {
    if (!draftHydratedRef.current) return
    const timer = setTimeout(() => {
      flushWelcomeDraft()
    }, WELCOME_DRAFT_DEBOUNCE_MS)
    return () => {
      clearTimeout(timer)
    }
  }, [contentRevision, files, flushWelcomeDraft, images, modelName, reasoningConfig, speedValue, welcomeAppIds, welcomeAppSelectionTouched, welcomeApprovalPolicy, welcomeApprovalPolicyDirty, welcomeContextExplicit, welcomeContextMode, welcomeMode])

  useEffect(() => {
    return () => {
      if (!draftHydratedRef.current || skipDraftPersistRef.current) return
      flushWelcomeDraft()
    }
  }, [flushWelcomeDraft])

  const switchWelcomeWorkspace = useCallback(async (nextWorkspacePath: string): Promise<void> => {
    if (nextWorkspacePath === workspacePath) return
    flushWelcomeDraft()
    await window.api.workspace.switch(nextWorkspacePath)
    useUIStore.getState().setWelcomeDraftWorkspace(nextWorkspacePath)
  }, [flushWelcomeDraft, workspacePath])

  const persistWelcomePreference = useCallback(async (
    nextPreference: ModelPreference,
    nextProviderId = providerId
  ): Promise<void> => {
    if (!workspaceConfigPath || !nextProviderId.trim() || !nextPreference.model.trim()) return
    const providerPreferences = setProviderPreference(
      await readWorkspaceProviderPreferences(),
      nextProviderId,
      nextPreference
    )
    await window.api.appServer.sendRequest('workspace/config/update', {
      providerId: nextProviderId,
      providerPreferences: toContractProviderPreferences(providerPreferences)
    })
  }, [providerId, readWorkspaceProviderPreferences, workspaceConfigPath])

  const handleModelChange = useCallback(
    async (nextModel: string): Promise<void> => {
      if (!workspaceConfigPath || !nextModel || nextModel === 'Default' || nextModel === modelName) return
      setModelApplying(true)
      const previousModel = modelName
      const previousReasoning = reasoningConfig
      const previousContext = welcomeContextMode
      const nextPreference = normalizePreferenceForModel({
        model: nextModel,
        reasoning: { ...reasoningConfig },
        speed: speedValue,
        contextWindow: { mode: welcomeContextMode }
      }, modelCatalog)
      setModelName(nextPreference.model)
      setReasoningConfig(nextPreference.reasoning)
      setWelcomeContextMode(nextPreference.contextWindow.mode)
      try {
        await persistWelcomePreference(nextPreference)
      } catch (err) {
        const msg = err instanceof Error ? err.message : String(err)
        setModelName(previousModel)
        setReasoningConfig(previousReasoning)
        setWelcomeContextMode(previousContext)
        addToast(`Failed to save model: ${msg}`, 'error')
      } finally {
        setModelApplying(false)
      }
    },
    [
      modelCatalog,
      modelName,
      persistWelcomePreference,
      reasoningConfig,
      speedValue,
      welcomeContextMode,
      workspaceConfigPath
    ]
  )

  const handleProviderChange = useCallback(async (nextProviderId: string): Promise<void> => {
    if (!workspaceConfigPath || !nextProviderId || nextProviderId === providerId) return
    setModelApplying(true)
    const previousProvider = providerId
    const previousModel = modelName
    try {
      const cfg = await readEffectiveWorkspaceConfig()
      await loadModels(true, nextProviderId)
      const catalogState = useModelCatalogStore.getState()
      const remembered = findProviderPreference(
        readProviderPreferences(getCaseInsensitiveConfigValue(cfg, 'ProviderPreferences')),
        nextProviderId
      )
      const nextPreference = remembered
        ? normalizePreferenceForModel(remembered, catalogState.models)
        : createCatalogDefaultPreference(catalogState.models[0], catalogState.modelOptions[0] ?? '')
      if (!nextPreference.model) {
        addToast(t('composer.providerModelUnavailable'), 'error')
        await loadModels(true, previousProvider)
        return
      }
      await persistWelcomePreference(nextPreference, nextProviderId)
      setProviderId(nextProviderId)
      setModelName(nextPreference.model)
      setReasoningConfig(nextPreference.reasoning)
      setSpeedValue(nextPreference.speed)
      setWelcomeContextMode(nextPreference.contextWindow.mode)
      setWelcomeContextExplicit(false)
    } catch (err) {
      setProviderId(previousProvider)
      setModelName(previousModel)
      await loadModels(true, previousProvider)
      addToast(`Failed to switch provider: ${err instanceof Error ? err.message : String(err)}`, 'error')
    } finally {
      setModelApplying(false)
    }
  }, [loadModels, modelName, persistWelcomePreference, providerId, readEffectiveWorkspaceConfig, t, workspaceConfigPath])

  const handleReasoningChange = useCallback(
    async (nextReasoning: ReasoningQuickValue): Promise<void> => {
      if (!workspaceConfigPath) return
      const nextPayload = buildReasoningPayload(nextReasoning, reasoningConfig)
      setModelApplying(true)
      const previousReasoning = reasoningConfig
      setReasoningConfig(nextPayload ?? DEFAULT_REASONING_CONFIG)
      try {
        const fallback = createCatalogDefaultPreference(
          modelCatalog.find((item) => item.id === modelName),
          modelName
        ).reasoning
        await persistWelcomePreference({
          model: modelName,
          reasoning: nextPayload ?? fallback,
          speed: speedValue,
          contextWindow: { mode: welcomeContextMode }
        })
      } catch (err) {
        const msg = err instanceof Error ? err.message : String(err)
        setReasoningConfig(previousReasoning)
        addToast(`Failed to save thinking: ${msg}`, 'error')
      } finally {
        setModelApplying(false)
      }
    },
    [modelCatalog, modelName, persistWelcomePreference, reasoningConfig, speedValue, welcomeContextMode, workspaceConfigPath]
  )

  const handleSpeedChange = useCallback(async (nextSpeed: InferenceSpeedWire): Promise<void> => {
    if (!workspaceConfigPath || nextSpeed === speedValue) return
    const previousSpeed = speedValue
    setModelApplying(true)
    setSpeedValue(nextSpeed)
    try {
      await persistWelcomePreference({
        model: modelName,
        reasoning: { ...reasoningConfig },
        speed: nextSpeed,
        contextWindow: { mode: welcomeContextMode }
      })
    } catch (err) {
      setSpeedValue(previousSpeed)
      addToast(`Failed to save speed: ${err instanceof Error ? err.message : String(err)}`, 'error')
    } finally {
      setModelApplying(false)
    }
  }, [modelName, persistWelcomePreference, reasoningConfig, speedValue, welcomeContextMode, workspaceConfigPath])

  const handleContextModeChange = useCallback(async (nextMode: ContextWindowMode): Promise<void> => {
    const previousMode = welcomeContextMode
    setWelcomeContextExplicit(true)
    setWelcomeContextMode(nextMode)
    setModelApplying(true)
    try {
      await persistWelcomePreference({
        model: modelName,
        reasoning: { ...reasoningConfig },
        speed: speedValue,
        contextWindow: { mode: nextMode }
      })
    } catch (err) {
      setWelcomeContextMode(previousMode)
      addToast(`Failed to save context window: ${err instanceof Error ? err.message : String(err)}`, 'error')
    } finally {
      setModelApplying(false)
    }
  }, [modelName, persistWelcomePreference, reasoningConfig, speedValue, welcomeContextMode])

  useEffect(() => {
    if (!welcomeContextExplicit || welcomeContextMode !== 'max') return
    if (modelCatalogStatus !== 'ready' || contextSupportsMax) return
    setWelcomeContextMode('default')
  }, [contextSupportsMax, modelCatalogStatus, welcomeContextExplicit, welcomeContextMode])

  const saveDataUrlAsTemp = useCallback(
    async (dataUrl: string, fileName: string, mimeType: string): Promise<void> => {
      if (remoteWorkspace) {
        addToast(t('input.remoteLocalFilesUnavailable'), 'warning')
        return
      }
      const baseLen = dataUrl.split(',')[1]?.length ?? 0
      const approxBytes = Math.floor((baseLen * 3) / 4)
      if (approxBytes > MAX_IMAGE_BYTES) {
        addToast(
          t('welcomeComposer.imageTooLarge', { mb: MAX_IMAGE_BYTES / 1024 / 1024 }),
          'warning'
        )
        return
      }
      if (images.length >= MAX_IMAGES) {
        addToast(t('welcomeComposer.maxImages', { max: MAX_IMAGES }), 'warning')
        return
      }
      try {
        const { path } = await window.api.workspace.saveImageToTemp({ dataUrl, fileName })
        setImages((prev) => [...prev, { tempPath: path, dataUrl, fileName, mimeType }])
      } catch (e) {
        const msg = e instanceof Error ? e.message : String(e)
        addToast(t('welcomeComposer.saveImageFailed', { error: msg }), 'error')
      }
    },
    [images.length, remoteWorkspace, t]
  )

  const showGoalUnavailable = useCallback((): void => {
    addToast(t('goal.toast.unsupported'), 'warning')
  }, [t])

  const startWelcomeThread = useCallback(async (): Promise<ThreadSummary> => {
    const identity = {
      channelName: 'dotcraft-desktop',
      userId: 'local',
      channelContext: `workspace:${identityPath}`,
      workspacePath: identityPath
    }
    const config = providerId && modelName && modelName !== 'Default'
      ? { providerId, model: modelName }
      : undefined

    // New chats snapshot the local Project's folders as runtime roots; cwd defaults
    // to the primary (WorkspacePath). Omitted for single-folder / remote workspaces.
    // Worktree mode deliberately omits this: worktree/createAndStart does not accept
    // runtime roots, so the first turn/start establishes them and Session Core
    // retargets the primary root to the created worktree while preserving the
    // secondary roots (see specs/features/multi-folder-projects.md §5).
    const runtimeWorkspaceRoots = runtimeWorkspaceRootsFor(identityPath)
    const rootsField = runtimeWorkspaceRoots ? { runtimeWorkspaceRoots } : {}

    const thread = welcomeWorkspaceMode === 'worktree'
      ? (await window.api.appServer.sendRequest('worktree/createAndStart', {
          identity,
          historyMode: 'server',
          baseRef: welcomeBaseRef || undefined,
          branchName: welcomeWorktreeBranchName || undefined,
          config
        }, 180_000) as unknown as { thread: ThreadSummary }).thread
      : (await window.api.appServer.sendRequest('thread/start', {
          identity,
          historyMode: 'server',
          config,
          ...rootsField
        }) as unknown as { thread: ThreadSummary }).thread

    // Apply a welcome pre-selected Perforce changelist to the new thread (non-default only).
    if (welcomeChangelist && welcomeChangelist !== 'default') {
      try {
        await usePerforceChangelistStore.getState().setTarget(thread.id, welcomeChangelist)
      } catch {
        // Non-fatal: the thread still starts; its target stays on the default changelist.
      }
    }
    return thread
  }, [identityPath, modelName, providerId, welcomeBaseRef, welcomeChangelist, welcomeWorkspaceMode, welcomeWorktreeBranchName])

  // Apply a profile chosen via /Profile to the freshly created thread (the only method that lands the
  // profile's compiled config). No-op when no profile was selected.
  const applyWelcomeProfile = useCallback(async (threadId: string, profileId: string | null): Promise<void> => {
    if (!profileId) return
    await window.api.appServer.sendRequest('agent/profiles/refreshThread', { threadId, profileId })
  }, [])

  const createGoalBackedThread = useCallback(async (objective: string): Promise<boolean> => {
    if (!canUseThreadGoals) {
      showGoalUnavailable()
      return false
    }
    const trimmedObjective = objective.trim()
    if (!trimmedObjective) {
      addToast(t('goal.toast.emptyObjective'), 'warning')
      return false
    }
    if (connectionStatus !== 'connected' || sendInFlightRef.current || modelLoading) {
      return false
    }

    sendInFlightRef.current = true
    setStarting(true)
    setMascotBounce((n) => n + 1)
    // A profile-backed thread runs its agent's fixed posture (no Plan/Agent mode).
    const capturedMode = selectedProfileId ? 'agent' : welcomeMode
    const capturedApprovalPolicy = welcomeApprovalPolicy
    const capturedModel = modelName === 'Default' ? '' : modelName
    const capturedReasoning = reasoningConfig
    const capturedContextWindow = buildWelcomeContextWindowConfig()
    const capturedProfileId = selectedProfileId
    let createdThreadId: string | null = null
    try {
      const thread = await startWelcomeThread()
      createdThreadId = thread.id
      await applyWelcomeProfile(thread.id, capturedProfileId)

      const goalResult = await window.api.appServer.sendRequest('thread/goal/set', {
        threadId: thread.id,
        objective: trimmedObjective
      })
      const goal = extractGoal(goalResult)
      await startWelcomeAppBindings(thread.id)
      const { inputParts } = buildComposerInputParts({ text: trimmedObjective })

      skipDraftPersistRef.current = true
      latestDraftTextRef.current = ''
      latestDraftSegmentsRef.current = []
      latestDraftSelectionRef.current = null
      clearWelcomeDraft(draftProjectKey)
      richRef.current?.clear()
      setImages([])
      setFiles([])

      addThread(goal ? { ...thread, goal } : thread)
      if (goal) {
        useThreadStore.getState().setThreadGoal(goal)
      }
      useUIStore.getState().setPendingWelcomeTurn({
        threadId: thread.id,
        text: trimmedObjective,
        inputParts,
        mode: capturedMode,
        approvalPolicy: capturedApprovalPolicy,
        model: capturedModel,
        reasoning: capturedReasoning,
        contextWindow: capturedContextWindow,
        sentAsGoal: true
      })
      setActiveThreadId(thread.id)
      useUIStore.getState().setActiveMainView('conversation')
      return true
    } catch (err) {
      if (createdThreadId) await deleteUnusedWelcomeThread(createdThreadId)
      addToast(t('goal.toast.updateFailed', { error: err instanceof Error ? err.message : String(err) }), 'error')
      return false
    } finally {
      sendInFlightRef.current = false
      setStarting(false)
    }
  }, [
    addThread,
    applyWelcomeProfile,
    buildWelcomeContextWindowConfig,
    canUseThreadGoals,
    clearWelcomeDraft,
    connectionStatus,
    draftProjectKey,
    modelLoading,
    selectedProfileId,
    setActiveThreadId,
    showGoalUnavailable,
    startWelcomeAppBindings,
    startWelcomeThread,
    t,
    welcomeApprovalPolicy,
    welcomeMode,
    modelName,
    reasoningConfig
  ])

  const executeWelcomeGoalCommand = useCallback(async (command: GoalSlashCommand): Promise<boolean> => {
    if (!canUseThreadGoals) {
      showGoalUnavailable()
      return false
    }
    if (command.kind === 'show') {
      enterGoalComposeMode()
      return true
    }
    if (command.kind === 'set') return createGoalBackedThread(command.objective)
    addToast(t('goal.toast.noCurrent'), 'warning')
    return false
  }, [canUseThreadGoals, createGoalBackedThread, enterGoalComposeMode, showGoalUnavailable, t])

  const sendFromWelcome = useCallback(async (draftOverride?: ThreadComposerDraftInput): Promise<void> => {
    const text = draftOverride?.text ?? richRef.current?.getText() ?? ''
    const segments = draftOverride?.segments ?? richRef.current?.getSegments() ?? []
    const inputImages = draftOverride?.images ?? images
    const inputFiles = draftOverride?.files ?? files
    const trimmed = text.trim()
    const isInitCommand = trimmed.toLowerCase() === '/init'
    if (
      (!trimmed && inputImages.length === 0 && inputFiles.length === 0) ||
      sendInFlightRef.current ||
      connectionStatus !== 'connected' ||
      modelLoading
    ) {
      return
    }
    if (remoteWorkspace && (inputImages.length > 0 || inputFiles.length > 0)) {
      addToast(t('input.remoteLocalFilesUnavailable'), 'warning')
      return
    }

    if (goalComposeMode) {
      const objective = buildGoalObjective({ text, segments, files: inputFiles, images: inputImages })
      if (!objective.trim()) {
        addToast(t('goal.toast.emptyObjective'), 'warning')
        return
      }
      const created = await createGoalBackedThread(objective)
      if (created) setGoalComposeMode(false)
      return
    }

    const systemCommand = parseWelcomeSystemSlashCommand(trimmed)
    if (systemCommand) {
      setWelcomeMode(systemCommand.kind)
      richRef.current?.clear()
      setImages([])
      setFiles([])
      return
    }

    const goalCommand = parseGoalSlashCommand(trimmed)
    if (goalCommand) {
      const clearInput = await executeWelcomeGoalCommand(goalCommand)
      if (clearInput) {
        richRef.current?.clear()
        setImages([])
        setFiles([])
      }
      return
    }

    sendInFlightRef.current = true
    setStarting(true)
    setMascotBounce((n) => n + 1)
    const capturedImages = [...inputImages]
    const capturedFiles = [...inputFiles]
    // A profile-backed thread runs its agent's fixed posture (no Plan/Agent mode).
    const capturedMode = selectedProfileId ? 'agent' : welcomeMode
    const capturedApprovalPolicy = welcomeApprovalPolicy
    const capturedModel = modelName === 'Default' ? '' : modelName
    const capturedReasoning = reasoningConfig
    const capturedContextWindow = buildWelcomeContextWindowConfig()
    const capturedProfileId = selectedProfileId
    let createdThreadId: string | null = null
    try {
      const thread = await startWelcomeThread()
      createdThreadId = thread.id
      await applyWelcomeProfile(thread.id, capturedProfileId)
      await startWelcomeAppBindings(thread.id)
      const turnText = isInitCommand ? await expandInitCommand(thread.id) : trimmed

      skipDraftPersistRef.current = true
      latestDraftTextRef.current = ''
      latestDraftSegmentsRef.current = []
      latestDraftSelectionRef.current = null
      clearWelcomeDraft(draftProjectKey)
      const { inputParts } = buildComposerInputParts({
        text: turnText,
        segments: isInitCommand ? [] : segments,
        files: capturedFiles,
        images: capturedImages
      })
      useUIStore.getState().setPendingWelcomeTurn({
        threadId: thread.id,
        text: turnText,
        inputParts,
        images: capturedImages.length > 0 ? capturedImages : undefined,
        files: capturedFiles.length > 0 ? capturedFiles : undefined,
        mode: capturedMode,
        approvalPolicy: capturedApprovalPolicy,
        model: capturedModel,
        reasoning: capturedReasoning,
        contextWindow: capturedContextWindow
      })
      addThread(thread)
      setActiveThreadId(thread.id)
      useUIStore.getState().setActiveMainView('conversation')
      richRef.current?.clear()
      setImages([])
      setFiles([])
    } catch (err) {
      console.error('Failed to start thread from welcome composer:', err)
      if (createdThreadId) await deleteUnusedWelcomeThread(createdThreadId)
      addToast(err instanceof Error ? err.message : String(err), 'error')
    } finally {
      sendInFlightRef.current = false
      setStarting(false)
    }
  }, [
    files,
    images,
    connectionStatus,
    addThread,
    applyWelcomeProfile,
    buildWelcomeContextWindowConfig,
    selectedProfileId,
    setActiveThreadId,
    startWelcomeAppBindings,
    startWelcomeThread,
    welcomeApprovalPolicy,
    welcomeMode,
    modelName,
    reasoningConfig,
    modelLoading,
    clearWelcomeDraft,
    draftProjectKey,
    executeWelcomeGoalCommand,
    goalComposeMode,
    createGoalBackedThread,
    remoteWorkspace,
    t
  ])

  const captureWelcomeVoiceDraft = useCallback((): ThreadComposerDraftInput => ({
    text: richRef.current?.getText() ?? latestDraftTextRef.current,
    segments: richRef.current?.getSegments() ?? latestDraftSegmentsRef.current,
    images: [...images],
    files: [...files]
  }), [files, images])

  const applyWelcomeVoiceDraft = useCallback((draft: ThreadComposerDraftInput): void => {
    richRef.current?.setContent({ text: draft.text, segments: draft.segments })
    richRef.current?.setSelectionRange({ start: draft.text.length, end: draft.text.length })
    latestDraftTextRef.current = draft.text
    latestDraftSegmentsRef.current = [...draft.segments]
    latestDraftSelectionRef.current = { start: draft.text.length, end: draft.text.length }
    setImages([...draft.images])
    setFiles([...draft.files])
    setContentRevision((revision) => revision + 1)
    useComposerDraftStore.getState().clearDraft(voiceThreadId)
  }, [voiceThreadId])

  useEffect(() => registerComposerVoiceTarget(voiceThreadId, {
    capture: captureWelcomeVoiceDraft,
    apply: applyWelcomeVoiceDraft,
    submit: sendFromWelcome
  }), [applyWelcomeVoiceDraft, captureWelcomeVoiceDraft, sendFromWelcome, voiceThreadId])

  useEffect(() => () => {
    void useVoiceStore.getState().discardOrigin(voiceThreadId)
  }, [voiceThreadId])

  const onSelectSystemAction = useCallback((actionId: string): void => {
    setSlashDismissed(true)
    richRef.current?.removeCommandQuery()
    if (actionId === 'init') {
      richRef.current?.setContent({ text: '/init', segments: [] })
      void sendFromWelcome()
      return
    }
    if (actionId === 'planMode') {
      toggleWelcomeMode()
      return
    }
    if (actionId === 'profile') {
      setProfilePickerOpen(true)
      return
    }
    if (actionId !== 'goal') return
    enterGoalComposeMode()
  }, [enterGoalComposeMode, sendFromWelcome, toggleWelcomeMode])

  const onPasteImage = useCallback(
    (file: File): void => {
      if (remoteWorkspace) {
        addToast(t('input.remoteLocalFilesUnavailable'), 'warning')
        return
      }
      if (!isImageFile(file)) return
      const reader = new FileReader()
      reader.onload = () => {
        const dataUrl = reader.result as string
        void saveDataUrlAsTemp(dataUrl, file.name, file.type || 'image/png')
      }
      reader.readAsDataURL(file)
    },
    [remoteWorkspace, saveDataUrlAsTemp, t]
  )

  const onDragOver = useCallback((e: React.DragEvent): void => {
    e.preventDefault()
    e.stopPropagation()
    if (remoteWorkspace) return
    setDragOver(true)
  }, [remoteWorkspace])

  const onDragLeave = useCallback((e: React.DragEvent): void => {
    e.preventDefault()
    e.stopPropagation()
    setDragOver(false)
  }, [])

  const attachImages = useCallback((picked: File[]): void => {
    if (remoteWorkspace) {
      if (picked.length > 0) addToast(t('input.remoteLocalFilesUnavailable'), 'warning')
      return
    }
    for (const file of picked) {
      onPasteImage(file)
    }
  }, [onPasteImage, remoteWorkspace, t])

  const onDrop = useCallback(
    (e: React.DragEvent): void => {
      e.preventDefault()
      e.stopPropagation()
      setDragOver(false)
      if (remoteWorkspace) {
        addToast(t('input.remoteLocalFilesUnavailable'), 'warning')
        return
      }
      const { imageFiles, fileAttachments, skippedCount } = classifyDroppedComposerFiles(
        e.dataTransfer,
        window.api.workspace.getPathForFile
      )
      attachImages(imageFiles)
      if (fileAttachments.length > 0) {
        setFiles((prev) => mergeComposerFileAttachments(prev, fileAttachments))
      }
      if (skippedCount > 0) {
        addToast(t('input.dropItemsSkipped', { count: skippedCount }), 'warning')
      }
    },
    [attachImages, remoteWorkspace, t]
  )

  function fillSuggestion(prompt: string): void {
    richRef.current?.setPlainText(prompt)
    setTimeout(() => {
      latestDraftSelectionRef.current = {
        start: prompt.length,
        end: prompt.length
      }
      richRef.current?.setSelectionRange({
        start: prompt.length,
        end: prompt.length
      })
    }, 0)
  }

  const canSend = useMemo(() => {
    const textLen = (richRef.current?.getText() ?? '').trim().length
    return (textLen > 0 || images.length > 0 || files.length > 0) && isConnected && !starting && !modelLoading
  }, [contentRevision, files.length, images.length, isConnected, starting, modelLoading])
  const canSendWithVoice = voiceRecording || (canSend && !voiceProcessing)
  const submitOrStopVoice = useCallback((): void => {
    if (voiceRecording) {
      void useVoiceStore.getState().stopRecording('send')
      return
    }
    void sendFromWelcome()
  }, [sendFromWelcome, voiceRecording])

  const mascotSpeed = speedValue === 'fast' && modelCatalog.some(
    (model) => model.id === modelName && model.speed?.supportedModes.includes('fast') === true
  )
    ? 'fast'
    : 'standard'

  return (
    <div
      style={{
        display: 'flex',
        flex: 1,
        flexDirection: 'column',
        position: 'relative',
        minHeight: 0,
        background: 'transparent',
        overflow: 'hidden'
      }}
    >
      {canUseAppBinding && isConnected && (
        <div style={welcomeAppButtonSlot}>
          <WelcomeAppBindingsButton
            apps={welcomeApps}
            selectedAppIds={welcomeAppIds}
            disabled={starting}
            loading={appBindingAppsLoading}
            error={appBindingAppsError}
            onRetry={retryWelcomeApps}
            onToggleApp={toggleWelcomeApp}
          />
        </div>
      )}
      <div
        style={{
          flex: 1,
          minHeight: 0,
          overflowY: 'auto',
          display: 'flex',
          justifyContent: 'center',
          padding: '48px 24px'
        }}
      >
        <div
          style={{
            width: '100%',
            maxWidth: 'var(--conversation-reading-width)',
            margin: 'auto',
            display: 'flex',
            flexDirection: 'column',
            alignItems: 'center'
          }}
        >
          <div
            style={{
              display: 'flex',
              flexDirection: 'column',
              alignItems: 'center',
              gap: '8px',
              marginBottom: '18px'
            }}
          >
            <h1
              style={{
                fontSize: 'var(--type-title-size)',
                lineHeight: 'var(--type-title-line-height)',
                fontWeight: 'var(--type-title-weight)',
                color: 'var(--text-primary)',
                margin: 0,
                letterSpacing: 0
              }}
            >
              {t('welcome.heroTitle')}
            </h1>
            <p style={{
              fontSize: 'var(--type-body-size)',
              lineHeight: 'var(--type-body-line-height)',
              fontWeight: 'var(--type-body-weight)',
              color: 'var(--text-secondary)',
              margin: 0,
              textAlign: 'center',
              width: 'min(520px, 100%)',
              maxWidth: '520px',
              minHeight: '20px'
            }}>
              {isConnected
                ? t('welcomeComposer.hint.select')
                : t('welcomeComposer.hint.connecting')}
            </p>
          </div>

          <div style={{ width: '100%' }}>
            <ComposerShell
              dragOver={dragOver}
              dropLabel={t('composer.dropImage')}
              onDragOver={onDragOver}
              onDragLeave={onDragLeave}
              onDrop={onDrop}
              opacity={starting ? 0.65 : 1}
              focused={editorFocused}
              showMascot
              mascotBounceSignal={mascotBounce}
              mascotReasoningEffort={reasoningConfig.enabled ? reasoningConfig.effort : 'off'}
              mascotSpeed={mascotSpeed}
              mascotContextMax={welcomeContextMode === 'max'}
              mascotAvatar={resolvedProfileAvatar}
              attachmentStrip={
                <AttachmentStrip
                  images={images}
                  files={files}
                  onRemoveImage={(idx) => {
                    setImages((prev) => prev.filter((_, i) => i !== idx))
                  }}
                  onRemoveFile={(idx) => {
                    setFiles((prev) => prev.filter((_, i) => i !== idx))
                  }}
                  removeImageLabel={t('composer.removeImageAria')}
                  removeFileLabel={t('composer.removeFileAria')}
                />
              }
              editor={
                <div style={{ position: 'relative' }}>
                  <div style={{ position: 'relative', minWidth: 0 }}>
                    <ProfilePickerPopover
                      visible={profilePickerOpen}
                      activeProfileId={selectedProfileId ?? undefined}
                      onPick={(profileId) => {
                        setSelectedProfileId(profileId)
                        setProfilePickerOpen(false)
                      }}
                      onDismiss={() => {
                        setProfilePickerOpen(false)
                      }}
                    />
                    <CommandSearchPopover
                      query={commandPopoverQuery}
                      visible={showCommandPopover}
                      loading={customCommandStatus === 'loading' || skillsLoading}
                      systemActions={systemActions}
                      commands={customCommands}
                      skills={availableSkills}
                      onSelectSystemAction={onSelectSystemAction}
                      onSelectCommand={onSelectCommand}
                      onSelectSkill={onSelectSkill}
                      onDismiss={() => {
                        if (commandQuery !== null) richRef.current?.endCommandQuery()
                        else setSlashDismissed(true)
                      }}
                    />
                    <CommandSearchPopover
                      query={skillQuery ?? ''}
                      visible={showSkillPopover}
                      loading={skillsLoading}
                      commands={[]}
                      skills={availableSkills}
                      onSelectCommand={() => {}}
                      onSelectSkill={onSelectSkill}
                      onDismiss={() => {
                        setSkillDismissed(true)
                      }}
                    />
                    <FileSearchPopover
                      query={atQuery ?? ''}
                      visible={showMentionPopover}
                      workspacePath={workspacePath}
                      onSelect={onSelectFile}
                      onDismiss={() => {
                        setMentionDismissed(true)
                      }}
                    />
                    <RichInputArea
                      ref={richRef}
                      chrome="minimal"
                      disabled={busy}
                      suppressSubmit={showMentionPopover || showCommandPopover || showSkillPopover || modelLoading}
                      onToggleModeShortcut={toggleWelcomeMode}
                      placeholder={
                        !isConnected
                          ? t('composer.placeholder.connecting')
                          : goalComposeMode
                            ? t('goal.objective.placeholder')
                            : t('welcomeComposer.placeholder.ask')
                      }
                      onSubmit={() => {
                        submitOrStopVoice()
                      }}
                      onAtQuery={remoteWorkspace ? undefined : handleAtQuery}
                      onSlashQuery={handleSlashQuery}
                      onCommandQuery={handleCommandQuery}
                      onSkillQuery={handleSkillQuery}
                      onContentChange={() => {
                        if (!draftHydratedRef.current && !draftHydratingRef.current) {
                          userEditedBeforeHydrationRef.current = true
                        }
                        latestDraftTextRef.current = richRef.current?.getText() ?? latestDraftTextRef.current
                        latestDraftSegmentsRef.current =
                          richRef.current?.getSegments() ?? latestDraftSegmentsRef.current
                        setContentRevision((n) => n + 1)
                      }}
                      onSelectionChange={(range) => {
                        if (range) {
                          latestDraftSelectionRef.current = range
                        }
                      }}
                      onFocusChange={setEditorFocused}
                      onPasteImage={onPasteImage}
                      onPasteTextOversized={() => {
                        addToast(
                          t('input.truncated', { max: MAX_TEXT_LENGTH.toLocaleString() }),
                          'warning'
                        )
                      }}
                      refCatalog={richRefCatalog}
                    />
                  </div>
                </div>
              }
              footerLeading={
                <div style={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: '10px',
                  minWidth: 0,
                  flex: compactVoiceFooter ? 1 : undefined,
                  flexWrap: compactVoiceFooter ? 'nowrap' : 'wrap'
                }}>
                  <ComposerCommandTrigger
                    label={t('composer.openCommands')}
                    expanded={showCommandPopover}
                    active={showCommandQueryPopover}
                    disabled={!canUseSlashPicker || busy}
                    onClick={() => {
                      if (showCommandPopover) {
                        if (commandQuery !== null) richRef.current?.endCommandQuery()
                        else setSlashDismissed(true)
                        return
                      }
                      setSlashDismissed(false)
                      richRef.current?.beginCommandQuery()
                    }}
                  />
                  <VoiceInputStatus threadId={voiceThreadId} />

                  {!compactVoiceFooter && (
                    <>
                      <ApprovalPolicyPicker
                        value={welcomeApprovalPolicy}
                        onChange={setWelcomeApprovalPolicyFromUser}
                        disabled={starting}
                      />

                      {selectedProfileId ? (
                        <ComposerCustomProfileLabel
                          label={t('composer.mode.custom')}
                          onClear={() => setSelectedProfileId(null)}
                          title={t('composer.customPill.title', { name: selectedProfileId })}
                          ariaLabel={t('composer.customPill.aria')}
                        />
                      ) : (
                        <ComposerPlanModeLabel
                          value={welcomeMode}
                          onDisable={() => {
                            setWelcomeMode('agent')
                          }}
                          label={t('composer.mode.plan')}
                          shortcut={ACTION_SHORTCUTS.toggleMode}
                          title={t('composer.planPill.create')}
                          ariaLabel={t('composer.system.plan.disable')}
                        />
                      )}

                      {canUseThreadGoals && goalComposeMode && (
                        <GoalComposePill
                          label={t('goal.system.label')}
                          title={t('goal.compose.active')}
                          ariaLabel={t('goal.compose.exit')}
                          onExit={() => setGoalComposeMode(false)}
                        />
                      )}
                    </>
                  )}
                </div>
              }
              footerAction={
                <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                  {!compactVoiceFooter && <ModelPicker
                    providerId={providerId}
                    providerOptions={providerOptions}
                    modelName={modelName}
                    modelOptions={modelApiAvailable ? modelOptions : []}
                    modelCatalog={modelCatalog}
                    reasoningValue={reasoningConfig.enabled ? reasoningConfig.effort : 'off'}
                    speedValue={speedValue}
                    loading={modelLoading}
                    unsupported={modelListUnsupportedEndpoint}
                    modelListReady={modelApiAvailable && modelCatalogStatus === 'ready' && modelOptions.length > 0}
                    errorMessage={
                      modelCatalogStatus === 'error'
                        ? (
                            modelCatalogErrorCode
                              ? `${modelCatalogErrorCode}: ${modelCatalogErrorMessage ?? ''}`.trim()
                              : (modelCatalogErrorMessage || t('composer.modelListError'))
                          )
                        : null
                    }
                    disabled={modelApplying || starting}
                    onChange={(nextModel) => {
                      void handleModelChange(nextModel)
                    }}
                    onProviderChange={(nextProviderId) => {
                      void handleProviderChange(nextProviderId)
                    }}
                    onReasoningChange={(nextReasoning) => {
                      void handleReasoningChange(nextReasoning)
                    }}
                    onSpeedChange={(nextSpeed) => {
                      void handleSpeedChange(nextSpeed)
                    }}
                    contextMode={welcomeContextMode}
                    contextSupportsMax={contextSupportsMax}
                    contextDegraded={false}
                    contextConfiguredWindow={contextConfiguredWindow}
                    onContextModeChange={handleContextModeChange}
                    onRetry={() => {
                      void loadModels(true, providerId)
                    }}
                    shortcut={ACTION_SHORTCUTS.selectModel}
                    triggerStyle={composerModelPillStyle(
                      modelApplying || starting || modelLoading
                        ? 'var(--composer-footer-muted)'
                        : 'var(--composer-footer-highlight)',
                      modelApplying || starting || modelLoading
                    )}
                  />}
                  <VoiceInputControl threadId={voiceThreadId} />
                  <ActionTooltip
                    label={starting ? t('welcome.startingAria') : t('welcome.sendAria')}
                    shortcut={canSendWithVoice ? ACTION_SHORTCUTS.send : undefined}
                    placement="top"
                  >
                    <ComposerSendButton
                      tone={canSendWithVoice ? 'enabled' : 'disabled'}
                      onClick={submitOrStopVoice}
                      disabled={!canSendWithVoice}
                      aria-label={starting ? t('welcome.startingAria') : t('welcome.sendAria')}
                      aria-busy={starting ? 'true' : undefined}
                    >
                      {starting ? <SendProcessingIcon /> : <SendIcon />}
                    </ComposerSendButton>
                  </ActionTooltip>
                </div>
              }
              belowFooter={
                openingWorkspace ? (
                  <WelcomeFooterSkeleton />
                ) : (
                  <ComposerWorkspaceFooter
                    workspacePath={workspacePath}
                    mode={welcomeWorkspaceMode}
                    variant="welcome"
                    remoteWorkspace={remoteWorkspace}
                    baseRef={welcomeBaseRef}
                    worktreeBranchName={welcomeWorktreeBranchName}
                    onWelcomeModeChange={(nextMode) => {
                      setWelcomeWorkspaceMode(nextMode)
                      if (nextMode === 'local') {
                        setWelcomeWorktreeBranchName(null)
                      }
                    }}
                    onBaseRefChange={setWelcomeBaseRef}
                    onWorktreeBranchNameChange={setWelcomeWorktreeBranchName}
                    onWelcomeWorkspaceChange={switchWelcomeWorkspace}
                    trailing={<ChatGptUsageBadge provider={activeChatGptProvider} />}
                    welcomeChangelist={welcomeChangelist}
                    onWelcomeChangelistChange={setWelcomeChangelist}
                  />
                )
              }
            />
          </div>

          <div
            style={{
              width: '100%',
              display: 'flex',
              flexDirection: 'column',
              marginTop: '8px',
              gap: '4px'
            }}
          >
            {openingWorkspace ? (
              <WelcomeSuggestionSkeletonList />
            ) : displayedSuggestions.map((s, idx) => {
              const Icon = s.icon
              return (
                <button
                  key={idx}
                  type="button"
                  onClick={() => { fillSuggestion(s.prompt) }}
                  disabled={busy}
                  onMouseEnter={() => setHoveredIdx(idx)}
                  onMouseLeave={() => setHoveredIdx(null)}
                  style={{
                    display: 'flex',
                    alignItems: 'center',
                    gap: '8px',
                    width: '100%',
                    minHeight: '34px',
                    boxSizing: 'border-box',
                    padding: '6px 10px',
                    margin: 0,
                    background: hoveredIdx === idx ? 'var(--bg-tertiary)' : 'transparent',
                    border: 'none',
                    borderRadius: '8px',
                    color: 'var(--text-secondary)',
                    cursor: busy ? 'default' : 'pointer',
                    textAlign: 'left',
                    fontSize: 'var(--type-ui-size)',
                    fontWeight: 'var(--type-ui-weight)',
                    lineHeight: 'var(--type-ui-line-height)',
                    transition: 'background-color 120ms ease, color 120ms ease',
                    opacity: busy ? 0.7 : 1
                  }}
                  onFocus={(e) => {
                    e.currentTarget.style.color = 'var(--text-primary)'
                  }}
                  onBlur={(e) => {
                    e.currentTarget.style.color = 'var(--text-secondary)'
                  }}
                  aria-label={s.title}
                >
                  <Icon size={16} strokeWidth={1.8} style={{ flexShrink: 0 }} />
                  <span
                    style={{
                      minWidth: 0,
                      overflow: 'hidden',
                      textOverflow: 'ellipsis',
                      whiteSpace: 'nowrap'
                    }}
                  >
                    {s.title}
                  </span>
                </button>
              )
            })}
          </div>
        </div>
      </div>
    </div>
  )
}

function WelcomeFooterSkeleton(): JSX.Element {
  const t = useT()
  return (
    <div
      role="status"
      aria-busy="true"
      aria-label={t('threadList.loading')}
      data-testid="welcome-footer-skeleton"
      style={{
        display: 'flex',
        alignItems: 'center',
        gap: '10px',
        minHeight: '28px',
        minWidth: 0,
        flexWrap: 'wrap'
      }}
    >
      <Skeleton width={104} height={18} radius={999} />
      <Skeleton width={112} height={18} radius={999} />
      <Skeleton width={168} height={18} radius={999} />
    </div>
  )
}

function WelcomeSuggestionSkeletonList(): JSX.Element {
  const t = useT()
  const rows = ['58%', '44%', '52%', '48%']
  return (
    <div
      role="status"
      aria-busy="true"
      aria-label={t('threadList.loading')}
      style={{
        width: '100%',
        display: 'flex',
        flexDirection: 'column',
        gap: '4px'
      }}
    >
      {rows.map((width, index) => (
        <div
          key={index}
          data-testid="welcome-suggestion-skeleton"
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: '8px',
            minHeight: '34px',
            padding: '6px 10px',
            boxSizing: 'border-box'
          }}
        >
          <Skeleton width={16} height={16} radius={4} />
          <Skeleton width={width} height={12} />
        </div>
      ))}
    </div>
  )
}

function WelcomeAppBindingsButton({
  apps,
  selectedAppIds,
  disabled,
  loading,
  error,
  onRetry,
  onToggleApp,
}: {
  apps: AppInfo[]
  selectedAppIds: string[]
  disabled: boolean
  loading: boolean
  error: string | null
  onRetry: () => Promise<void>
  onToggleApp: (appId: string, selected: boolean) => void
}): JSX.Element {
  const t = useT()
  const [open, setOpen] = useState(false)

  return (
    <AppBindingsPicker
      open={open}
      onOpenChange={setOpen}
      activeCount={selectedAppIds.length}
      disabled={disabled}
      loading={loading}
      error={error}
      empty={apps.length === 0}
      emptyLabel={t('appBinding.welcomeEmpty')}
      onRetry={() => { void onRetry() }}
      placement="welcome"
    >
      {apps.map((app) => {
        const selected = selectedAppIds.includes(app.appId)
        return (
          <AppBindingPickerRow
            key={app.appId}
            icon={<AppLogo app={app} />}
            title={app.displayName}
            action={(
              <PillSwitch
                checked={selected}
                onChange={(checked) => onToggleApp(app.appId, checked)}
                size="sm"
                disabled={disabled}
                aria-label={t('appBinding.welcomeUseApp', { name: app.displayName })}
              />
            )}
          />
        )
      })}
    </AppBindingsPicker>
  )
}

function AppLogo({ app }: { app: AppInfo }): JSX.Element {
  if (app.icon) return <img src={app.icon} alt="" style={welcomeAppLogoImg} />
  return (
    <span style={welcomeAppLogoFallback} aria-hidden>
      <Link2 size={15} />
    </span>
  )
}

const welcomeAppButtonSlot: CSSProperties = { position: 'absolute', top: 12, right: 16, zIndex: 8 }
const welcomeAppLogoImg: CSSProperties = { width: 30, height: 30, borderRadius: 7, objectFit: 'cover', background: 'var(--bg-tertiary)', border: '1px solid var(--border-default)' }
const welcomeAppLogoFallback: CSSProperties = { width: 30, height: 30, borderRadius: 7, display: 'inline-flex', alignItems: 'center', justifyContent: 'center', background: 'var(--bg-tertiary)', border: '1px solid var(--border-default)', color: 'var(--text-secondary)' }

function parseWelcomeSystemSlashCommand(text: string): { kind: 'agent' | 'plan' } | null {
  const trimmed = text.trim().toLowerCase()
  if (trimmed === '/plan') return { kind: 'plan' }
  if (trimmed === '/agent') return { kind: 'agent' }
  return null
}

function sameStringArray(left: string[], right: string[]): boolean {
  if (left.length !== right.length) return false
  return left.every((value, index) => value === right[index])
}

async function deleteUnusedWelcomeThread(threadId: string): Promise<void> {
  try {
    await window.api.appServer.sendRequest('thread/delete', { threadId })
  } catch {
    // Best effort cleanup only; preserving the user's draft matters more than surfacing this secondary failure.
  }
}

function buildReasoningPayload(
  value: ReasoningQuickValue,
  current: ResolvedReasoningConfig
): ResolvedReasoningConfig | null {
  if (value === 'default') return null
  if (value === 'off') {
    return {
      enabled: false,
      effort: current.effort || 'medium',
      output: current.output || 'full'
    }
  }
  return {
    enabled: true,
    effort: value,
    output: current.output || 'full'
  }
}

function readReasoningObject(value: unknown): ResolvedReasoningConfig | null {
  if (!value || typeof value !== 'object') return null
  const obj = value as Record<string, unknown>
  const enabledRaw = obj.enabled ?? obj.Enabled
  const effort = normalizeReasoningEffort(obj.effort ?? obj.Effort)
  const output = normalizeReasoningOutput(obj.output ?? obj.Output)
  return {
    enabled: typeof enabledRaw === 'boolean' ? enabledRaw : false,
    effort: effort ?? 'medium',
    output: output ?? 'full'
  }
}

function normalizeReasoningEffort(value: unknown): ReasoningEffortWire | null {
  if (typeof value !== 'string') return null
  const normalized = value.replace(/[-_\s]/g, '').toLowerCase()
  if (normalized === 'low') return 'low'
  if (normalized === 'medium') return 'medium'
  if (normalized === 'high') return 'high'
  if (normalized === 'extrahigh') return 'extraHigh'
  return null
}

function normalizeReasoningOutput(value: unknown): ReasoningOutputWire | null {
  if (typeof value !== 'string') return null
  const normalized = value.trim().toLowerCase()
  if (normalized === 'none') return 'none'
  if (normalized === 'summary') return 'summary'
  if (normalized === 'full') return 'full'
  return null
}

function getCaseInsensitiveConfigValue(record: Record<string, unknown>, key: string): unknown {
  const expected = key.toLowerCase()
  return Object.entries(record).find(([candidate]) => candidate.toLowerCase() === expected)?.[1]
}
