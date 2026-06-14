import { useCallback, useEffect, useMemo, useRef, useState, type ComponentType, type CSSProperties } from 'react'
import { BookText, Bot, Bug, ExternalLink, FileText, Link2, ListChecks, RefreshCw, Sparkles, Target } from 'lucide-react'
import { useLocale, useT } from '../../contexts/LocaleContext'
import { useConnectionStore } from '../../stores/connectionStore'
import { useModelCatalogStore, type ReasoningEffortWire, type ReasoningOutputWire } from '../../stores/modelCatalogStore'
import { useProvidersStore, useChatGptOAuthSummary } from '../../stores/providersStore'
import { useThreadStore } from '../../stores/threadStore'
import { useUIStore } from '../../stores/uiStore'
import { useSkillsStore } from '../../stores/skillsStore'
import { useAppBindingStore, type AppHandoff, type AppInfo } from '../../stores/appBindingStore'
import { addToast } from '../../stores/toastStore'
import { useCustomCommandCatalog } from '../../hooks/useCustomCommandCatalog'
import type { ComposerFileAttachment, ImageAttachment, ThreadMode } from '../../types/conversation'
import type { ComposerDraftSegment } from '../../types/composerDraft'
import type { ThreadSummary } from '../../types/thread'
import { parseJsonConfig } from '../../../shared/jsonConfig'
import {
  classifyDroppedComposerFiles,
  isImageFile,
  mergeComposerFileAttachments
} from '../../utils/composerAttachments'
import { buildComposerInputParts } from '../../utils/composeInputParts'
import { extractGoal, parseGoalSlashCommand, type GoalSlashCommand } from '../../utils/threadGoal'
import { CommandSearchPopover } from './CommandSearchPopover'
import { GoalControlPopover } from './GoalControlPopover'
import { FileSearchPopover } from './FileSearchPopover'
import { AttachmentStrip } from './AttachmentStrip'
import { ComposerAttachmentMenu } from './ComposerAttachmentMenu'
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
  composerModelPillStyle
} from './ComposerShell'
import { ComposerWorkspaceFooter, type ComposerWorkspaceMode } from './ComposerWorkspaceFooter'
import { ProfilePickerPopover } from './ProfilePickerPopover'
import { ActionTooltip } from '../ui/ActionTooltip'
import { PillSwitch } from '../ui/PillSwitch'
import { Skeleton } from '../ui/Skeleton'
import { ACTION_SHORTCUTS } from '../ui/shortcutKeys'
import type { WorkspaceConfigChangedPayload } from '../../utils/workspaceConfigChanged'
import { configObjectFromWorkspaceCore, type WorkspaceCoreConfigLike } from '../../utils/workspaceCoreConfig'

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
  const [skillQuery, setSkillQuery] = useState<string | null>(null)
  const [skillDismissed, setSkillDismissed] = useState(false)
  const [goalPopoverOpen, setGoalPopoverOpen] = useState(false)
  const [goalBusy, setGoalBusy] = useState(false)
  /** Agent/plan before a thread exists; applied when the first thread is created. */
  const [welcomeMode, setWelcomeMode] = useState<ThreadMode>('agent')
  const [welcomeWorkspaceMode, setWelcomeWorkspaceMode] = useState<ComposerWorkspaceMode>('local')
  const [welcomeBaseRef, setWelcomeBaseRef] = useState<string | null>(null)
  const [welcomeWorktreeBranchName, setWelcomeWorktreeBranchName] = useState<string | null>(null)
  const [welcomeApprovalPolicy, setWelcomeApprovalPolicy] = useState<VisibleApprovalPolicy>('default')
  const [modelName, setModelName] = useState<string>('Default')
  const [reasoningConfig, setReasoningConfig] = useState<ResolvedReasoningConfig>(DEFAULT_REASONING_CONFIG)
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
  const initialWelcomeDraftRef = useRef(useUIStore.getState().getWelcomeDraftForWorkspace(draftProjectKey))
  const workspaceLlmConfigChangedRef = useRef(false)
  const workspaceModelFromConfigRef = useRef<string | null>(null)
  const suggestionFingerprintRef = useRef<string | null>(null)
  const suggestionRequestSeqRef = useRef(0)
  const richRef = useRef<RichInputAreaHandle>(null)
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
  const { addThread, setActiveThreadId } = useThreadStore()
  const setWelcomeDraft = useUIStore((s) => s.setWelcomeDraft)
  const clearWelcomeDraft = useUIStore((s) => s.clearWelcomeDraft)
  const setWelcomeDraftWorkspace = useUIStore((s) => s.setWelcomeDraftWorkspace)
  const appBindingApps = useAppBindingStore((s) => s.apps)
  const fetchAppBindings = useAppBindingStore((s) => s.fetchApps)
  const startAppConnection = useAppBindingStore((s) => s.startConnection)
  const createAppBindingRequest = useAppBindingStore((s) => s.createBindingRequest)
  const waitForAppConnection = useAppBindingStore((s) => s.waitForConnection)
  const waitForThreadAppBinding = useAppBindingStore((s) => s.waitForThreadBinding)
  const [welcomeAppIds, setWelcomeAppIds] = useState<string[]>([])
  const [welcomeAppUserDisabledIds, setWelcomeAppUserDisabledIds] = useState<Set<string>>(() => new Set())
  const [welcomeAppBusyId, setWelcomeAppBusyId] = useState<string | null>(null)

  const isConnected = connectionStatus === 'connected'
  const openingWorkspace = connectionStatus === 'connecting'
  const busy = starting || !isConnected
  const showMentionPopover = atQuery !== null && !mentionDismissed && !remoteWorkspace
  const canUseCommandPicker = capabilities?.commandManagement === true
  const canUseSkillPicker = capabilities?.skillsManagement === true
  const canUseThreadGoals = capabilities?.threadGoals === true
  const canUseAppBinding = capabilities?.appBinding === true
  const canUseAgentProfiles = capabilities?.agentProfileManagement === true
  // A profile chosen via /Profile before sending; applied to the thread that the first message creates.
  const [profilePickerOpen, setProfilePickerOpen] = useState(false)
  const [selectedProfileId, setSelectedProfileId] = useState<string | null>(null)
  const canUseSystemActions = true
  const canUseSlashPicker = canUseCommandPicker || canUseSkillPicker || canUseThreadGoals || canUseSystemActions
  const remoteLocalFilesUnavailable = remoteWorkspace ? t('input.remoteLocalFilesUnavailable') : undefined
  const normalizedSlashQuery = slashQuery?.toLowerCase() ?? null
  const isExactSystemSlashQuery = normalizedSlashQuery === 'plan' || normalizedSlashQuery === 'agent'
  const showSlashPopover = slashQuery !== null && !slashDismissed && canUseSlashPicker && !isExactSystemSlashQuery
  const showSkillPopover = skillQuery !== null && !skillDismissed && canUseSkillPicker
  const { commands: customCommands, status: customCommandStatus } = useCustomCommandCatalog({
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
      // A profile-backed thread runs its agent's fixed capability scope, so it has no Plan/Agent mode.
      if (!selectedProfileId) {
        actions.push({
          id: 'planMode',
          label: t('composer.system.plan'),
          description: welcomeMode === 'agent'
            ? t('composer.system.plan.enable')
            : t('composer.system.plan.disable'),
          keywords: ['plan', 'agent', '计划'],
          icon: <ListChecks size={11} strokeWidth={2} aria-hidden />
        })
      }
      if (canUseAgentProfiles) {
        actions.push({
          id: 'profile',
          label: t('composer.system.profile'),
          description: t('composer.system.profile.description'),
          keywords: ['profile', 'agent', 'custom'],
          icon: <Bot size={11} strokeWidth={2} aria-hidden />
        })
      }
      if (canUseThreadGoals) {
        actions.push({
          id: 'goal',
          label: t('goal.system.label'),
          description: t('goal.system.description'),
          keywords: ['goal', '目标'],
          icon: <Target size={11} strokeWidth={2} aria-hidden />
        })
      }
      return actions
    },
    [canUseAgentProfiles, canUseThreadGoals, selectedProfileId, t, welcomeMode]
  )
  const modelApiAvailable =
    isConnected &&
    capabilities?.modelCatalogManagement === true &&
    capabilities?.workspaceConfigManagement === true
  const modelLoading = modelApiAvailable && modelCatalogStatus === 'loading'
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
      .filter((app) => app.installed && app.enabled)
      .sort((a, b) => a.displayName.localeCompare(b.displayName)),
    [appBindingApps]
  )

  useEffect(() => {
    setWelcomeAppIds((current) => {
      const appsById = new Map(welcomeApps.map((app) => [app.appId, app]))
      const selected = new Set(
        current.filter((appId) => {
          const app = appsById.get(appId)
          return app?.connectionState === 'connected'
            && !welcomeAppUserDisabledIds.has(appId)
        })
      )
      for (const app of welcomeApps) {
        if (app.connectionState === 'connected'
          && app.requiresExternalConnection !== false
          && app.managed !== true
          && !welcomeAppUserDisabledIds.has(app.appId)) {
          selected.add(app.appId)
        }
      }
      const next = welcomeApps
        .filter((app) => selected.has(app.appId))
        .map((app) => app.appId)
      return sameStringArray(current, next) ? current : next
    })
  }, [welcomeAppUserDisabledIds, welcomeApps])

  const toggleWelcomeApp = useCallback((appId: string, selected: boolean): void => {
    setWelcomeAppUserDisabledIds((current) => {
      const next = new Set(current)
      if (selected) next.delete(appId)
      else next.add(appId)
      return next
    })
    setWelcomeAppIds((current) => {
      if (selected) return current.includes(appId) ? current : [...current, appId]
      return current.filter((candidate) => candidate !== appId)
    })
  }, [])

  const handleWelcomeAppRefresh = useCallback(async (): Promise<void> => {
    await fetchAppBindings(null, true, 'welcome')
  }, [fetchAppBindings])

  const handleWelcomeNativeInstall = useCallback(async (app: AppInfo): Promise<void> => {
    const url = app.nativeApp?.installUrl || app.releasePage || app.downloadUrl
    if (!url) throw new Error(t('appBinding.nativeInstallMissing'))
    await window.api.shell.openExternal(url)
  }, [t])

  const handleWelcomeAppConnect = useCallback(async (app: AppInfo): Promise<void> => {
    if (welcomeAppBusyId != null) return
    setWelcomeAppBusyId(app.appId)
    try {
      const result = await startAppConnection(app.appId)
      await openWelcomeAppHandoff(result.handoff, t)
      addToast(t('appBinding.connectStarted'), 'info')
      await waitForAppConnection(app.appId)
      toggleWelcomeApp(app.appId, true)
      addToast(t('appBinding.connection.connected'), 'success')
    } catch (err) {
      addToast(err instanceof Error ? err.message : String(err), 'error')
    } finally {
      setWelcomeAppBusyId(null)
    }
  }, [startAppConnection, t, toggleWelcomeApp, waitForAppConnection, welcomeAppBusyId])

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
        requestedScopes: defaultWelcomeRequestedScopes(selectedApp),
        requestedTools: requestedWelcomeTools(selectedApp),
        source: 'welcome'
      })
      if (result.handoff?.uri) await openWelcomeAppHandoff(result.handoff, t)
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

  const getCaseInsensitiveValue = useCallback((record: Record<string, unknown>, key: string): unknown => {
    const expected = key.toLowerCase()
    for (const [candidate, value] of Object.entries(record)) {
      if (candidate.toLowerCase() === expected) return value
    }
    return undefined
  }, [])

  const resolveModelFromConfig = useCallback((cfg: Record<string, unknown>): string => {
    const modelRaw = cfg.Model ?? cfg.model
    if (typeof modelRaw !== 'string') return 'Default'
    const trimmed = modelRaw.trim()
    if (trimmed.length === 0 || trimmed === 'Default') return 'Default'
    return trimmed
  }, [])

  const resolveReasoningFromConfig = useCallback((cfg: Record<string, unknown>): ResolvedReasoningConfig => {
    return readReasoningObject(cfg.Reasoning ?? cfg.reasoning) ?? DEFAULT_REASONING_CONFIG
  }, [])

  const resolveWelcomeSuggestionsEnabled = useCallback((cfg: Record<string, unknown>): boolean => {
    const section = getCaseInsensitiveValue(cfg, 'WelcomeSuggestions')
    if (section == null || typeof section !== 'object' || Array.isArray(section)) {
      return true
    }
    const enabled = getCaseInsensitiveValue(section as Record<string, unknown>, 'Enabled')
    return typeof enabled === 'boolean' ? enabled : true
  }, [getCaseInsensitiveValue])

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

  const clearSlashSystemInput = useCallback((): void => {
    const text = richRef.current?.getText() ?? ''
    if (text.trim().startsWith('/')) {
      richRef.current?.clear()
    }
  }, [])

  const toggleWelcomeMode = useCallback((): void => {
    setWelcomeMode((m) => (m === 'agent' ? 'plan' : 'agent'))
  }, [])

  const onSelectSystemAction = useCallback((actionId: string): void => {
    setSlashDismissed(true)
    clearSlashSystemInput()
    if (actionId === 'planMode') {
      toggleWelcomeMode()
      return
    }
    if (actionId === 'profile') {
      setProfilePickerOpen(true)
      return
    }
    if (actionId !== 'goal') return
    setGoalPopoverOpen(true)
  }, [clearSlashSystemInput, toggleWelcomeMode])

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
    setWelcomeApprovalPolicy(welcomeDraft.approvalPolicy ?? 'default')
    setModelName(
      workspaceLlmConfigChangedRef.current && workspaceModelFromConfigRef.current != null
        ? workspaceModelFromConfigRef.current
        : (welcomeDraft.model || 'Default')
    )
    setReasoningConfig(readReasoningObject(welcomeDraft.reasoning) ?? DEFAULT_REASONING_CONFIG)
    setContentRevision((n) => n + 1)
    draftHydratedRef.current = true
  }, [canUseCommandPicker, customCommandStatus, skillCatalogReady])

  useEffect(() => {
    let disposed = false
    const loadWorkspaceDefaults = async (): Promise<void> => {
      const workspaceModelChanged =
        workspaceConfigChangeSeq > 0 &&
        workspaceConfigChange?.regions.some((region) =>
          region === 'workspace.provider' || region === 'workspace.model'
        ) === true
      const workspaceReasoningChanged =
        workspaceConfigChangeSeq > 0 &&
        workspaceConfigChange?.regions.includes('workspace.reasoning') === true
      const hasInitialDraft = initialWelcomeDraftRef.current != null
      if (hasInitialDraft && !workspaceModelChanged && !workspaceReasoningChanged) return
      if (!workspaceConfigPath) {
        if (!hasInitialDraft || workspaceModelChanged) {
          workspaceLlmConfigChangedRef.current = true
          workspaceModelFromConfigRef.current = 'Default'
          setModelName('Default')
        }
        if (!hasInitialDraft || workspaceReasoningChanged) {
          setReasoningConfig(DEFAULT_REASONING_CONFIG)
        }
        return
      }

      try {
        const cfg = await readWorkspaceConfig()
        if (disposed) return
        if (!hasInitialDraft || workspaceModelChanged) {
          const nextModel = resolveModelFromConfig(cfg)
          workspaceLlmConfigChangedRef.current = true
          workspaceModelFromConfigRef.current = nextModel
          setModelName(nextModel)
        }
        if (!hasInitialDraft || workspaceReasoningChanged) {
          setReasoningConfig(resolveReasoningFromConfig(cfg))
        }
      } catch {
        if (!disposed) {
          if (!hasInitialDraft || workspaceModelChanged) {
            workspaceLlmConfigChangedRef.current = true
            workspaceModelFromConfigRef.current = 'Default'
            setModelName('Default')
          }
          if (!hasInitialDraft || workspaceReasoningChanged) {
            setReasoningConfig(DEFAULT_REASONING_CONFIG)
          }
        }
      }
    }

    void loadWorkspaceDefaults()
    return () => {
      disposed = true
    }
  }, [
    readWorkspaceConfig,
    resolveModelFromConfig,
    resolveReasoningFromConfig,
    workspaceConfigChange,
    workspaceConfigChangeSeq,
    workspaceConfigPath
  ])

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
      || welcomeApprovalPolicy !== 'default'
      || hasCustomReasoning
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
      model,
      reasoning: reasoningConfig,
      approvalPolicy: welcomeApprovalPolicy
    }, draftProjectKey)
  }, [clearWelcomeDraft, draftProjectKey, files, images, modelName, reasoningConfig, setWelcomeDraft, welcomeApprovalPolicy, welcomeMode])

  useEffect(() => {
    if (!draftHydratedRef.current) return
    const timer = setTimeout(() => {
      flushWelcomeDraft()
    }, WELCOME_DRAFT_DEBOUNCE_MS)
    return () => {
      clearTimeout(timer)
    }
  }, [contentRevision, files, flushWelcomeDraft, images, modelName, reasoningConfig, welcomeApprovalPolicy, welcomeMode])

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

  const handleModelChange = useCallback(
    async (nextModel: string): Promise<void> => {
      if (!workspaceConfigPath || !nextModel || nextModel === modelName) return
      setModelApplying(true)
      const previousModel = modelName
      setModelName(nextModel)
      try {
        await window.api.appServer.sendRequest('workspace/config/update', {
          model: nextModel === 'Default' ? null : nextModel
        })
      } catch (err) {
        const msg = err instanceof Error ? err.message : String(err)
        setModelName(previousModel)
        addToast(`Failed to save model: ${msg}`, 'error')
      } finally {
        setModelApplying(false)
      }
    },
    [modelName, workspaceConfigPath]
  )

  const handleReasoningChange = useCallback(
    async (nextReasoning: ReasoningQuickValue): Promise<void> => {
      if (!workspaceConfigPath) return
      const nextPayload = buildReasoningPayload(nextReasoning, reasoningConfig)
      setModelApplying(true)
      const previousReasoning = reasoningConfig
      setReasoningConfig(nextPayload ?? DEFAULT_REASONING_CONFIG)
      try {
        await window.api.appServer.sendRequest('workspace/config/update', {
          reasoning: nextPayload
        })
      } catch (err) {
        const msg = err instanceof Error ? err.message : String(err)
        setReasoningConfig(previousReasoning)
        addToast(`Failed to save thinking: ${msg}`, 'error')
      } finally {
        setModelApplying(false)
      }
    },
    [reasoningConfig, workspaceConfigPath]
  )

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

    if (welcomeWorkspaceMode === 'worktree') {
      const res = await window.api.appServer.sendRequest('worktree/createAndStart', {
        identity,
        historyMode: 'server',
        baseRef: welcomeBaseRef || undefined,
        branchName: welcomeWorktreeBranchName || undefined
      }, 180_000) as { thread: ThreadSummary }
      return res.thread
    }

    const res = await window.api.appServer.sendRequest('thread/start', {
      identity,
      historyMode: 'server'
    }) as { thread: ThreadSummary }
    return res.thread
  }, [identityPath, welcomeBaseRef, welcomeWorkspaceMode, welcomeWorktreeBranchName])

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
    setGoalBusy(true)
    setStarting(true)
    setMascotBounce((n) => n + 1)
    // A profile-backed thread runs its agent's fixed posture (no Plan/Agent mode).
    const capturedMode = selectedProfileId ? 'agent' : welcomeMode
    const capturedApprovalPolicy = welcomeApprovalPolicy
    const capturedModel = modelName === 'Default' ? '' : modelName
    const capturedReasoning = reasoningConfig
    const capturedProfileId = selectedProfileId
    let createdThreadId: string | null = null
    try {
      const thread = await startWelcomeThread()
      createdThreadId = thread.id
      await applyWelcomeProfile(thread.id, capturedProfileId)

      const goalResult = await window.api.appServer.sendRequest('thread/goal/set', {
        threadId: thread.id,
        objective: trimmedObjective,
        mode: 'upsertOrUpdate'
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
        reasoning: capturedReasoning
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
      setGoalBusy(false)
      setStarting(false)
    }
  }, [
    addThread,
    applyWelcomeProfile,
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
      setGoalPopoverOpen(true)
      return true
    }
    if (command.kind === 'set') return createGoalBackedThread(command.objective)
    addToast(t('goal.toast.noCurrent'), 'warning')
    return false
  }, [canUseThreadGoals, createGoalBackedThread, showGoalUnavailable, t])

  const sendFromWelcome = useCallback(async (): Promise<void> => {
    const text = richRef.current?.getText() ?? ''
    const segments = richRef.current?.getSegments() ?? []
    const trimmed = text.trim()
    if (
      (!trimmed && images.length === 0 && files.length === 0) ||
      sendInFlightRef.current ||
      connectionStatus !== 'connected' ||
      modelLoading
    ) {
      return
    }
    if (remoteWorkspace && (images.length > 0 || files.length > 0)) {
      addToast(t('input.remoteLocalFilesUnavailable'), 'warning')
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
    const capturedImages = [...images]
    const capturedFiles = [...files]
    // A profile-backed thread runs its agent's fixed posture (no Plan/Agent mode).
    const capturedMode = selectedProfileId ? 'agent' : welcomeMode
    const capturedApprovalPolicy = welcomeApprovalPolicy
    const capturedModel = modelName === 'Default' ? '' : modelName
    const capturedReasoning = reasoningConfig
    const capturedProfileId = selectedProfileId
    let createdThreadId: string | null = null
    try {
      const thread = await startWelcomeThread()
      createdThreadId = thread.id
      await applyWelcomeProfile(thread.id, capturedProfileId)
      await startWelcomeAppBindings(thread.id)

      skipDraftPersistRef.current = true
      latestDraftTextRef.current = ''
      latestDraftSegmentsRef.current = []
      latestDraftSelectionRef.current = null
      clearWelcomeDraft(draftProjectKey)
      const { inputParts } = buildComposerInputParts({
        text: trimmed,
        segments,
        files: capturedFiles,
        images: capturedImages
      })
      useUIStore.getState().setPendingWelcomeTurn({
        threadId: thread.id,
        text: trimmed,
        inputParts,
        images: capturedImages.length > 0 ? capturedImages : undefined,
        files: capturedFiles.length > 0 ? capturedFiles : undefined,
        mode: capturedMode,
        approvalPolicy: capturedApprovalPolicy,
        model: capturedModel,
        reasoning: capturedReasoning
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
    remoteWorkspace,
    t
  ])

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

  const addPickedFiles = useCallback((picked: Array<{ path: string; fileName: string }>): void => {
    if (picked.length === 0) return
    setFiles((prev) => mergeComposerFileAttachments(prev, picked))
  }, [])

  const pickFiles = useCallback(async (): Promise<void> => {
    if (remoteWorkspace) {
      addToast(t('input.remoteLocalFilesUnavailable'), 'warning')
      return
    }
    try {
      const picked = await window.api.workspace.pickFiles()
      addPickedFiles(picked)
    } catch (err) {
      const msg = err instanceof Error ? err.message : String(err)
      addToast(t('input.pickFilesFailed', { error: msg }), 'error')
    }
  }, [addPickedFiles, remoteWorkspace, t])

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
            busyAppId={welcomeAppBusyId}
            disabled={starting}
            onRefresh={handleWelcomeAppRefresh}
            onToggleApp={toggleWelcomeApp}
            onConnect={handleWelcomeAppConnect}
            onInstallNative={handleWelcomeNativeInstall}
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
                    <GoalControlPopover
                      visible={goalPopoverOpen}
                      goal={null}
                      busy={goalBusy}
                      onSetObjective={createGoalBackedThread}
                      onPause={async () => {
                        addToast(t('goal.toast.noCurrent'), 'warning')
                        return false
                      }}
                      onResume={async () => {
                        addToast(t('goal.toast.noCurrent'), 'warning')
                        return false
                      }}
                      onClear={async () => {
                        addToast(t('goal.toast.noCurrent'), 'warning')
                        return false
                      }}
                      onDismiss={() => {
                        setGoalPopoverOpen(false)
                      }}
                    />
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
                      query={slashQuery ?? ''}
                      visible={showSlashPopover}
                      loading={customCommandStatus === 'loading' || skillsLoading}
                      systemActions={systemActions}
                      commands={customCommands}
                      skills={availableSkills}
                      onSelectSystemAction={onSelectSystemAction}
                      onSelectCommand={onSelectCommand}
                      onSelectSkill={onSelectSkill}
                      onDismiss={() => {
                        setSlashDismissed(true)
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
                      suppressSubmit={showMentionPopover || showSlashPopover || showSkillPopover || modelLoading}
                      onToggleModeShortcut={toggleWelcomeMode}
                      placeholder={
                        isConnected
                          ? t('welcomeComposer.placeholder.ask')
                          : t('composer.placeholder.connecting')
                      }
                      onSubmit={() => {
                        void sendFromWelcome()
                      }}
                      onAtQuery={remoteWorkspace ? undefined : handleAtQuery}
                      onSlashQuery={handleSlashQuery}
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
                <div style={{ display: 'flex', alignItems: 'center', gap: '10px', minWidth: 0, flexWrap: 'wrap' }}>
                  <ComposerAttachmentMenu
                    title={t('composer.attachFileTitle')}
                    ariaLabel={t('composer.attachFileAria')}
                    attachImageLabel={t('composer.attachImage')}
                    referenceFileLabel={t('composer.referenceFile')}
                    onAttachImages={attachImages}
                    onReferenceFiles={() => {
                      void pickFiles()
                    }}
                    planModeLabel={selectedProfileId ? undefined : t('composer.system.plan')}
                    planModeToggleLabel={t('composer.system.plan.toggle')}
                    planModeEnabled={welcomeMode === 'plan'}
                    onTogglePlanMode={selectedProfileId ? undefined : () => {
                      toggleWelcomeMode()
                    }}
                    attachmentDisabledReason={remoteLocalFilesUnavailable}
                  />

                  <ApprovalPolicyPicker
                    value={welcomeApprovalPolicy}
                    onChange={setWelcomeApprovalPolicy}
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
                </div>
              }
              footerAction={
                <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                  <ChatGptUsageBadge provider={activeChatGptProvider} />
                  <ModelPicker
                    modelName={modelName}
                    modelOptions={modelApiAvailable ? modelOptions : []}
                    modelCatalog={modelCatalog}
                    reasoningValue={reasoningConfig.enabled ? reasoningConfig.effort : 'off'}
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
                    onReasoningChange={(nextReasoning) => {
                      void handleReasoningChange(nextReasoning)
                    }}
                    onRetry={() => {
                      void loadModels(true)
                    }}
                    shortcut={ACTION_SHORTCUTS.selectModel}
                    triggerStyle={composerModelPillStyle(
                      modelApplying || starting || modelLoading
                        ? 'var(--composer-footer-muted)'
                        : 'var(--composer-footer-highlight)',
                      modelApplying || starting || modelLoading
                    )}
                  />
                  <ActionTooltip
                    label={t('welcome.sendAria')}
                    shortcut={canSend ? ACTION_SHORTCUTS.send : undefined}
                    placement="top"
                  >
                    <ComposerSendButton
                      tone={canSend ? 'enabled' : 'disabled'}
                      onClick={() => { void sendFromWelcome() }}
                      disabled={!canSend}
                      aria-label={t('welcome.sendAria')}
                    >
                      <SendIcon />
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
  busyAppId,
  disabled,
  onRefresh,
  onToggleApp,
  onConnect,
  onInstallNative
}: {
  apps: AppInfo[]
  selectedAppIds: string[]
  busyAppId: string | null
  disabled: boolean
  onRefresh: () => Promise<void>
  onToggleApp: (appId: string, selected: boolean) => void
  onConnect: (app: AppInfo) => Promise<void>
  onInstallNative: (app: AppInfo) => Promise<void>
}): JSX.Element {
  const t = useT()
  const [open, setOpen] = useState(false)
  const rootRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!open) return
    const handlePointerDown = (event: PointerEvent): void => {
      if (rootRef.current?.contains(event.target as Node)) return
      setOpen(false)
    }
    const handleKeyDown = (event: KeyboardEvent): void => {
      if (event.key === 'Escape') setOpen(false)
    }
    window.addEventListener('pointerdown', handlePointerDown)
    window.addEventListener('keydown', handleKeyDown)
    return () => {
      window.removeEventListener('pointerdown', handlePointerDown)
      window.removeEventListener('keydown', handleKeyDown)
    }
  }, [open])

  async function runAction(action: () => Promise<void>): Promise<void> {
    if (busyAppId != null) return
    try {
      await action()
    } catch (err) {
      addToast(err instanceof Error ? err.message : String(err), 'error')
    }
  }

  return (
    <div ref={rootRef} style={welcomeAppButtonRoot}>
      <ActionTooltip label={t('appBinding.title')} placement="bottom">
        <button
          type="button"
          aria-label={t('appBinding.title')}
          style={welcomeAppHeaderButton}
          disabled={disabled}
          onClick={() => {
            setOpen((value) => {
              const next = !value
              if (next) void onRefresh()
              return next
            })
          }}
        >
          <Link2 size={15} aria-hidden />
          {selectedAppIds.length > 0 && <span style={welcomeAppCountBadge}>{selectedAppIds.length}</span>}
        </button>
      </ActionTooltip>
      {open && (
        <div style={welcomeAppPopover} role="dialog" aria-label={t('appBinding.title')}>
          <div style={welcomeAppPopoverHeader}>
            <strong style={welcomeAppPopoverTitle}>{t('appBinding.title')}</strong>
            <button
              type="button"
              style={welcomeAppIconButton}
              aria-label={t('appBinding.refresh')}
              disabled={busyAppId != null}
              onClick={() => { void runAction(onRefresh) }}
            >
              <RefreshCw size={13} aria-hidden />
            </button>
          </div>
          {apps.length === 0 ? (
            <div style={welcomeAppMuted}>{t('appBinding.welcomeEmpty')}</div>
          ) : (
            <div style={welcomeAppList}>
              {apps.map((app) => {
                const connected = app.connectionState === 'connected'
                const nativeMissing = app.nativeApp?.status === 'missing'
                const checked = selectedAppIds.includes(app.appId)
                const rowBusy = busyAppId === app.appId
                return (
                  <div key={app.appId} style={welcomeAppRow}>
                    <AppLogo app={app} />
                    <div style={welcomeAppMain}>
                      <div style={welcomeAppTitleRow}>
                        <strong style={welcomeAppTitle}>{app.displayName}</strong>
                        <span style={welcomeStatePill(connected)}>
                          {welcomeConnectionStateLabel(app.connectionState, t)}
                        </span>
                      </div>
                      {!connected && (
                        <span style={welcomeAppNativeHint}>
                          {nativeMissing ? t('appBinding.native.missing') : t('appBinding.handoffOpening')}
                        </span>
                      )}
                    </div>
                    <div style={welcomeAppActions}>
                      {connected ? (
                        <PillSwitch
                          checked={checked}
                          onChange={(nextChecked) => onToggleApp(app.appId, nextChecked)}
                          size="sm"
                          disabled={disabled || busyAppId != null}
                          aria-label={t('appBinding.welcomeUseApp', { name: app.displayName })}
                        />
                      ) : nativeMissing ? (
                        <button
                          type="button"
                          style={welcomeAppSecondaryButton}
                          disabled={disabled || busyAppId != null}
                          onClick={() => { void runAction(() => onInstallNative(app)) }}
                        >
                          <ExternalLink size={13} aria-hidden />
                          {t('appBinding.installNative')}
                        </button>
                      ) : !connected ? (
                        <button
                          type="button"
                          style={welcomeAppPrimaryButton}
                          disabled={disabled || busyAppId != null}
                          onClick={() => { void onConnect(app) }}
                        >
                          <Link2 size={13} aria-hidden />
                          {rowBusy ? t('appBinding.connection.connecting') : t('appBinding.connect')}
                        </button>
                      ) : null}
                    </div>
                  </div>
                )
              })}
            </div>
          )}
        </div>
      )}
    </div>
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

function welcomeConnectionStateLabel(state: string, t: ReturnType<typeof useT>): string {
  if (state === 'connected') return t('appBinding.connection.connected')
  if (state === 'connecting') return t('appBinding.connection.connecting')
  if (state === 'needsAuth') return t('appBinding.connection.needsAuth')
  if (state === 'error') return t('appBinding.connection.error')
  return t('appBinding.connection.notConnected')
}

const welcomeAppButtonSlot: CSSProperties = { position: 'absolute', top: 12, right: 16, zIndex: 8 }
const welcomeAppButtonRoot: CSSProperties = { position: 'relative' }
const welcomeAppHeaderButton: CSSProperties = {
  height: 30,
  minWidth: 30,
  padding: '0 9px',
  border: '1px solid var(--border-default)',
  borderRadius: 7,
  background: 'var(--bg-secondary)',
  color: 'var(--text-secondary)',
  cursor: 'pointer',
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  gap: 5
}
const welcomeAppCountBadge: CSSProperties = { minWidth: 15, height: 15, borderRadius: 999, background: 'var(--accent)', color: 'var(--on-accent)', fontSize: 10, display: 'inline-flex', alignItems: 'center', justifyContent: 'center', marginLeft: -4 }
const welcomeAppPopover: CSSProperties = { position: 'absolute', top: 36, right: 0, zIndex: 40, width: 360, maxWidth: 'calc(100vw - 32px)', border: '1px solid var(--border-default)', borderRadius: 8, background: 'var(--bg-secondary)', boxShadow: 'var(--shadow-level-3)', padding: 10 }
const welcomeAppPopoverHeader: CSSProperties = { display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 8, marginBottom: 8 }
const welcomeAppPopoverTitle: CSSProperties = { fontSize: 13, color: 'var(--text-primary)' }
const welcomeAppIconButton: CSSProperties = { width: 28, height: 28, border: 'none', borderRadius: 7, background: 'var(--bg-tertiary)', color: 'var(--text-secondary)', cursor: 'pointer', display: 'inline-flex', alignItems: 'center', justifyContent: 'center', padding: 0 }
const welcomeAppMuted: CSSProperties = { color: 'var(--text-secondary)', fontSize: 12, padding: 8 }
const welcomeAppList: CSSProperties = { display: 'flex', flexDirection: 'column', gap: 8 }
const welcomeAppRow: CSSProperties = { display: 'grid', gridTemplateColumns: '30px minmax(0, 1fr) auto', alignItems: 'center', gap: 9, border: '1px solid var(--border-default)', borderRadius: 8, padding: 9 }
const welcomeAppMain: CSSProperties = { minWidth: 0 }
const welcomeAppTitleRow: CSSProperties = { display: 'flex', alignItems: 'center', gap: 7, minWidth: 0, flexWrap: 'wrap' }
const welcomeAppTitle: CSSProperties = { fontSize: 12, color: 'var(--text-primary)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }
const welcomeAppLogoImg: CSSProperties = { width: 30, height: 30, borderRadius: 7, objectFit: 'cover', background: 'var(--bg-tertiary)', border: '1px solid var(--border-default)' }
const welcomeAppLogoFallback: CSSProperties = { width: 30, height: 30, borderRadius: 7, display: 'inline-flex', alignItems: 'center', justifyContent: 'center', background: 'var(--bg-tertiary)', border: '1px solid var(--border-default)', color: 'var(--text-secondary)' }
const welcomeAppNativeHint: CSSProperties = { display: 'block', marginTop: 5, fontSize: 11, color: 'var(--text-tertiary)' }
const welcomeAppActions: CSSProperties = { display: 'flex', justifyContent: 'flex-end', alignItems: 'center' }
const welcomeAppBaseButton: CSSProperties = { border: 'none', borderRadius: 8, padding: '7px 10px', cursor: 'pointer', display: 'inline-flex', alignItems: 'center', gap: 6, fontSize: 12, fontWeight: 600, whiteSpace: 'nowrap' }
const welcomeAppPrimaryButton: CSSProperties = { ...welcomeAppBaseButton, background: '#050505', color: '#fff' }
const welcomeAppSecondaryButton: CSSProperties = { ...welcomeAppBaseButton, background: 'var(--bg-tertiary)', color: 'var(--text-primary)' }

function welcomeStatePill(good: boolean): CSSProperties {
  return {
    borderRadius: 999,
    padding: '2px 6px',
    fontSize: 10,
    background: good ? 'rgba(22, 163, 74, 0.12)' : 'var(--bg-tertiary)',
    color: good ? 'var(--success, #15803d)' : 'var(--text-secondary)'
  }
}

function parseWelcomeSystemSlashCommand(text: string): { kind: 'agent' | 'plan' } | null {
  const trimmed = text.trim().toLowerCase()
  if (trimmed === '/plan') return { kind: 'plan' }
  if (trimmed === '/agent') return { kind: 'agent' }
  return null
}

function defaultWelcomeRequestedScopes(app: AppInfo): string[] {
  return app.scopes.map((scope) => scope.id)
}

function requestedWelcomeTools(app: AppInfo): string[] | undefined {
  return app.dynamicToolCatalog?.enabled === true
    ? undefined
    : app.toolCatalog.map((tool) => tool.name)
}

function sameStringArray(left: string[], right: string[]): boolean {
  if (left.length !== right.length) return false
  return left.every((value, index) => value === right[index])
}

async function openWelcomeAppHandoff(
  handoff: AppHandoff,
  t: ReturnType<typeof useT>
): Promise<void> {
  if (!handoff.uri) {
    addToast(t('appBinding.handoffReady'), 'info')
    return
  }

  try {
    await (window.api.shell.openAppHandoff ?? window.api.shell.openExternal)(handoff.uri)
  } catch {
    addToast(t('appBinding.handoffReady'), 'info')
  }
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
