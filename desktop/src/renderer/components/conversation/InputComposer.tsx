import { useRef, useState, useCallback, useEffect, useMemo, type CSSProperties } from 'react'
import type { DesktopPluginComposerSurfaceContext } from '@dotcraft/plugin'
import { Archive, Bot, ChevronsDown, FileText, ListChecks, Target } from 'lucide-react'
import { readAppServerErrorFields } from '../../../shared/appServerError'
import { useLocale, useT } from '../../contexts/LocaleContext'
import { useConversationStore } from '../../stores/conversationStore'
import { addToast } from '../../stores/toastStore'
import { useUIStore } from '../../stores/uiStore'
import { useConnectionStore } from '../../stores/connectionStore'
import { useCustomCommandCatalog } from '../../hooks/useCustomCommandCatalog'
import { useSkillsStore } from '../../stores/skillsStore'
import { useThreadStore } from '../../stores/threadStore'
import {
  useComposerDraftStore,
  threadComposerDraftHasContent,
  type ThreadComposerDraftInput
} from '../../stores/composerDraftStore'
import type { ContextUsageSnapshotWire, ContextWindowMode, ThreadGoal } from '../../types/thread'
import type { ComposerDraftSegment } from '../../types/composerDraft'
import { wireTurnToConversationTurn } from '../../types/conversation'
import type {
  ComposerFileAttachment,
  ConversationItem,
  ConversationTurn,
  ImageAttachment,
  InputPart,
  QueuedTurnInput
} from '../../types/conversation'
import { startTurnWithOptimisticUI } from '../../utils/startTurn'
import { expandInitCommand } from '../../utils/initCommand'
import { useComposerMascot } from './useComposerMascot'
import { buildComposerInputParts } from '../../utils/composeInputParts'
import { readThreadHistoryHead } from '../../utils/threadHistory'
import { interruptTurn } from '../../utils/interruptTurn'
import { buildGoalObjective, extractGoal, parseGoalSlashCommand, type GoalSlashCommand } from '../../utils/threadGoal'
import {
  classifyDroppedComposerFiles,
  extForFile,
  isImageFile,
  mergeComposerFileAttachments
} from '../../utils/composerAttachments'
import { PendingMessageIndicator } from './PendingMessageIndicator'
import { RichInputArea, type RichInputAreaHandle } from './RichInputArea'
import { AttachmentStrip } from './AttachmentStrip'
import { FileSearchPopover } from './FileSearchPopover'
import { CommandSearchPopover, type SlashSystemActionInfo } from './CommandSearchPopover'
import { GoalControlPopover } from './GoalControlPopover'
import { GoalComposePill } from './GoalComposePill'
import { ModelPicker, type ReasoningQuickValue } from './ModelPicker'
import type { InferenceSpeedWire, ModelCatalogItem } from '../../stores/modelCatalogStore'
import { useModelCatalogStore } from '../../stores/modelCatalogStore'
import { useProvidersStore, useChatGptOAuthSummary } from '../../stores/providersStore'
import { ChatGptUsageBadge } from './ChatGptUsageBadge'
import { ComposerCommandTrigger } from './ComposerCommandTrigger'
import { ContextUsageRing } from './ContextUsageRing'
import { ApprovalPolicyPicker } from './ApprovalPolicyPicker'
import { QueuedInputDock } from './QueuedInputDock'
import {
  COMPOSER_FOOTER_CONTROL_HEIGHT,
  ComposerCustomProfileLabel,
  ComposerPlanModeLabel,
  ComposerShell,
  composerFooterControlHoverBackground,
  composerModelPillStyle
} from './ComposerShell'
import { ComposerSubmitButton } from './ComposerSubmitButton'
import { ProfilePickerPopover } from './ProfilePickerPopover'
import { ComposerWorkspaceFooter } from './ComposerWorkspaceFooter'
import { type AvatarSpec } from '../agents/agentAvatar'
import { useResolvedProfileAvatar } from '../../stores/agentProfileAvatarStore'
import { ActionTooltip } from '../ui/ActionTooltip'
import { ACTION_SHORTCUTS } from '../ui/shortcutKeys'
import { useConfirmDialog } from '../ui/ConfirmDialog'
import { ConversationColumn } from './ConversationColumn'
import { stringifyComposerDraftSegments } from './richInputSerialization'
import { resolveComposerMascotEffectState } from './composerMascotEffectState'
import { VoiceInputControl, VoiceInputStatus } from './VoiceInputControl'
import { registerComposerVoiceTarget } from '../../voice/composerDraftBridge'
import { isVoiceProcessingForThread, shouldUseCompactVoiceFooter, useVoiceStore } from '../../voice/voiceStore'
import {
  executeDesktopPluginCommand,
  isDesktopPluginContributionAvailable,
  useDesktopPluginRegistry
} from '../../plugins/desktopPluginRegistry'
import { DesktopPluginSurface } from '../desktopPlugins/DesktopPluginSurface'
import {
  ComposerStatusContent,
  ComposerToolbarLeadingSlots,
  ComposerToolbarTrailingSlots
} from './ComposerSurfaceSlots'

const MAX_TEXT_LENGTH = 100_000
const MAX_IMAGES = 5
const MAX_IMAGE_BYTES = 10 * 1024 * 1024
const MANUAL_COMPACTION_TIMEOUT_MS = 5 * 60 * 1000
const MANUAL_MEMORY_CONSOLIDATION_TIMEOUT_MS = 5 * 60 * 1000

interface ComposerHistoryEntry {
  text: string
  segments: ComposerDraftSegment[]
}

interface ComposerDraftSnapshot extends ComposerHistoryEntry {
  files: ComposerFileAttachment[]
  images: ImageAttachment[]
}

function emptyComposerDraftSnapshot(): ComposerDraftSnapshot {
  return { text: '', segments: [], files: [], images: [] }
}

/** AppServer maps a running turn and active maintenance alike onto this code. */
const TURN_IN_PROGRESS_RPC_CODE = -32012

function isTurnBusyError(err: unknown): boolean {
  const { code, rpcCode } = readAppServerErrorFields(err)
  return code === 'turnInProgress' || rpcCode === TURN_IN_PROGRESS_RPC_CODE
}

function isRequestTimeoutError(err: unknown): boolean {
  const message = err instanceof Error ? err.message : String(err)
  const normalized = message.toLowerCase()
  return normalized.includes('timed out') || normalized.includes('timeout')
}

export interface InputComposerSubmitPayload {
  text: string
  segments: ComposerDraftSegment[]
  files: ComposerFileAttachment[]
  images: ImageAttachment[]
  inputParts: InputPart[]
  visibleText: string
  bodyText: string
}

interface InputComposerProps {
  threadId: string
  /** Thread state/identity workspace path. Worktree threads still belong to this root. */
  workspacePath: string
  /** File browsing and local attachment root. Worktree threads use effectiveWorkspacePath here. */
  fileWorkspacePath?: string
  modelName?: string
  providerId?: string
  providerOptions?: Array<{ id: string; displayName: string }>
  modelOptions?: string[]
  modelCatalog?: ModelCatalogItem[]
  reasoningValue?: ReasoningQuickValue
  speedValue?: InferenceSpeedWire
  modelLoading?: boolean
  modelDisabled?: boolean
  remoteWorkspace?: boolean
  /** When true, model/list reported that the upstream API does not support listing; show a read-only label. */
  modelListUnsupportedEndpoint?: boolean
  modelCatalogError?: boolean
  modelCatalogErrorMessage?: string | null
  onModelChange?: (model: string) => void
  onProviderChange?: (providerId: string) => void
  onReasoningChange?: (value: ReasoningQuickValue) => void
  onSpeedChange?: (value: InferenceSpeedWire) => void
  onModelCatalogRetry?: () => void
  contextMode?: ContextWindowMode
  contextSupportsMax?: boolean
  contextDegraded?: boolean
  contextConfiguredWindow?: number
  onContextModeChange?: (mode: ContextWindowMode) => void
  /**
   * Hides the workspace/worktree footer, the approval-policy picker and the ChatGPT badge
   * for embedded composers; the core input (attach, plan, reasoning, model, send) is kept.
   */
  minimalChrome?: boolean
  /** Overrides the thread-profile-derived avatar — the Agent Builder thread has no profile id. */
  mascotAvatar?: AvatarSpec
  variant?: 'default' | 'agentBuilder'
  placeholder?: string
  /** One-shot text injection request from an external empty state or suggestion. */
  prefillRequest?: { id: number; text: string } | null
  onBeforeSend?: () => Promise<void> | void
  /**
   * InputComposer still owns rich-input serialization, but the caller owns the destination
   * thread/RPC. Used by the Agent Builder intro before a hidden builder thread exists.
   */
  submitOverride?: (payload: InputComposerSubmitPayload) => Promise<void> | void
  /** Discards voice work when this pre-thread Composer unmounts. */
  transientVoiceOrigin?: boolean
  dockPadding?: CSSProperties['padding']
}

export function InputComposer(props: InputComposerProps): JSX.Element {
  const threadMode = useConversationStore((state) => state.threadMode)
  const turnStatus = useConversationStore((state) => state.turnStatus)
  const maintenanceKind = useConversationStore((state) => state.maintenanceKind)
  const hasSubmitOverride = props.submitOverride !== undefined
  const waitingForInput = !hasSubmitOverride && turnStatus === 'waitingInput'
  const context = {
    workspacePath: props.workspacePath || null,
    threadId: hasSubmitOverride ? null : props.threadId,
    mode: hasSubmitOverride ? 'agent' : threadMode,
    busy: (!hasSubmitOverride && (
      turnStatus === 'running'
      || waitingForInput
      || maintenanceKind === 'compacting'
      || maintenanceKind === 'consolidating'
    )),
    awaitingApproval: !hasSubmitOverride && turnStatus === 'waitingApproval',
    variant: props.variant ?? 'default',
    minimalChrome: props.minimalChrome ?? false
  } as const

  return (
    <DesktopPluginSurface name="composer" context={context}>
      <InputComposerCore {...props} desktopPluginSurfaceContext={context} />
    </DesktopPluginSurface>
  )
}

interface InputComposerCoreProps extends InputComposerProps {
  desktopPluginSurfaceContext: DesktopPluginComposerSurfaceContext
}

function InputComposerCore({
  threadId,
  workspacePath,
  fileWorkspacePath,
  modelName = 'Default',
  providerId,
  providerOptions = [],
  modelOptions = [],
  modelCatalog = [],
  reasoningValue = 'off',
  speedValue = 'standard',
  modelLoading = false,
  modelDisabled = false,
  remoteWorkspace = false,
  modelListUnsupportedEndpoint = false,
  modelCatalogError = false,
  modelCatalogErrorMessage = null,
  onModelChange,
  onProviderChange,
  onReasoningChange,
  onSpeedChange,
  onModelCatalogRetry,
  contextMode,
  contextSupportsMax,
  contextDegraded,
  contextConfiguredWindow,
  onContextModeChange,
  minimalChrome = false,
  mascotAvatar,
  variant = 'default',
  placeholder,
  prefillRequest = null,
  onBeforeSend,
  submitOverride,
  transientVoiceOrigin = false,
  dockPadding = composerDockStyle.padding,
  desktopPluginSurfaceContext
}: InputComposerCoreProps): JSX.Element {
  const t = useT()
  const isAgentBuilder = variant === 'agentBuilder'
  const hasSubmitOverride = submitOverride !== undefined
  const [images, setImages] = useState<ImageAttachment[]>([])
  const [files, setFiles] = useState<ComposerFileAttachment[]>([])
  const [atQuery, setAtQuery] = useState<string | null>(null)
  const [mentionDismissed, setMentionDismissed] = useState(false)
  const [slashQuery, setSlashQuery] = useState<string | null>(null)
  const [slashDismissed, setSlashDismissed] = useState(false)
  const [commandQuery, setCommandQuery] = useState<string | null>(null)
  const [skillQuery, setSkillQuery] = useState<string | null>(null)
  const [skillDismissed, setSkillDismissed] = useState(false)
  const [goalPopoverOpen, setGoalPopoverOpen] = useState(false)
  const [goalComposeMode, setGoalComposeMode] = useState(false)
  const [profilePickerOpen, setProfilePickerOpen] = useState(false)
  const [goalPillActive, setGoalPillActive] = useState(false)
  const [goalBusy, setGoalBusy] = useState(false)
  const [compactBusy, setCompactBusy] = useState(false)
  const [consolidateBusy, setConsolidateBusy] = useState(false)
  const [editingQueuedInputId, setEditingQueuedInputId] = useState<string | null>(null)
  const [dragOver, setDragOver] = useState(false)
  const [editorFocused, setEditorFocused] = useState(false)
  /** Bumps on rich-input edits so `canSend` re-evaluates from ref (contentEditable has no React state). */
  const [contentRevision, setContentRevision] = useState(0)
  const [historyCursor, setHistoryCursor] = useState<number | null>(null)
  const [mascotBounce, setMascotBounce] = useState(0)
  const richRef = useRef<RichInputAreaHandle>(null)
  const sendInFlightRef = useRef(false)
  const editingQueuedInputIdRef = useRef<string | null>(null)
  const pendingModeChangeRef = useRef<Promise<unknown> | null>(null)
  const applyingHistoryRef = useRef(false)
  const historyDraftRef = useRef<ComposerDraftSnapshot | null>(null)
  // Latest composer contents, mirrored from change handlers so the per-thread
  // draft can be saved on unmount even after `richRef` has been detached.
  const latestDraftRef = useRef<ComposerDraftSnapshot>(emptyComposerDraftSnapshot())
  const capabilities = useConnectionStore((s) => s.capabilities)
  const effectiveFileWorkspacePath = fileWorkspacePath ?? workspacePath
  const activeThread = useThreadStore((s) => s.activeThread?.id === threadId ? s.activeThread : null)
  const voiceRecording = useVoiceStore((state) => state.recording?.threadId === threadId)
  const voiceProcessing = useVoiceStore((state) => isVoiceProcessingForThread(
    state.snapshot,
    state.finalizing?.threadId,
    threadId
  ))
  const compactVoiceFooter = useVoiceStore((state) => shouldUseCompactVoiceFooter(
    state.snapshot,
    state.recording?.threadId,
    state.finalizing?.threadId,
    threadId
  ))

  // Load providers once so the ChatGPT subscription badge can render in the composer footer.
  const reloadProviders = useProvidersStore((s) => s.reload)
  useEffect(() => {
    if (capabilities?.providerManagement !== true) return
    void reloadProviders()
  }, [capabilities?.providerManagement, reloadProviders])
  const activeCatalogProviderId = useModelCatalogStore((s) => s.providerId)
  const activeChatGptProvider = useChatGptOAuthSummary(activeCatalogProviderId)

  const turns = useConversationStore((s) => s.turns)
  const turnStatus = useConversationStore((s) => s.turnStatus)
  const activeTurnId = useConversationStore((s) => s.activeTurnId)
  const pendingMessage = useConversationStore((s) => s.pendingMessage)
  const queuedInputs = useConversationStore((s) => s.queuedInputs)
  const interruptingTurnId = useConversationStore((s) => s.interruptingTurnId)
  const maintenanceKind = useConversationStore((s) => s.maintenanceKind)
  const rawMascotInteraction = useComposerMascot({ threadId, workspacePath })
  const mascotInteraction = hasSubmitOverride ? undefined : rawMascotInteraction
  const threadMode = useConversationStore((s) => s.threadMode)
  const setThreadMode = useConversationStore((s) => s.setThreadMode)
  const composerPrefill = useUIStore((s) => s.composerPrefill)
  const composerFileAttachmentRequest = useUIStore((s) => s.composerFileAttachmentRequest)
  const currentGoal = useThreadStore((s) => s.goalSnapshots.get(threadId) ?? null)
  const visibleQueuedInputs = hasSubmitOverride ? [] : queuedInputs
  const visiblePendingMessage = hasSubmitOverride ? null : pendingMessage
  const hasBackgroundActivityDock = !hasSubmitOverride && queuedInputs.length > 0
  const locale = useLocale()
  const confirm = useConfirmDialog()
  const activeMainView = useUIStore((s) => s.activeMainView)
  const desktopCommandContributions = useDesktopPluginRegistry((s) => s.commands)

  const isRunning = !hasSubmitOverride && turnStatus === 'running'
  const isWaitingApproval = !hasSubmitOverride && turnStatus === 'waitingApproval'
  const isWaitingInput = !hasSubmitOverride && turnStatus === 'waitingInput'
  const isMaintenanceActive = !hasSubmitOverride && (maintenanceKind === 'compacting' || maintenanceKind === 'consolidating')
  const isBusyForInput = isRunning || isMaintenanceActive
  const canUseCommandPicker = capabilities?.commandManagement === true
  const canUseSkillPicker = capabilities?.skillsManagement === true
  const canUseThreadGoals = !isAgentBuilder && capabilities?.threadGoals === true
  const canUseManualCompaction = !isAgentBuilder && capabilities?.manualCompaction === true
  const canUseManualMemoryConsolidation = !isAgentBuilder && capabilities?.manualMemoryConsolidation === true
  const turnsLength = turns.length
  const canCompactCurrentThread = canUseManualCompaction && turnsLength > 0 && turnStatus === 'idle' && !isMaintenanceActive
  const canConsolidateCurrentThread = canUseManualMemoryConsolidation && turnsLength > 0 && turnStatus === 'idle' && !isMaintenanceActive
  const canUseSystemActions = !isAgentBuilder
  const canUseAgentProfiles = !isAgentBuilder && capabilities?.agentProfileManagement === true
  const rawProfileId = (activeThread?.configuration as Record<string, unknown> | null | undefined)?.agentProfileId
  const activeProfileId = typeof rawProfileId === 'string' && rawProfileId.length > 0 ? rawProfileId : undefined
  const hasProfile = activeProfileId !== undefined
  // Prefer the profile's configured (stored) avatar over a derived one so the mascot
  // matches the builder gallery and picker instead of a name-hash.
  const resolvedProfileAvatar = useResolvedProfileAvatar(activeProfileId, workspacePath)
  const effectiveMascotAvatar = mascotAvatar ?? resolvedProfileAvatar
  const desktopCommandContext = useMemo(() => ({
    workspacePath: workspacePath || null,
    threadId,
    viewId: activeMainView
  }), [activeMainView, threadId, workspacePath])
  const desktopCommands = useMemo(
    () => desktopCommandContributions.filter((command) =>
      isDesktopPluginContributionAvailable(command, desktopCommandContext)),
    [desktopCommandContext, desktopCommandContributions]
  )
  const canUseSlashPicker = canUseCommandPicker
    || canUseSkillPicker
    || canUseThreadGoals
    || canUseSystemActions
    || desktopCommands.length > 0
  const showMentionPopover = atQuery !== null && !mentionDismissed && !remoteWorkspace
  const normalizedSlashQuery = slashQuery?.toLowerCase() ?? null
  const isAgentBuilderModeSlashQuery = isAgentBuilder
    && (normalizedSlashQuery === 'plan' || normalizedSlashQuery === 'agent')
  const isExactSystemSlashQuery = !isAgentBuilder
    && (normalizedSlashQuery === 'plan' || normalizedSlashQuery === 'agent' || normalizedSlashQuery === 'init' || normalizedSlashQuery === 'compact' || normalizedSlashQuery === 'consolidate')
  const showSlashPopover = slashQuery !== null && !slashDismissed && canUseSlashPicker && !isExactSystemSlashQuery && !isAgentBuilderModeSlashQuery
  const showCommandQueryPopover = commandQuery !== null && canUseSlashPicker
  const showCommandPopover = showSlashPopover || showCommandQueryPopover
  const commandPopoverQuery = commandQuery ?? slashQuery ?? ''
  const showSkillPopover = skillQuery !== null && !skillDismissed && canUseSkillPicker
  const { commands: customCommands, initAvailable, status: customCommandStatus, reload: reloadCommands } = useCustomCommandCatalog({
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
  const richRefCatalog = useMemo(
    () => ({
      commands: customCommands,
      skills: availableSkills
    }),
    [availableSkills, customCommands]
  )
  const composerHistory = useMemo(
    () => buildComposerHistory(turns, threadId),
    [threadId, turns]
  )
  const systemActions = useMemo(
    () => {
      const actions: SlashSystemActionInfo[] = []
      if (!canUseSystemActions) return actions
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
      if (!hasProfile) {
        actions.push({
          id: 'planMode',
          label: t('composer.system.plan'),
          description: threadMode === 'agent'
            ? t('composer.system.plan.enable')
            : t('composer.system.plan.disable'),
          keywords: ['plan', 'agent'],
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
      if (canCompactCurrentThread) {
        actions.push({
          id: 'compact',
          label: t('composer.system.compact'),
          description: t('composer.system.compact.description'),
          keywords: ['compact'],
          icon: <ChevronsDown size={15} strokeWidth={2} aria-hidden />
        })
      }
      if (canConsolidateCurrentThread) {
        actions.push({
          id: 'consolidate',
          label: t('composer.system.consolidate'),
          description: t('composer.system.consolidate.description'),
          keywords: ['consolidate', 'memory'],
          icon: <Archive size={15} strokeWidth={2} aria-hidden />
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
    [canCompactCurrentThread, canConsolidateCurrentThread, canUseAgentProfiles, canUseCommandPicker, canUseSystemActions, canUseThreadGoals, hasProfile, initAvailable, t, threadMode]
  )

  useEffect(() => {
    if (!canUseSkillPicker) return
    void fetchSkills()
  }, [canUseSkillPicker, fetchSkills])

  useEffect(() => {
    setHistoryCursor(null)
    historyDraftRef.current = null
  }, [threadId])

  const captureComposerDraft = useCallback((): ComposerDraftSnapshot => ({
    text: richRef.current?.getText() ?? '',
    segments: richRef.current?.getSegments() ?? [],
    files: [...files],
    images: [...images]
  }), [files, images])

  const applyComposerSnapshot = useCallback((
    snapshot: ComposerHistoryEntry,
    nextFiles: ComposerFileAttachment[] = [],
    nextImages: ImageAttachment[] = []
  ): void => {
    applyingHistoryRef.current = true
    try {
      richRef.current?.setContent({
        text: snapshot.text,
        segments: snapshot.segments
      })
      const cursor = linearLengthOfComposerEntry(snapshot)
      richRef.current?.setSelectionRange({ start: cursor, end: cursor })
      setFiles([...nextFiles])
      setImages([...nextImages])
    } finally {
      applyingHistoryRef.current = false
    }
  }, [])

  const handleComposerContentChange = useCallback((): void => {
    latestDraftRef.current = {
      ...latestDraftRef.current,
      text: richRef.current?.getText() ?? '',
      segments: richRef.current?.getSegments() ?? []
    }
    setContentRevision((n) => n + 1)
    if (applyingHistoryRef.current) return
    setHistoryCursor(null)
    historyDraftRef.current = null
  }, [])

  useEffect(() => {
    latestDraftRef.current = { ...latestDraftRef.current, images }
  }, [images])
  useEffect(() => {
    latestDraftRef.current = { ...latestDraftRef.current, files }
  }, [files])

  const resetComposerInput = useCallback((): void => {
    richRef.current?.clear()
    setImages([])
    setFiles([])
    latestDraftRef.current = emptyComposerDraftSnapshot()
    useComposerDraftStore.getState().clearDraft(threadId)
  }, [threadId])

  // Restore a saved draft on (re)mount and save on unmount or thread switch. Drafts
  // are in-memory only (see composerDraftStore); sending clears them.
  useEffect(() => {
    const id = threadId
    latestDraftRef.current = emptyComposerDraftSnapshot()
    let restoreTimer: number | undefined
    // A one-shot prefill (e.g. "Try in chat") takes precedence over a saved draft.
    if (!useUIStore.getState().composerPrefill) {
      const draft = useComposerDraftStore.getState().getDraft(id)
      if (draft && threadComposerDraftHasContent(draft)) {
        restoreTimer = window.setTimeout(() => {
          applyComposerSnapshot({ text: draft.text, segments: draft.segments }, draft.files, draft.images)
          latestDraftRef.current = {
            text: draft.text,
            segments: [...draft.segments],
            files: [...draft.files],
            images: [...draft.images]
          }
        }, 0)
      }
    }
    return () => {
      if (restoreTimer !== undefined) window.clearTimeout(restoreTimer)
      const snapshot = latestDraftRef.current
      if (threadComposerDraftHasContent(snapshot)) {
        useComposerDraftStore.getState().saveDraft(id, snapshot)
      } else {
        useComposerDraftStore.getState().clearDraft(id)
      }
    }
  }, [threadId, applyComposerSnapshot])

  const handleHistoryNavigate = useCallback((direction: 'previous' | 'next'): boolean => {
    if (showMentionPopover || showCommandPopover || showSkillPopover || goalPopoverOpen) {
      return false
    }
    const historyCount = composerHistory.length
    if (historyCount === 0) return false

    if (direction === 'previous') {
      const currentCursor = Math.min(Math.max(historyCursor ?? historyCount, 0), historyCount)
      if (currentCursor <= 0) return false
      if (historyCursor === null) {
        historyDraftRef.current = captureComposerDraft()
      }
      const nextCursor = currentCursor - 1
      const entry = composerHistory[nextCursor]
      if (!entry) return false
      setHistoryCursor(nextCursor)
      applyComposerSnapshot(entry)
      return true
    }

    if (historyCursor === null) return false
    const nextCursor = historyCursor + 1
    if (nextCursor >= historyCount) {
      const draft = historyDraftRef.current
      setHistoryCursor(null)
      historyDraftRef.current = null
      if (draft) {
        applyComposerSnapshot(draft, draft.files, draft.images)
      } else {
        applyComposerSnapshot({ text: '', segments: [] })
      }
      return true
    }

    const entry = composerHistory[nextCursor]
    if (!entry) return false
    setHistoryCursor(nextCursor)
    applyComposerSnapshot(entry)
    return true
  }, [
    applyComposerSnapshot,
    captureComposerDraft,
    composerHistory,
    goalPopoverOpen,
    historyCursor,
    showMentionPopover,
    showSkillPopover,
    showCommandPopover
  ])

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

  useEffect(() => {
    if (composerPrefill) {
      const prefill = composerPrefill
      useUIStore.getState().consumeComposerPrefill()
      setTimeout(() => {
        richRef.current?.setPlainText(prefill)
        richRef.current?.focus()
      }, 0)
    }
  }, [composerPrefill])

  useEffect(() => {
    if (!composerFileAttachmentRequest) return
    const attachment = useUIStore.getState().consumeComposerFileAttachmentRequest()
    if (!attachment) return
    if (remoteWorkspace) {
      addToast(t('input.remoteLocalFilesUnavailable'), 'warning')
      return
    }
    setFiles((current) => mergeComposerFileAttachments(current, [attachment]))
    setTimeout(() => richRef.current?.focus(), 0)
  }, [composerFileAttachmentRequest, remoteWorkspace, t])

  useEffect(() => {
    const prefill = prefillRequest?.text
    if (!prefill) return
    setTimeout(() => {
      richRef.current?.setPlainText(prefill)
      richRef.current?.setSelectionRange({ start: prefill.length, end: prefill.length })
      richRef.current?.focus()
    }, 0)
  }, [prefillRequest?.id, prefillRequest?.text])

  useEffect(() => {
    const focus = (): void => {
      richRef.current?.focus()
    }
    const setTextAndFocus = (value: string): void => {
      richRef.current?.setPlainText(value)
      setTimeout(() => richRef.current?.focus(), 0)
    }
    ;(window as Window & { __inputComposerFocus?: () => void }).__inputComposerFocus = focus
    ;(window as Window & { __inputComposerSetText?: (v: string) => void }).__inputComposerSetText = setTextAndFocus
    return () => {
      delete (window as Window & { __inputComposerFocus?: () => void }).__inputComposerFocus
      delete (window as Window & { __inputComposerSetText?: (v: string) => void }).__inputComposerSetText
    }
  }, [])

  const prevTurnStatusRef = useRef(turnStatus)
  useEffect(() => {
    const prev = prevTurnStatusRef.current
    if (prev === 'waitingApproval' && turnStatus !== 'waitingApproval') {
      richRef.current?.focus()
    }
    prevTurnStatusRef.current = turnStatus
  }, [turnStatus])

  const previousCommandRefreshStatusRef = useRef(turnStatus)
  useEffect(() => {
    const previous = previousCommandRefreshStatusRef.current
    previousCommandRefreshStatusRef.current = turnStatus
    if (previous !== 'idle' && turnStatus === 'idle' && canUseCommandPicker) {
      void reloadCommands()
    }
  }, [canUseCommandPicker, reloadCommands, turnStatus])

  const ensureCurrentGoal = useCallback(async (): Promise<ThreadGoal | null> => {
    if (currentGoal) return currentGoal
    const raw = await window.api.appServer.sendRequest('thread/goal/get', { threadId })
    const goal = extractGoal(raw)
    if (goal) {
      useThreadStore.getState().setThreadGoal(goal)
    } else {
      useThreadStore.getState().clearThreadGoal(threadId)
    }
    return goal
  }, [currentGoal, threadId])

  const showGoalUnavailable = useCallback((): void => {
    addToast(t('goal.toast.unsupported'), 'warning')
  }, [t])

  const setGoalObjective = useCallback(async (objective: string): Promise<boolean> => {
    if (!canUseThreadGoals) {
      showGoalUnavailable()
      return false
    }
    const trimmedObjective = objective.trim()
    if (!trimmedObjective) {
      addToast(t('goal.toast.emptyObjective'), 'warning')
      return false
    }

    setGoalBusy(true)
    try {
      const existing = await ensureCurrentGoal()
      const replacing =
        existing != null &&
        existing.status !== 'complete' &&
        existing.objective.trim() !== trimmedObjective
      if (replacing) {
        const accepted = await confirm({
          title: t('goal.replaceConfirm.title'),
          message: t('goal.replaceConfirm.message', {
            current: existing.objective,
            next: trimmedObjective
          }),
          confirmLabel: t('goal.replaceConfirm.confirm'),
          cancelLabel: t('goal.action.cancel')
        })
        if (!accepted) return false
      }

      const result = await window.api.appServer.sendRequest('thread/goal/set', {
        threadId,
        objective: trimmedObjective
      })
      const goal = extractGoal(result)
      if (goal) {
        useThreadStore.getState().setThreadGoal(goal)
      }
      return true
    } catch (err) {
      addToast(t('goal.toast.updateFailed', { error: err instanceof Error ? err.message : String(err) }), 'error')
      try {
        await ensureCurrentGoal()
      } catch {
        // Best-effort refresh only.
      }
      return false
    } finally {
      setGoalBusy(false)
    }
  }, [canUseThreadGoals, confirm, ensureCurrentGoal, showGoalUnavailable, t, threadId])

  const updateGoalStatus = useCallback(async (status: 'active' | 'paused'): Promise<boolean> => {
    if (!canUseThreadGoals) {
      showGoalUnavailable()
      return false
    }
    setGoalBusy(true)
    try {
      const existing = await ensureCurrentGoal()
      if (!existing) {
        addToast(t('goal.toast.noCurrent'), 'warning')
        return false
      }
      const result = await window.api.appServer.sendRequest('thread/goal/set', {
        threadId,
        status
      })
      const goal = extractGoal(result)
      if (goal) {
        useThreadStore.getState().setThreadGoal(goal)
      }
      return true
    } catch (err) {
      addToast(t('goal.toast.updateFailed', { error: err instanceof Error ? err.message : String(err) }), 'error')
      try {
        await ensureCurrentGoal()
      } catch {
        // Best-effort refresh only.
      }
      return false
    } finally {
      setGoalBusy(false)
    }
  }, [canUseThreadGoals, ensureCurrentGoal, showGoalUnavailable, t, threadId])

  const clearGoal = useCallback(async (): Promise<boolean> => {
    if (!canUseThreadGoals) {
      showGoalUnavailable()
      return false
    }
    setGoalBusy(true)
    try {
      await window.api.appServer.sendRequest('thread/goal/clear', { threadId })
      useThreadStore.getState().clearThreadGoal(threadId)
      return true
    } catch (err) {
      addToast(t('goal.toast.updateFailed', { error: err instanceof Error ? err.message : String(err) }), 'error')
      try {
        await ensureCurrentGoal()
      } catch {
        // Best-effort refresh only.
      }
      return false
    } finally {
      setGoalBusy(false)
    }
  }, [canUseThreadGoals, ensureCurrentGoal, showGoalUnavailable, t, threadId])

  const executeGoalCommand = useCallback(async (command: GoalSlashCommand): Promise<boolean> => {
    if (!canUseThreadGoals) {
      showGoalUnavailable()
      return false
    }
    if (command.kind === 'show') {
      setGoalPopoverOpen(true)
      try {
        await ensureCurrentGoal()
      } catch {
        // Showing an empty panel is still useful when refresh fails.
      }
      return true
    }
    if (command.kind === 'set') return setGoalObjective(command.objective)
    if (command.kind === 'pause') return updateGoalStatus('paused')
    if (command.kind === 'resume') return updateGoalStatus('active')
    return clearGoal()
  }, [canUseThreadGoals, clearGoal, ensureCurrentGoal, setGoalObjective, showGoalUnavailable, updateGoalStatus])

  const enterGoalComposeMode = useCallback((): void => {
    if (!canUseThreadGoals) {
      showGoalUnavailable()
      return
    }
    setGoalComposeMode(true)
    window.setTimeout(() => richRef.current?.focus(), 0)
  }, [canUseThreadGoals, showGoalUnavailable])

  // Flattens the rich input and attachments into one objective string, then submits it
  // as a normal turn so the agent starts on it and the message gets the goal badge.
  const sendGoalFromComposer = useCallback(async (): Promise<void> => {
    const text = richRef.current?.getText() ?? ''
    const segments = richRef.current?.getSegments() ?? []
    const capturedFiles = [...files]
    const capturedImages = [...images]
    const objective = buildGoalObjective({ text, segments, files, images })
    if (!objective.trim()) {
      addToast(t('goal.toast.emptyObjective'), 'warning')
      return
    }
    if (remoteWorkspace && (images.length > 0 || files.length > 0)) {
      addToast(t('input.remoteLocalFilesUnavailable'), 'warning')
      return
    }
    if (sendInFlightRef.current) return
    sendInFlightRef.current = true
    try {
      const ok = await setGoalObjective(objective)
      if (!ok) return
      if (isBusyForInput) {
        const { inputParts } = buildComposerInputParts({ text: objective })
        await window.api.appServer.sendRequest('turn/enqueue', {
          threadId,
          input: inputParts,
          sender: undefined,
          sentAsGoal: true
        })
      } else {
        await startTurnWithOptimisticUI({
          threadId,
          workspacePath: effectiveFileWorkspacePath,
          identityWorkspacePath: workspacePath,
          text: objective,
          segments: [],
          images: [],
          files: [],
          fallbackThreadName: t('toast.imageMessage'),
          fileFallbackThreadName: t('toast.fileReferenceMessage'),
          attachmentFallbackThreadName: t('toast.attachmentMessage'),
          throwOnStartError: true,
          sentAsGoal: true
        })
      }
      setGoalComposeMode(false)
      setMascotBounce((n) => n + 1)
      resetComposerInput()
    } catch (err) {
      richRef.current?.setContent({ text, segments })
      setFiles(capturedFiles)
      setImages(capturedImages)
      addToast(err instanceof Error ? err.message : String(err), 'error')
    } finally {
      sendInFlightRef.current = false
    }
  }, [files, images, remoteWorkspace, setGoalObjective, resetComposerInput, isBusyForInput, threadId, effectiveFileWorkspacePath, workspacePath, t])

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
          t('input.imageTooLarge', { mb: MAX_IMAGE_BYTES / 1024 / 1024 }),
          'warning'
        )
        return
      }
      if (images.length >= MAX_IMAGES) {
        addToast(t('input.maxImages', { max: MAX_IMAGES }), 'warning')
        return
      }
      try {
        const { path } = await window.api.workspace.saveImageToTemp({ dataUrl, fileName })
        setImages((prev) => [
          ...prev,
          { tempPath: path, dataUrl, fileName, mimeType }
        ])
      } catch (e) {
        const msg = e instanceof Error ? e.message : String(e)
        addToast(t('input.saveImageFailed', { error: msg }), 'error')
      }
    },
    [images.length, remoteWorkspace, t]
  )

  const onPasteImage = useCallback(
    (file: File): void => {
      if (remoteWorkspace) {
        addToast(t('input.remoteLocalFilesUnavailable'), 'warning')
        return
      }
      if (!isImageFile(file)) {
        addToast(
          t('input.unsupportedImage', { ext: extForFile(file.name) || 'unknown' }),
          'warning'
        )
        return
      }
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

  const sendMessage = useCallback(async (draftOverride?: ThreadComposerDraftInput) => {
    if (!isAgentBuilder && goalComposeMode && canUseThreadGoals) {
      await sendGoalFromComposer()
      return
    }
    const text = draftOverride?.text ?? richRef.current?.getText() ?? ''
    const segments = draftOverride?.segments ?? richRef.current?.getSegments() ?? []
    const inputImages = draftOverride?.images ?? images
    const inputFiles = draftOverride?.files ?? files
    const trimmed = text.trim()
    if (!trimmed && inputImages.length === 0 && inputFiles.length === 0) return
    if (isWaitingApproval || isWaitingInput) return
    if (modelLoading) return
    if (remoteWorkspace && (inputImages.length > 0 || inputFiles.length > 0)) {
      addToast(t('input.remoteLocalFilesUnavailable'), 'warning')
      return
    }

    if (!isAgentBuilder && trimmed.toLowerCase() === '/init') {
      if (sendInFlightRef.current) return
      sendInFlightRef.current = true
      try {
        const expandedPrompt = await expandInitCommand(threadId)
        resetComposerInput()
        await startTurnWithOptimisticUI({
          threadId,
          workspacePath: effectiveFileWorkspacePath,
          identityWorkspacePath: workspacePath,
          text: expandedPrompt,
          fallbackThreadName: t('cmd.init'),
          throwOnStartError: true
        })
      } catch (err) {
        addToast(err instanceof Error ? err.message : String(err), 'error')
      } finally {
        sendInFlightRef.current = false
      }
      return
    }

    const systemCommand = isAgentBuilder ? null : parseSystemSlashCommand(trimmed)
    if (systemCommand) {
      let clearInput = false
      if (systemCommand.kind === 'plan') clearInput = await setComposerMode('plan')
      else if (systemCommand.kind === 'agent') clearInput = await setComposerMode('agent')
      else if (systemCommand.kind === 'compact') clearInput = await compactThreadContext()
      else clearInput = await consolidateThreadMemory()
      if (clearInput) {
        resetComposerInput()
      }
      return
    }

    const goalCommand: GoalSlashCommand | null = isAgentBuilder ? null : parseGoalSlashCommand(trimmed)
    if (goalCommand) {
      const clearInput = await executeGoalCommand(goalCommand)
      if (clearInput) {
        resetComposerInput()
      }
      return
    }

    try {
      await onBeforeSend?.()
    } catch (err) {
      addToast(err instanceof Error ? err.message : String(err), 'error')
      return
    }

    setMascotBounce((n) => n + 1)

    if (pendingModeChangeRef.current) {
      await pendingModeChangeRef.current
    }

    if (submitOverride) {
      if (sendInFlightRef.current) return
      sendInFlightRef.current = true
      const capturedImages = [...inputImages]
      const capturedFiles = [...inputFiles]
      const capturedSegments = [...segments]
      const { inputParts, visibleText, bodyText } = buildComposerInputParts({
        text: trimmed,
        segments: capturedSegments,
        files: capturedFiles,
        images: capturedImages
      })
      try {
        resetComposerInput()
        await submitOverride({
          text: trimmed,
          segments: capturedSegments,
          files: capturedFiles,
          images: capturedImages,
          inputParts,
          visibleText,
          bodyText
        })
      } catch (err) {
        console.error('composer submit override failed:', err)
        richRef.current?.setContent({ text: trimmed, segments: capturedSegments })
        setImages(capturedImages)
        setFiles(capturedFiles)
        addToast(err instanceof Error ? err.message : String(err), 'error')
      } finally {
        sendInFlightRef.current = false
      }
      return
    }

    if (isBusyForInput) {
      if (sendInFlightRef.current) return
      sendInFlightRef.current = true
      try {
        if (trimmed || inputFiles.length > 0 || inputImages.length > 0) {
          const { inputParts } = buildComposerInputParts({
            text: trimmed,
            segments,
            files: inputFiles,
            images: inputImages
          })
          if (isRunning) {
            if (!activeTurnId || activeTurnId.startsWith('local-turn-')) {
              throw new Error('The active turn is not ready for steering yet. Your draft was preserved.')
            }
            await window.api.appServer.sendRequest('turn/steer', {
              threadId,
              expectedTurnId: activeTurnId,
              input: inputParts,
              sender: undefined
            })
          } else {
            await window.api.appServer.sendRequest('turn/enqueue', {
              threadId,
              input: inputParts,
              sender: undefined
            })
          }
        }
        resetComposerInput()
      } catch (err) {
        console.error(isRunning ? 'turn/steer failed:' : 'turn/enqueue failed:', err)
        addToast(err instanceof Error ? err.message : String(err), 'error')
      } finally {
        sendInFlightRef.current = false
      }
      return
    }

    if (sendInFlightRef.current) return
    sendInFlightRef.current = true
    const capturedImages = [...inputImages]
    const capturedFiles = [...inputFiles]
    const capturedSegments = [...segments]
    const { inputParts } = buildComposerInputParts({
      text: trimmed,
      segments: capturedSegments,
      files: capturedFiles,
      images: capturedImages
    })
    try {
      resetComposerInput()
      await startTurnWithOptimisticUI({
        threadId,
        workspacePath: effectiveFileWorkspacePath,
        identityWorkspacePath: workspacePath,
        text: trimmed,
        segments,
        images: capturedImages,
        files: capturedFiles,
        fallbackThreadName: t('toast.imageMessage'),
        fileFallbackThreadName: t('toast.fileReferenceMessage'),
        attachmentFallbackThreadName: t('toast.attachmentMessage'),
        throwOnStartError: true
      })
    } catch (err) {
      console.error('turn/start failed:', err)
      const currentMaintenanceKind = useConversationStore.getState().maintenanceKind
      if (isTurnBusyError(err)
        && (currentMaintenanceKind === 'compacting' || currentMaintenanceKind === 'consolidating')) {
        try {
          await window.api.appServer.sendRequest('turn/enqueue', {
            threadId,
            input: inputParts,
            sender: undefined
          })
          return
        } catch (enqueueErr) {
          console.error('turn/enqueue fallback failed:', enqueueErr)
          richRef.current?.setContent({ text: trimmed, segments: capturedSegments })
          setImages(capturedImages)
          setFiles(capturedFiles)
          addToast(enqueueErr instanceof Error ? enqueueErr.message : String(enqueueErr), 'error')
          return
        }
      }
      richRef.current?.setContent({ text: trimmed, segments: capturedSegments })
      setImages(capturedImages)
      setFiles(capturedFiles)
      addToast(err instanceof Error ? err.message : String(err), 'error')
    } finally {
      sendInFlightRef.current = false
    }
  }, [activeTurnId, compactThreadContext, consolidateThreadMemory, effectiveFileWorkspacePath, executeGoalCommand, files, images, isAgentBuilder, isBusyForInput, isRunning, isWaitingApproval, isWaitingInput, modelLoading, onBeforeSend, remoteWorkspace, setComposerMode, submitOverride, threadId, workspacePath, t, goalComposeMode, canUseThreadGoals, sendGoalFromComposer])

  useEffect(() => registerComposerVoiceTarget(threadId, {
    capture: captureComposerDraft,
    apply: (draft) => {
      applyComposerSnapshot({ text: draft.text, segments: draft.segments }, draft.files, draft.images)
      latestDraftRef.current = {
        text: draft.text,
        segments: [...draft.segments],
        files: [...draft.files],
        images: [...draft.images]
      }
    },
    submit: sendMessage
  }), [applyComposerSnapshot, captureComposerDraft, sendMessage, threadId])

  useEffect(() => () => {
    if (transientVoiceOrigin) void useVoiceStore.getState().discardOrigin(threadId)
  }, [threadId, transientVoiceOrigin])

  const removeQueuedInput = useCallback(async (queuedInputId: string): Promise<void> => {
    try {
      const res = await window.api.appServer.sendRequest('turn/queue/remove', { threadId, queuedInputId }) as {
        queuedInputs?: unknown[]
      }
      useConversationStore.getState().setQueuedInputs((res.queuedInputs ?? []) as QueuedTurnInput[])
    } catch (err) {
      addToast(err instanceof Error ? err.message : String(err), 'error')
    }
  }, [threadId])

  const editQueuedInput = useCallback(async (queuedInputId: string): Promise<void> => {
    if (editingQueuedInputIdRef.current) return
    const queued = useConversationStore.getState().queuedInputs.find((item) => item.id === queuedInputId)
    if (!queued || (queued.status !== 'queued' && queued.status !== 'guidancePending') || queued.triggerKind || queued.sentAsGoal === true) return

    editingQueuedInputIdRef.current = queuedInputId
    setEditingQueuedInputId(queuedInputId)
    try {
      const draft = await queuedInputToComposerDraft(queued)
      const res = await window.api.appServer.sendRequest('turn/queue/remove', {
        threadId,
        queuedInputId
      }) as { queuedInputs?: unknown[] }

      useConversationStore.getState().setQueuedInputs((res.queuedInputs ?? []) as QueuedTurnInput[])
      applyComposerSnapshot(draft, draft.files, draft.images)
      latestDraftRef.current = {
        text: draft.text,
        segments: [...draft.segments],
        files: [...draft.files],
        images: [...draft.images]
      }
      if (threadComposerDraftHasContent(draft)) {
        useComposerDraftStore.getState().saveDraft(threadId, draft)
      } else {
        useComposerDraftStore.getState().clearDraft(threadId)
      }
      setHistoryCursor(null)
      historyDraftRef.current = null
      window.setTimeout(() => richRef.current?.focus(), 0)
    } catch (err) {
      addToast(
        t('composer.queueEditFailed', {
          error: err instanceof Error ? err.message : String(err)
        }),
        'error'
      )
    } finally {
      editingQueuedInputIdRef.current = null
      setEditingQueuedInputId(null)
    }
  }, [applyComposerSnapshot, threadId, t])

  const steerQueuedInput = useCallback(async (queuedInputId: string): Promise<void> => {
    const state = useConversationStore.getState()
    const queued = state.queuedInputs.find((item) => item.id === queuedInputId)
    if (!queued) return
    try {
      const status = queued.status === 'guidancePending' ? 'queued' : 'guidancePending'
      const expectedTurnId = status === 'queued'
        ? (queued.readyAfterTurnId ?? state.activeTurnId)
        : state.activeTurnId
      if (!expectedTurnId) return
      const res = await window.api.appServer.sendRequest('turn/queue/update', {
        threadId,
        expectedTurnId,
        queuedInputId,
        status
      }) as { queuedInputs?: unknown[] }
      useConversationStore.getState().setQueuedInputs((res.queuedInputs ?? []) as QueuedTurnInput[])
    } catch (err) {
      addToast(err instanceof Error ? err.message : String(err), 'error')
    }
  }, [threadId])

  const reorderQueuedInputs = useCallback(async (orderedQueuedInputIds: string[]): Promise<void> => {
    const previousQueue = useConversationStore.getState().queuedInputs
    const previousById = new Map(previousQueue.map((item) => [item.id, item]))
    const optimisticQueue = orderedQueuedInputIds
      .map((id) => previousById.get(id))
      .filter((item): item is QueuedTurnInput => item !== undefined)
    if (optimisticQueue.length !== previousQueue.length) return

    useConversationStore.getState().setQueuedInputs(optimisticQueue)
    try {
      const res = await window.api.appServer.sendRequest('turn/queue/reorder', {
        threadId,
        orderedQueuedInputIds
      }) as { queuedInputs?: unknown[] }
      useConversationStore.getState().setQueuedInputs((res.queuedInputs ?? []) as QueuedTurnInput[])
    } catch (err) {
      useConversationStore.getState().setQueuedInputs(previousQueue)
      addToast(
        t('composer.queueReorderFailed', {
          error: err instanceof Error ? err.message : String(err)
        }),
        'error'
      )
    }
  }, [threadId, t])

  const stopTurn = useCallback(async () => {
    const state = useConversationStore.getState()
    const activeTurnId = state.activeTurnId
    try {
      if (activeTurnId && !activeTurnId.startsWith('local-turn-')) {
        await interruptTurn({
          threadId,
          turnId: activeTurnId,
          onError: (error) => {
            addToast(t('composer.stopFailed', {
              error: error instanceof Error ? error.message : String(error)
            }), 'error')
          }
        })
        return
      }
      if (state.maintenanceKind === 'compacting' || state.maintenanceKind === 'consolidating') {
        await window.api.appServer.sendRequest('thread/maintenance/interrupt', { threadId })
      }
    } catch (err) {
      console.error('interrupt failed:', err)
    }
  }, [threadId, t])

  async function setComposerMode(nextMode: 'agent' | 'plan'): Promise<boolean> {
    if (hasProfile) return false
    if (pendingModeChangeRef.current) return false
    const previousMode = useConversationStore.getState().threadMode
    if (previousMode === nextMode) return true

    setThreadMode(nextMode)
    const request = window.api.appServer
      .sendRequest('thread/mode/set', {
        threadId,
        mode: nextMode
      })
      .catch((err) => {
        console.error('thread/mode/set failed:', err)
        setThreadMode(previousMode)
        addToast(
          t('composer.modeSwitchFailed', {
            error: err instanceof Error ? err.message : String(err)
          }),
          'error'
        )
        return false
      })
      .finally(() => {
        if (pendingModeChangeRef.current === request) {
          pendingModeChangeRef.current = null
        }
      })

    pendingModeChangeRef.current = request
    const result = await request
    return result !== false
  }

  async function toggleMode(): Promise<void> {
    const previousMode = useConversationStore.getState().threadMode
    const newMode = previousMode === 'agent' ? 'plan' : 'agent'
    await setComposerMode(newMode)
  }

  async function compactThreadContext(): Promise<boolean> {
    if (compactBusy) return false
    if (!canCompactCurrentThread) {
      addToast(t('composer.compact.unavailable'), 'warning')
      return false
    }

    setCompactBusy(true)
    addToast(t('composer.compact.started'), 'info')
    try {
      const result = (await window.api.appServer.sendRequest(
        'thread/compact/start',
        { threadId },
        MANUAL_COMPACTION_TIMEOUT_MS
      )) as {
        outcome?: string
        message?: string
        contextUsage?: ContextUsageSnapshotWire | null
      }
      if (result.contextUsage) {
        useConversationStore.getState().setContextUsage(result.contextUsage)
      }
      const outcome = String(result.outcome ?? '').toLowerCase()
      if (outcome === 'micro' || outcome === 'partial') {
        await refreshThreadAfterManualCompact()
        addToast(t('composer.compact.succeeded'), 'success')
      } else if (outcome === 'skipped') {
        addToast(t('composer.compact.skipped'), 'info')
      } else {
        addToast(t('composer.compact.failed', { error: result.message || outcome || 'unknown' }), 'error')
      }
      return true
    } catch (err) {
      if (isRequestTimeoutError(err) && useConversationStore.getState().maintenanceKind === 'compacting') {
        addToast(t('composer.compact.stillRunning'), 'info')
        return true
      }
      addToast(t('composer.compact.failed', { error: err instanceof Error ? err.message : String(err) }), 'error')
      return false
    } finally {
      setCompactBusy(false)
    }
  }

  async function refreshThreadAfterManualCompact(): Promise<void> {
    try {
      const response = await readThreadHistoryHead(
        (method, params) => window.api.appServer.sendRequest(method, params),
        threadId
      )
      const refreshed = response.thread
      if (!refreshed || useThreadStore.getState().activeThreadId !== threadId) return

      useThreadStore.getState().setActiveThread(refreshed)
      useThreadStore.getState().setActiveHistoryCursors(threadId, response.turnCursor)
      useConversationStore.getState().setTurns(
        (refreshed.turns ?? []).map((turn) =>
          wireTurnToConversationTurn(turn as unknown as Record<string, unknown>)
        )
      )
      useConversationStore.getState().setQueuedInputs(refreshed.queuedInputs ?? [])
      if ('contextUsage' in refreshed) {
        useConversationStore.getState().setContextUsage(refreshed.contextUsage ?? null)
      }
    } catch (err) {
      console.warn('thread/read after manual compaction failed:', err)
    }
  }

  async function consolidateThreadMemory(): Promise<boolean> {
    if (consolidateBusy) return false
    if (!canConsolidateCurrentThread) {
      addToast(t('composer.consolidate.unavailable'), 'warning')
      return false
    }

    setConsolidateBusy(true)
    addToast(t('composer.consolidate.started'), 'info')
    try {
      const result = (await window.api.appServer.sendRequest(
        'thread/memory/consolidate/start',
        { threadId },
        MANUAL_MEMORY_CONSOLIDATION_TIMEOUT_MS
      )) as {
        outcome?: string
        message?: string
        memoryWritten?: boolean
        historyWritten?: boolean
      }
      const outcome = String(result.outcome ?? '').toLowerCase()
      if (outcome === 'succeeded') {
        addToast(t('composer.consolidate.succeeded'), 'success')
      } else if (outcome === 'skipped') {
        addToast(t('composer.consolidate.skipped'), 'info')
      } else {
        addToast(t('composer.consolidate.failed', { error: result.message || outcome || 'unknown' }), 'error')
      }
      return true
    } catch (err) {
      addToast(t('composer.consolidate.failed', { error: err instanceof Error ? err.message : String(err) }), 'error')
      return false
    } finally {
      setConsolidateBusy(false)
    }
  }

  const canSend = useMemo(() => {
    const textLen = (richRef.current?.getText() ?? '').trim().length
    return (textLen > 0 || images.length > 0 || files.length > 0) && !isWaitingApproval && !isWaitingInput && !modelLoading
  }, [contentRevision, files.length, images.length, isWaitingApproval, isWaitingInput, modelLoading])
  const canSendWithVoice = voiceRecording || (canSend && !voiceProcessing)
  const submitOrStopVoice = useCallback((): void => {
    if (voiceRecording && !isBusyForInput) {
      void useVoiceStore.getState().stopRecording('send')
      return
    }
    void sendMessage()
  }, [isBusyForInput, sendMessage, voiceRecording])

  const onSelectFile = useCallback(
    (relativePath: string): void => {
      richRef.current?.insertFileTag(relativePath)
    },
    []
  )

  const onSelectCommand = useCallback((commandName: string): void => {
    richRef.current?.insertCommandTag(commandName)
  }, [])

  const onSelectDesktopCommand = useCallback((contributionKey: string): void => {
    setSlashDismissed(true)
    richRef.current?.removeCommandQuery()
    void executeDesktopPluginCommand(contributionKey, desktopCommandContext)
  }, [desktopCommandContext])

  const applyProfile = useCallback(async (profileId: string): Promise<void> => {
    setProfilePickerOpen(false)
    try {
      // refreshThread resolves and applies the profile's compiled configuration (tools/mcp/skills/model).
      const res = await window.api.appServer.sendRequest('agent/profiles/refreshThread', { threadId, profileId }) as { config?: Record<string, unknown> }
      // A profile-backed thread has no operational mode; reset the optimistic local mode to agent.
      setThreadMode('agent')
      const active = useThreadStore.getState().activeThread
      if (active && active.id === threadId) {
        const nextConfig = res.config && typeof res.config === 'object'
          ? res.config
          : { ...(active.configuration ?? {}), agentProfileId: profileId }
        useThreadStore.getState().setActiveThread({ ...active, configuration: nextConfig as unknown as typeof active.configuration })
      }
    } catch (err) {
      addToast(t('composer.profile.changeFailed', { error: err instanceof Error ? err.message : String(err) }), 'error')
    }
  }, [threadId, setThreadMode, t])

  const clearProfile = useCallback(async (): Promise<void> => {
    try {
      const readRes = await window.api.appServer.sendRequest('thread/read', { threadId }) as { thread?: { configuration?: Record<string, unknown> | null } }
      const config = readRes.thread?.configuration && typeof readRes.thread.configuration === 'object'
        ? { ...readRes.thread.configuration }
        : {}
      delete config.agentProfileId
      delete config.agentProfileFingerprint
      const active = useThreadStore.getState().activeThread
      if (active && active.id === threadId) {
        useThreadStore.getState().setActiveThread({ ...active, configuration: config as unknown as typeof active.configuration })
      }
      await window.api.appServer.sendRequest('thread/config/update', { threadId, config })
    } catch (err) {
      addToast(t('composer.profile.changeFailed', { error: err instanceof Error ? err.message : String(err) }), 'error')
    }
  }, [threadId, t])

  const onSelectSystemAction = useCallback((actionId: string): void => {
    setSlashDismissed(true)
    richRef.current?.removeCommandQuery()
    if (actionId === 'planMode') {
      void toggleMode()
      return
    }
    if (actionId === 'init') {
      richRef.current?.setContent({ text: '/init', segments: [] })
      void sendMessage()
      return
    }
    if (actionId === 'profile') {
      setProfilePickerOpen(true)
      return
    }
    if (actionId === 'compact') {
      void compactThreadContext()
      return
    }
    if (actionId === 'consolidate') {
      void consolidateThreadMemory()
      return
    }
    if (actionId !== 'goal') return
    if (currentGoal) {
      setGoalPopoverOpen(true)
      void ensureCurrentGoal().catch(() => {})
    } else {
      enterGoalComposeMode()
    }
  }, [currentGoal, enterGoalComposeMode, ensureCurrentGoal, compactThreadContext, consolidateThreadMemory, sendMessage, toggleMode])

  const onSelectSkill = useCallback((skillName: string): void => {
    richRef.current?.insertSkillTag(skillName)
  }, [])
  const effectiveComposerDockStyle =
    dockPadding === composerDockStyle.padding
      ? composerDockStyle
      : { ...composerDockStyle, padding: dockPadding }
  const mascotEffectState = resolveComposerMascotEffectState({
    modelName,
    modelCatalog,
    reasoningValue,
    speedValue,
    contextMode,
    contextDegraded
  })
  return (
    <>
      <div style={effectiveComposerDockStyle}>
        <ConversationColumn>
        <DesktopPluginSurface name="composer.before" context={desktopPluginSurfaceContext} />
        {visiblePendingMessage && <PendingMessageIndicator message={visiblePendingMessage} />}
      <ComposerShell
        desktopPluginSurfaceContext={desktopPluginSurfaceContext}
        dragOver={dragOver}
        dropLabel={t('composer.dropImage')}
        topAccessory={(
          <QueuedInputDock
            queuedInputs={visibleQueuedInputs}
            onQueueSteer={(id: string) => { void steerQueuedInput(id) }}
            onQueueRemove={(id: string) => { void removeQueuedInput(id) }}
            onQueueEdit={(id: string) => { void editQueuedInput(id) }}
            onQueueReorder={(orderedIds: string[]) => { void reorderQueuedInputs(orderedIds) }}
            editingQueuedInputId={editingQueuedInputId}
          />
        )}
        topAccessoryVisible={hasBackgroundActivityDock}
        onDragOver={onDragOver}
        onDragLeave={onDragLeave}
        onDrop={onDrop}
        focused={editorFocused}
        showMascot
        mascotBounceSignal={mascotBounce}
        mascotInteraction={mascotInteraction}
        mascotReasoningEffort={mascotEffectState.reasoningEffort}
        mascotSpeed={mascotEffectState.speed}
        mascotContextMax={mascotEffectState.contextMax}
        mascotAvatar={effectiveMascotAvatar}
        mascotHandoff
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
                goal={currentGoal}
                busy={goalBusy}
                onSetObjective={setGoalObjective}
                onPause={() => updateGoalStatus('paused')}
                onResume={() => updateGoalStatus('active')}
                onClear={clearGoal}
                onDismiss={() => {
                  setGoalPopoverOpen(false)
                }}
              />
              <ProfilePickerPopover
                visible={profilePickerOpen}
                activeProfileId={activeProfileId}
                onPick={(profileId) => {
                  void applyProfile(profileId)
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
                desktopCommands={desktopCommands}
                skills={availableSkills}
                onSelectSystemAction={onSelectSystemAction}
                onSelectCommand={onSelectCommand}
                onSelectDesktopCommand={onSelectDesktopCommand}
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
                workspacePath={effectiveFileWorkspacePath}
                onSelect={onSelectFile}
                onDismiss={() => {
                  setMentionDismissed(true)
                }}
              />
              <RichInputArea
                ref={richRef}
                chrome="minimal"
                disabled={isWaitingApproval || isWaitingInput}
                suppressSubmit={showMentionPopover || showCommandPopover || showSkillPopover || modelLoading}
                onToggleModeShortcut={isAgentBuilder ? undefined : () => {
                  void toggleMode()
                }}
                onHistoryNavigate={handleHistoryNavigate}
                historyNavigationActive={historyCursor !== null}
                placeholder={
                  isWaitingApproval
                    ? t('composer.placeholder.approval')
                    : isWaitingInput
                      ? t('composer.placeholder.userInput')
                      : goalComposeMode
                        ? t('goal.objective.placeholder')
                        : placeholder ?? t('composer.placeholder.ask')
                }
                onSubmit={() => {
                  submitOrStopVoice()
                }}
                onAtQuery={remoteWorkspace ? undefined : handleAtQuery}
                onSlashQuery={handleSlashQuery}
                onCommandQuery={handleCommandQuery}
                onSkillQuery={handleSkillQuery}
                onContentChange={handleComposerContentChange}
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
          <ComposerToolbarLeadingSlots
            context={desktopPluginSurfaceContext}
            compact={compactVoiceFooter}
            commands={(
              <ComposerCommandTrigger
                label={t('composer.openCommands')}
                expanded={showCommandPopover}
                active={showCommandQueryPopover}
                disabled={!canUseSlashPicker || isWaitingApproval || isWaitingInput}
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
            )}
            voiceStatus={<VoiceInputStatus threadId={threadId} />}
            permissions={!compactVoiceFooter && !minimalChrome ? (
              <ApprovalPolicyPicker threadId={threadId} disabled={isWaitingApproval || isWaitingInput} />
            ) : null}
            mode={!compactVoiceFooter ? (
              hasProfile ? (
                <ComposerCustomProfileLabel
                  label={t('composer.mode.custom')}
                    onClear={() => {
                      void clearProfile()
                    }}
                    title={t('composer.customPill.title', { name: activeProfileId ?? '' })}
                    ariaLabel={t('composer.customPill.aria')}
                  />
                ) : !isAgentBuilder ? (
                  <ComposerPlanModeLabel
                    value={threadMode}
                    onDisable={() => {
                      void setComposerMode('agent')
                    }}
                    label={t('composer.mode.plan')}
                    shortcut={ACTION_SHORTCUTS.toggleMode}
                    title={t('composer.planPill.create')}
                    ariaLabel={t('composer.system.plan.disable')}
                  />
                ) : (
                  null
                )
            ) : null}
            goal={!compactVoiceFooter ? (
              canUseThreadGoals && !isAgentBuilder && (
                goalComposeMode ? (
                    <GoalComposePill
                      label={t('goal.system.label')}
                      title={t('goal.compose.active')}
                      ariaLabel={t('goal.compose.exit')}
                      onExit={() => setGoalComposeMode(false)}
                    />
                  ) : currentGoal ? (
                    <ActionTooltip label={currentGoal.objective} placement="top">
                      <button
                        type="button"
                        onClick={() => {
                          setGoalPopoverOpen(true)
                          void ensureCurrentGoal().catch(() => {})
                        }}
                        onMouseEnter={() => setGoalPillActive(true)}
                        onMouseLeave={() => setGoalPillActive(false)}
                        onFocus={(event) => {
                          if (event.currentTarget.matches(':focus-visible')) setGoalPillActive(true)
                        }}
                        onBlur={() => setGoalPillActive(false)}
                        aria-label={t('goal.pill.aria', { status: t(`goal.status.${currentGoal.status}`) })}
                        style={goalPillStyle(currentGoal.status, goalPillActive)}
                      >
                        <Target size={13} aria-hidden />
                        <span>{t(`goal.pill.${currentGoal.status}`)}</span>
                      </button>
                    </ActionTooltip>
                  ) : null
                )
            ) : null}
          />
        }
        footerAction={
          <ComposerToolbarTrailingSlots
            context={desktopPluginSurfaceContext}
            contextUsage={!compactVoiceFooter && !hasSubmitOverride ? <ContextUsageRing /> : null}
            model={!compactVoiceFooter ? (
              <ModelPicker
                providerId={providerId}
                providerOptions={providerOptions}
                modelName={modelName}
                modelOptions={modelOptions}
                modelCatalog={modelCatalog}
                reasoningValue={reasoningValue}
                speedValue={speedValue}
                loading={modelLoading}
                unsupported={modelListUnsupportedEndpoint}
                modelListReady={!modelLoading && !modelListUnsupportedEndpoint && !modelCatalogError && modelOptions.length > 0}
                errorMessage={modelCatalogError ? (modelCatalogErrorMessage || t('composer.modelListError')) : null}
                disabled={modelDisabled || isWaitingApproval || isWaitingInput}
                onChange={onModelChange}
                onProviderChange={onProviderChange}
                allowDefaultModel={false}
                onReasoningChange={onReasoningChange}
                onSpeedChange={onSpeedChange}
                onRetry={onModelCatalogRetry}
                contextMode={contextMode}
                contextSupportsMax={contextSupportsMax}
                contextDegraded={contextDegraded}
                contextConfiguredWindow={contextConfiguredWindow}
                onContextModeChange={onContextModeChange}
                shortcut={ACTION_SHORTCUTS.selectModel}
                triggerStyle={composerModelPillStyle(
                  modelDisabled || modelLoading ? 'var(--composer-footer-muted)' : 'var(--composer-footer-highlight)',
                  modelDisabled || modelLoading
                )}
              />
            ) : null}
            voice={!isWaitingApproval && !isWaitingInput ? (
              <VoiceInputControl threadId={threadId} />
            ) : null}
            submit={!isWaitingApproval && !isWaitingInput ? (
              isBusyForInput ? (
                canSend ? (
                  <ComposerSubmitButton mode={isRunning ? 'steer' : 'queue'} onClick={sendMessage} />
                ) : (
                  <ComposerSubmitButton
                    mode={interruptingTurnId ? 'stopping' : 'stop'}
                    tone="enabled"
                    disabled={Boolean(interruptingTurnId)}
                    onClick={stopTurn}
                  />
                )
              ) : (
                <ComposerSubmitButton
                  mode="send"
                  tone={canSendWithVoice ? 'enabled' : 'disabled'}
                  disabled={!canSendWithVoice}
                  onClick={submitOrStopVoice}
                />
              )
            ) : null}
          />
        }
        belowFooter={(
          <ComposerStatusContent
            context={desktopPluginSurfaceContext}
            topSpacing={!minimalChrome}
            workspace={minimalChrome ? null : (
              <ComposerWorkspaceFooter
                workspacePath={effectiveFileWorkspacePath}
                mode={activeThread?.worktree ? 'worktree' : 'local'}
                variant="thread"
                thread={activeThread}
                remoteWorkspace={remoteWorkspace}
                turnRunning={isRunning || isWaitingApproval || isWaitingInput}
              />
            )}
            subscription={minimalChrome ? null : <ChatGptUsageBadge provider={activeChatGptProvider} />}
          />
        )}
        />
        <DesktopPluginSurface name="composer.after" context={desktopPluginSurfaceContext} />
        </ConversationColumn>
      </div>
    </>
  )
}

const composerDockStyle: CSSProperties = {
  flexShrink: 0,
  padding: '0 clamp(20px, 4vw, 40px)'
}

function buildComposerHistory(turns: ConversationTurn[], threadId: string): ComposerHistoryEntry[] {
  const entries: ComposerHistoryEntry[] = []
  for (const turn of turns) {
    if (turn.threadId !== threadId) continue
    for (const item of turn.items) {
      const entry = userItemToComposerHistoryEntry(item)
      if (entry) entries.push(entry)
    }
  }
  return entries
}

function userItemToComposerHistoryEntry(item: ConversationItem): ComposerHistoryEntry | null {
  if (item.type !== 'userMessage') return null
  if (item.deliveryMode === 'guidance') return null
  const text = item.text ?? ''

  const inputParts = item.nativeInputParts ?? item.materializedInputParts
  if (inputParts && inputParts.length > 0) {
    const segments = inputPartsToComposerSegments(inputParts)
    const serialized = stringifyComposerDraftSegments(segments).trim()
    if (serialized.length === 0) return null
    return {
      text: serialized,
      segments
    }
  }

  const trimmedText = text.trim()
  if (trimmedText.length === 0) return null
  return {
    text,
    segments: []
  }
}

function inputPartsToComposerSegments(parts: InputPart[]): ComposerDraftSegment[] {
  const segments: ComposerDraftSegment[] = []
  for (const part of parts) {
    switch (part.type) {
      case 'text':
        pushComposerTextSegment(segments, part.text)
        break
      case 'fileRef':
        segments.push({ type: 'file', relativePath: part.displayPath ?? part.path })
        break
      case 'commandRef':
        pushCommandRefSegments(segments, part)
        break
      case 'skillRef':
        if (part.name.trim().length > 0) {
          segments.push({ type: 'skill', skillName: part.name.trim() })
        }
        break
      default:
        break
    }
  }
  return segments
}

async function queuedInputToComposerDraft(item: QueuedTurnInput): Promise<ComposerDraftSnapshot> {
  const parts = item.nativeInputParts?.length
    ? item.nativeInputParts
    : item.materializedInputParts?.length
      ? item.materializedInputParts
      : null

  if (!parts) {
    if (!item.displayText) throw new Error('Queued message has no editable content.')
    return {
      text: item.displayText,
      segments: [{ type: 'text', value: item.displayText }],
      files: [],
      images: []
    }
  }

  if (parts.some((part) => part.type === 'image')) {
    throw new Error('Remote image inputs cannot be restored in the composer.')
  }

  const segments = inputPartsToComposerSegments(parts)
  const images: ImageAttachment[] = []
  for (const part of parts) {
    if (part.type !== 'localImage') continue
    const { dataUrl } = await window.api.workspace.readImageAsDataUrl({ path: part.path })
    if (!dataUrl) throw new Error(`Unable to read queued image: ${part.fileName || part.path}`)
    images.push({
      tempPath: part.path,
      dataUrl,
      fileName: part.fileName?.trim() || fileNameFromPath(part.path),
      mimeType: part.mimeType?.trim() || mimeTypeFromDataUrl(dataUrl) || 'image/png'
    })
  }

  return {
    text: stringifyComposerDraftSegments(segments),
    segments,
    files: [],
    images
  }
}

function fileNameFromPath(path: string): string {
  return path.split(/[/\\]/).pop() || path
}

function mimeTypeFromDataUrl(dataUrl: string): string | null {
  const match = /^data:([^;,]+)[;,]/i.exec(dataUrl)
  return match?.[1] ?? null
}

function pushComposerTextSegment(segments: ComposerDraftSegment[], value: string): void {
  if (value.length === 0) return
  const previous = segments[segments.length - 1]
  if (previous?.type === 'text') {
    previous.value += value
    return
  }
  segments.push({ type: 'text', value })
}

function pushCommandRefSegments(
  segments: ComposerDraftSegment[],
  part: Extract<InputPart, { type: 'commandRef' }>
): void {
  const rawText = typeof part.rawText === 'string' ? part.rawText.trim() : ''
  const name = typeof part.name === 'string' ? part.name.trim().replace(/^\/+/, '') : ''
  const normalizedRaw = rawText.length > 0
    ? (rawText.startsWith('/') ? rawText : `/${rawText}`)
    : name.length > 0
      ? `/${name}`
      : ''
  if (normalizedRaw.length === 0) return

  const firstWhitespace = normalizedRaw.search(/\s/)
  const command = firstWhitespace >= 0 ? normalizedRaw.slice(0, firstWhitespace) : normalizedRaw
  const rawArgs = firstWhitespace >= 0 ? normalizedRaw.slice(firstWhitespace + 1).trim() : ''
  const argsText = (part.argsText?.trim() || rawArgs).trim()
  segments.push({ type: 'command', command })
  if (argsText.length > 0) {
    pushComposerTextSegment(segments, ` ${argsText}`)
  }
}

function linearLengthOfComposerEntry(entry: ComposerHistoryEntry): number {
  if (entry.segments.length === 0) return entry.text.length
  return entry.segments.reduce((total, segment) => {
    if (segment.type === 'text') return total + segment.value.length
    return total + 1
  }, 0)
}

function parseSystemSlashCommand(text: string): { kind: 'plan' | 'agent' | 'compact' | 'consolidate' } | null {
  const trimmed = text.trim().toLowerCase()
  if (trimmed === '/plan') return { kind: 'plan' }
  if (trimmed === '/agent') return { kind: 'agent' }
  if (trimmed === '/compact') return { kind: 'compact' }
  if (trimmed === '/consolidate') return { kind: 'consolidate' }
  return null
}

function goalPillStyle(_status: ThreadGoal['status'], active = false): CSSProperties {
  const color = 'var(--composer-footer-text)'
  return {
    display: 'inline-flex',
    alignItems: 'center',
    gap: 6,
    maxWidth: 260,
    minHeight: COMPOSER_FOOTER_CONTROL_HEIGHT,
    border: 'none',
    borderRadius: 8,
    background: active ? composerFooterControlHoverBackground : 'transparent',
    color,
    cursor: 'pointer',
    fontSize: 'var(--type-secondary-size)',
    lineHeight: 'var(--type-secondary-line-height)',
    fontWeight: 'var(--type-ui-emphasis-weight)',
    padding: '2px 6px',
    overflow: 'hidden',
    whiteSpace: 'nowrap',
    transition: 'background-color 120ms ease, color 120ms ease'
  }
}
