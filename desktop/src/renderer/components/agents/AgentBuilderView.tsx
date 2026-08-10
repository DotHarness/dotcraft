import {
  useCallback,
  useEffect,
  useLayoutEffect,
  useMemo,
  useRef,
  useState,
  type CSSProperties,
  type Dispatch,
  type JSX,
  type ReactNode,
  type SetStateAction
} from 'react'
import { createPortal } from 'react-dom'
import type { ClientRequestMethods } from '@dotcraft/sdk/contracts'
import { ArrowLeft, BookOpen, CircleHelp, Clock, Eye, FileSearch, FileText, Globe, ListChecks, MoreHorizontal, MousePointer2, Pencil, Plus, RefreshCw, Search, Server, Shuffle, Tag, Trash2, Wrench, X, type LucideIcon } from 'lucide-react'
import { showToast } from '../../stores/toastStore'
import { useModelCatalogStore } from '../../stores/modelCatalogStore'
import { useProvidersStore } from '../../stores/providersStore'
import { useConversationStore } from '../../stores/conversationStore'
import { useUIStore } from '../../stores/uiStore'
import { useLocale, useT } from '../../contexts/LocaleContext'
import { Input } from '../ui/Input'
import { ConversationPanel } from '../layout/ConversationPanel'
import { DragHandle } from '../layout/DragHandle'
import { InputComposer, type InputComposerSubmitPayload } from '../conversation/InputComposer'
import { useComposerModelControls } from '../conversation/useComposerModelControls'
import {
  createCatalogDefaultPreference,
  PreferenceModelPicker
} from '../conversation/PreferenceModelPicker'
import { MarkdownRenderer } from '../conversation/MarkdownRenderer'
import type { ThreadConfigurationWire } from '../../types/thread'
import { formatRelativeTime } from '../../utils/relativeTime'
import {
  AGENT_BUILDER_CHAT_MIN_WIDTH,
  resolveAgentBuilderChatWidth,
  resolveMaxAgentBuilderChatWidth
} from '../../utils/agentBuilderLayout'
import { CatalogCompactGrid, CatalogHoverButton, CatalogSearchBox, CatalogSection, CatalogTopBar, CATALOG_TOOLBAR_CONTROL_RADIUS, CATALOG_TOOLBAR_CONTROL_SIZE, styles as catalogStyles } from '../catalog/CatalogSurface'
import { ContextMenu, type ContextMenuPosition } from '../ui/ContextMenu'
import { SettingsGroup, SettingsRow } from '../settings/SettingsGroup'
import { SettingsSelect } from '../settings/ui/SettingsSelect'
import { PillSwitch } from '../ui/PillSwitch'
import { Button } from '../ui/Button'
import { IconButton } from '../ui/IconButton'
import { RobotAvatar } from './RobotAvatar'
import { AGENT_BUILDER_AVATAR, randomAvatar, resolveProfileAvatar, type AvatarSpec } from './agentAvatar'
import { useAgentProfileAvatarStore } from '../../stores/agentProfileAvatarStore'
import {
  findProviderPreference,
  mergeProviderPreferences,
  type ModelPreference,
  type ProviderPreferences
} from '../../../shared/modelPreference'
import {
  AGENT_CONTROL_OPTIONS,
  APPROVAL_OPTIONS,
  createEmptyDraft,
  parseProfile,
  toMarkdown,
  type AgentControl,
  type AgentProviderPreference,
  type ApprovalPolicy,
  type ProfileDraft,
  type SaveTarget,
  type ToolPolicyMode
} from './agentProfileDraft'
import { applyBuilderChange, isBuilderField, type BuilderField, type BuilderToolResult } from './agentBuilderDraftSync'
import { useAgentBuilderConversation } from './useAgentBuilderConversation'
import { AgentSaveTargetDialog } from './AgentSaveTargetDialog'
import { AgentBuilderChatEmptyState } from './AgentBuilderChatEmptyState'
import './AgentBuilderView.css'

// ── Wire shapes (subset; see specs/protocols/appserver-protocol.md) ──

interface ProfileEntry {
  id: string
  name?: string
  description?: string
  avatar?: number | AvatarSpec
  source: string
  valid?: boolean
  readOnly?: boolean
  isBuiltIn?: boolean
  shadowed?: boolean
  rawContent?: string
  updatedAt?: string
}

interface ToolInfo {
  name: string
  description?: string
  icon?: string
  planMode?: boolean
}

interface SkillInfo {
  name: string
  displayName?: string
  description?: string
  source?: string
  enabled?: boolean
}

function toAgentProviderPreference(
  providerId: string,
  preference: ModelPreference
): AgentProviderPreference {
  return {
    providerId,
    model: preference.model,
    reasoning: {
      enabled: preference.reasoning.enabled,
      effort: preference.reasoning.effort
    },
    speed: preference.speed,
    contextWindow: { mode: preference.contextWindow.mode }
  }
}

type Filter = 'all' | 'builtIn' | 'user' | 'workspace'

type Route =
  | { name: 'gallery' }
  | { name: 'intro' }
  | {
      name: 'builder'
      draft: ProfileDraft
      id: string | null
      source: string | null
      readOnly: boolean
      isNew: boolean
      saveTarget: SaveTarget
      saving: boolean
      avatar: AvatarSpec
      /** Whether the profile is persisted (existing, or created via the Create button). */
      created: boolean
      /** ISO last-updated time of the persisted profile; drives "Updated X ago". */
      updatedAt: string | null
    }

const SUGGESTIONS: { icon: LucideIcon; title: string; desc: string; prompt: string }[] = [
  { icon: FileSearch, title: 'Code review', desc: 'Read-only: find correctness gaps and missing tests', prompt: 'A read-only code reviewer focused on correctness, risk, and missing tests.' },
  { icon: FileText, title: 'Doc writer', desc: 'Draft and maintain docs in the established style', prompt: 'A documentation writer that drafts and keeps docs consistent in the established style.' },
  { icon: Tag, title: 'Bug triage', desc: 'Prioritize incoming bugs and log them', prompt: 'A bug triage agent that reviews incoming bugs, prioritizes them, and logs them.' }
]

const FAN = [
  { rot: -11, y: 12 },
  { rot: -6, y: 4 },
  { rot: 0, y: 0 },
  { rot: 6, y: 4 },
  { rot: 11, y: 12 }
]

const BUILDER_FIELD_LABEL_KEYS: Record<BuilderField, string> = {
  name: 'agentBuilder.field.name',
  description: 'agentBuilder.field.description',
  instructions: 'agentBuilder.field.instructions',
  'tools.policy': 'agentBuilder.field.tools',
  'mcp.servers': 'agentBuilder.field.mcp',
  'skills.preload': 'agentBuilder.field.skills',
  providerPreference: 'agentBuilder.field.model',
  approval: 'agentBuilder.field.approval',
  'tools.agentControl': 'agentBuilder.field.toolControl'
}

const INSTRUCTIONS_PLACEHOLDER = 'Give your agent instructions on how to operate — its job, boundaries, and what it handles…'

type CatalogKind = 'tool' | 'mcp' | 'skill'

const TOOL_ICON_BY_NAME: Record<string, LucideIcon> = {
  WebSearch: Search,
  WebFetch: Globe,
  WriteFile: Pencil,
  ReadFile: FileText,
  TodoWrite: ListChecks,
  Cron: Clock,
  RequestUserInput: CircleHelp
}

function catalogIcon(kind: CatalogKind, id: string): LucideIcon {
  if (kind === 'mcp') return Server
  if (kind === 'skill') return BookOpen
  return TOOL_ICON_BY_NAME[id] ?? Wrench
}

const MARKER_TARGET_SELECTOR = '[data-agent-builder-marker-target]'
const MARKER_FALLBACK_SELECTOR = [
  MARKER_TARGET_SELECTOR,
  '.dc-settings-select__value',
  'input',
  'textarea',
  '.agent-builder-chip-label',
  '.agent-builder-pick-empty',
  '.agent-builder-add'
].join(', ')
const MARKER_OFFSET_X_ATTR = 'data-agent-builder-marker-offset-x'
const MARKER_OFFSET_Y_ATTR = 'data-agent-builder-marker-offset-y'
const MARKER_SELECTOR = '.agent-builder-edit-marker'
const MARKER_FLIP_PAD = 12

function markerOffset(target: HTMLElement, attr: string): number | null {
  const value = target.getAttribute(attr)
  if (value == null) return null
  const parsed = Number.parseFloat(value)
  return Number.isFinite(parsed) ? parsed : null
}

let markerTextCanvas: HTMLCanvasElement | null = null

// Width of `line` as the element actually renders it — used to find where the
// text ends inside a full-width input/textarea (its box edge is uninformative).
function markerTextWidth(el: HTMLElement, line: string): number {
  try {
    const canvas = (markerTextCanvas ??= document.createElement('canvas'))
    const ctx = canvas.getContext('2d')
    if (!ctx) return 0
    const style = window.getComputedStyle(el)
    ctx.font = `${style.fontWeight} ${style.fontSize} ${style.fontFamily}`
    return ctx.measureText(line).width
  } catch {
    return 0
  }
}

// Distance from a marker target's left edge to the end of its rendered content.
// Text fields span the full width, so we measure the text rather than trust the
// box edge; every other target hugs its own content.
function markerContentEnd(target: HTMLElement): number {
  if (target instanceof HTMLInputElement || target instanceof HTMLTextAreaElement) {
    const style = window.getComputedStyle(target)
    const paddingLeft = Number.parseFloat(style.paddingLeft) || 0
    const paddingRight = Number.parseFloat(style.paddingRight) || 0
    const raw = target.value || target.placeholder || ''
    const line = target instanceof HTMLTextAreaElement ? (raw.split('\n')[0] ?? '') : raw
    const end = paddingLeft + markerTextWidth(target, line)
    const maxEnd = target.clientWidth - paddingRight
    return maxEnd > 0 ? Math.min(end, maxEnd) : end
  }
  return target.getBoundingClientRect().width
}

const galleryAvatar: CSSProperties = { flex: '0 0 auto', display: 'inline-flex' }
const galleryText: CSSProperties = { minWidth: 0, flex: 1, display: 'flex', flexDirection: 'column' }

async function rpc<T>(
  method: keyof ClientRequestMethods,
  params: ClientRequestMethods[keyof ClientRequestMethods]['params'] = {}
): Promise<T> {
  return (await window.api.appServer.sendRequest(method, params)) as T
}

function errorText(err: unknown): string {
  return err instanceof Error ? err.message : String(err)
}

function sectionFor(source: string): Filter {
  if (source === 'user') return 'user'
  if (source === 'workspace') return 'workspace'
  return 'builtIn'
}

function writableSource(source: string | null): SaveTarget {
  return source === 'user' ? 'user' : 'workspace'
}

function avatarForEntry(entry: Pick<ProfileEntry, 'id' | 'avatar'>): AvatarSpec {
  return resolveProfileAvatar(entry.id, entry.avatar)
}

function draftWithAvatar(draft: ProfileDraft, avatar: AvatarSpec): ProfileDraft {
  return draft.avatar ? draft : { ...draft, avatar }
}

function newDraftTargetId(): string {
  return `draft-agent-${globalThis.crypto?.randomUUID?.() ?? Date.now().toString(36)}`
}

export function AgentBuilderView(): JSX.Element {
  const [profiles, setProfiles] = useState<ProfileEntry[]>([])
  const [status, setStatus] = useState<'loading' | 'ready' | 'error'>('loading')
  const [loadError, setLoadError] = useState<string | null>(null)
  const [query, setQuery] = useState('')
  const [menuPos, setMenuPos] = useState<ContextMenuPosition | null>(null)
  const [route, setRoute] = useState<Route>({ name: 'gallery' })

  const [toolCatalog, setToolCatalog] = useState<ToolInfo[]>([])
  const [skillCatalog, setSkillCatalog] = useState<SkillInfo[]>([])
  const [mcpServers, setMcpServers] = useState<string[]>([])

  const [viewMode, setViewMode] = useState<'edit' | 'preview'>('edit')
  const [autoSaveState, setAutoSaveState] = useState<'idle' | 'saving' | 'saved' | 'error'>('idle')
  const lastSavedMdRef = useRef<string | null>(null)
  const lastSavedIdRef = useRef<string | null>(null)
  const lastSavedSourceRef = useRef<SaveTarget | null>(null)

  // Conversational builder target. The hidden thread is created lazily on the first chat send.
  // Kept separate from the live draft so editing the name doesn't retarget the conversation.
  const [builderSession, setBuilderSession] = useState<{
    targetId: string
    targetSource: string
  } | null>(null)
  const [introPrefillRequest, setIntroPrefillRequest] = useState<{ id: number; text: string } | null>(null)
  const [builderPrefillRequest, setBuilderPrefillRequest] = useState<{ id: number; text: string } | null>(null)
  const introPrefillSeqRef = useRef(0)
  const builderPrefillSeqRef = useRef(0)
  // The field the agent most recently edited — drives the cursor marker after a tool result lands.
  const [highlight, setHighlight] = useState<{ field: BuilderField; seq: number } | null>(null)
  const [editingField, setEditingField] = useState<BuilderField | null>(null)
  const highlightSeqRef = useRef(0)
  const builderSplitRef = useRef<HTMLDivElement>(null)
  const builderSplitWidthRef = useRef<number | null>(null)
  const [builderSplitWidth, setBuilderSplitWidth] = useState<number | null>(null)
  const [builderChatDividerActive, setBuilderChatDividerActive] = useState(false)
  const [builderChatResizing, setBuilderChatResizing] = useState(false)
  // Create flow: the save target (user/workspace) is chosen in a dialog opened from the Create button.
  const [createDialogOpen, setCreateDialogOpen] = useState(false)
  const workspacePath = useConversationStore((s) => s.workspacePath)
  const builderTurnStatus = useConversationStore((s) => s.turnStatus)
  const agentBuilderChatWidth = useUIStore((s) => s.agentBuilderChatWidth)
  const agentBuilderChatWidthRatio = useUIStore((s) => s.agentBuilderChatWidthRatio)
  const draftSyncTimerRef = useRef<number | null>(null)
  const draftSyncPromiseRef = useRef<Promise<void> | null>(null)
  const lastSyncedBuilderDraftRef = useRef<string | null>(null)
  const latestBuilderDraftRef = useRef<string>('')

  useEffect(() => {
    if (!highlight) return undefined
    const timer = window.setTimeout(() => setHighlight(null), 1500)
    return () => window.clearTimeout(timer)
  }, [highlight])

  // Apply one streamed builder tool result to the live draft and flag the edited field.
  const handleBuilderResult = useCallback((result: BuilderToolResult): void => {
    if (!result.ok || !isBuilderField(result.field)) return
    setHighlight({ field: result.field, seq: (highlightSeqRef.current += 1) })
    setRoute((r) => (r.name === 'builder' ? { ...r, draft: applyBuilderChange(r.draft, result).draft } : r))
  }, [])

  const builderConversation = useAgentBuilderConversation({
    active: route.name === 'builder',
    onResult: handleBuilderResult,
    onEditingField: setEditingField
  })
  const builderConversationStatus = builderConversation.status
  const builderConversationError = builderConversation.error
  const syncBuilderDraftRequest = builderConversation.syncDraft
  const startBuilderConversation = builderConversation.start

  useLayoutEffect(() => {
    if (route.name !== 'builder') return undefined
    const node = builderSplitRef.current
    if (!node) return undefined

    const measure = (): void => {
      const rect = node.getBoundingClientRect()
      const width = rect.width || window.innerWidth
      builderSplitWidthRef.current = width
      setBuilderSplitWidth((current) => (current != null && Math.abs(current - width) < 0.5 ? current : width))
    }

    measure()
    const observer = typeof ResizeObserver !== 'undefined' ? new ResizeObserver(measure) : null
    observer?.observe(node)
    window.addEventListener('resize', measure)
    return () => {
      observer?.disconnect()
      window.removeEventListener('resize', measure)
      builderSplitWidthRef.current = null
      setBuilderSplitWidth(null)
    }
  }, [route.name])

  const handleBuilderChatDrag = useCallback((delta: number): void => {
    const splitWidth = builderSplitWidthRef.current
      ?? builderSplitRef.current?.getBoundingClientRect().width
      ?? window.innerWidth
    const state = useUIStore.getState()
    const currentWidth = resolveAgentBuilderChatWidth(
      state.agentBuilderChatWidth,
      state.agentBuilderChatWidthRatio,
      splitWidth
    )
    const maxWidth = resolveMaxAgentBuilderChatWidth(splitWidth)
    const nextWidth = Math.min(maxWidth, Math.max(AGENT_BUILDER_CHAT_MIN_WIDTH, currentWidth - delta))
    state.setAgentBuilderChatWidth(nextWidth, splitWidth)
  }, [])

  const syncBuilderDraftNow = useCallback((rawContent?: string): Promise<void> => {
    if (builderConversationStatus !== 'ready') {
      return draftSyncPromiseRef.current ?? Promise.resolve()
    }
    const next = rawContent ?? latestBuilderDraftRef.current
    if (next === lastSyncedBuilderDraftRef.current) {
      return draftSyncPromiseRef.current ?? Promise.resolve()
    }

    const previous = draftSyncPromiseRef.current
    const promise = (previous ?? Promise.resolve())
      .catch(() => undefined)
      .then(() => syncBuilderDraftRequest(next))
      .then(() => {
        lastSyncedBuilderDraftRef.current = next
      })
      .finally(() => {
        if (draftSyncPromiseRef.current === promise) {
          draftSyncPromiseRef.current = null
        }
      })
    draftSyncPromiseRef.current = promise
    return promise
  }, [builderConversationStatus, syncBuilderDraftRequest])

  const flushBuilderDraft = useCallback(async (): Promise<void> => {
    if (draftSyncTimerRef.current !== null) {
      window.clearTimeout(draftSyncTimerRef.current)
      draftSyncTimerRef.current = null
    }
    const current = route.name === 'builder' ? toMarkdown(route.draft) : latestBuilderDraftRef.current
    latestBuilderDraftRef.current = current
    await syncBuilderDraftNow(current)
    if (draftSyncPromiseRef.current) {
      await draftSyncPromiseRef.current
    }
  }, [route, syncBuilderDraftNow])

  useEffect(() => {
    if (route.name !== 'builder') {
      lastSyncedBuilderDraftRef.current = null
      latestBuilderDraftRef.current = ''
      if (draftSyncTimerRef.current !== null) {
        window.clearTimeout(draftSyncTimerRef.current)
        draftSyncTimerRef.current = null
      }
      return undefined
    }
    const md = toMarkdown(route.draft)
    latestBuilderDraftRef.current = md
    if (builderConversationStatus !== 'ready' || builderTurnStatus !== 'idle') return undefined
    if (md === lastSyncedBuilderDraftRef.current) return undefined

    if (draftSyncTimerRef.current !== null) {
      window.clearTimeout(draftSyncTimerRef.current)
    }
    draftSyncTimerRef.current = window.setTimeout(() => {
      draftSyncTimerRef.current = null
      void syncBuilderDraftNow(md).catch(() => undefined)
    }, 400)

    return () => {
      if (draftSyncTimerRef.current !== null) {
        window.clearTimeout(draftSyncTimerRef.current)
        draftSyncTimerRef.current = null
      }
    }
  }, [route, builderConversationStatus, builderTurnStatus, syncBuilderDraftNow])

  const loadProfiles = useCallback(async (): Promise<void> => {
    try {
      const res = await rpc<{ profiles?: ProfileEntry[] }>('agent/profiles/list', { includeInvalid: true })
      const profiles = Array.isArray(res.profiles) ? res.profiles : []
      setProfiles(profiles)
      // Share the freshly-fetched stored avatars so the composer/welcome mascots
      // (which only know a profile id) resolve the same avatar without a refetch.
      useAgentProfileAvatarStore.getState().setFromList(useConversationStore.getState().workspacePath, profiles)
      setStatus('ready')
      setLoadError(null)
    } catch (err) {
      setProfiles([])
      setStatus('error')
      setLoadError(errorText(err))
    }
  }, [])

  useEffect(() => {
    void loadProfiles()
  }, [loadProfiles])

  // Load the selector catalogs once. Failures degrade gracefully (free-text fallback).
  useEffect(() => {
    void rpc<{ tools?: ToolInfo[] }>('tool/list', {})
      .then((res) => setToolCatalog(Array.isArray(res.tools) ? res.tools : []))
      .catch(() => setToolCatalog([]))
    void rpc<{ skills?: SkillInfo[] }>('skills/list', { includeUnavailable: true })
      .then((res) => setSkillCatalog(Array.isArray(res.skills) ? res.skills : []))
      .catch(() => setSkillCatalog([]))
    void rpc<{ servers?: { name: string }[] }>('mcp/list', {})
      .then((res) => setMcpServers(Array.isArray(res.servers) ? res.servers.map((s) => s.name).filter(Boolean) : []))
      .catch(() => setMcpServers([]))
  }, [])

  const setDraft: Dispatch<SetStateAction<ProfileDraft>> = useCallback((updater) => {
    setRoute((r) => {
      if (r.name !== 'builder') return r
      const next = typeof updater === 'function' ? (updater as (d: ProfileDraft) => ProfileDraft)(r.draft) : updater
      return { ...r, draft: next }
    })
  }, [])

  const openProfile = useCallback(async (entry: ProfileEntry): Promise<void> => {
    try {
      const res = await rpc<{ profile?: ProfileEntry }>('agent/profiles/read', { id: entry.id, source: entry.source })
      const draft = parseProfile(res.profile?.rawContent)
      const readOnly = entry.readOnly === true || entry.source === 'builtIn' || entry.source === 'plugin'
      if (!draft.name) draft.name = entry.id
      // Seed the auto-save baseline so opening a profile doesn't immediately re-save it (the
      // Markdown + target must match the opened state). Read-only sources have no saved id yet —
      // the first real edit forks a new editable copy under the save target.
      lastSavedMdRef.current = toMarkdown(draft)
      lastSavedIdRef.current = readOnly ? null : entry.id
      lastSavedSourceRef.current = writableSource(entry.source)
      setViewMode('edit')
      setAutoSaveState('idle')
      setRoute({
        name: 'builder',
        draft,
        id: entry.id,
        source: entry.source,
        readOnly,
        isNew: readOnly,
        saveTarget: writableSource(entry.source),
        saving: false,
        avatar: draft.avatar ?? avatarForEntry(res.profile ?? entry),
        // An existing writable profile is already "created"; a read-only template is an uncreated copy.
        created: !readOnly,
        updatedAt: readOnly ? null : (res.profile?.updatedAt ?? entry.updatedAt ?? null)
      })
      setBuilderSession({
        targetId: entry.id,
        targetSource: entry.source
      })
    } catch (err) {
      showToast({ message: `Could not open: ${errorText(err)}`, type: 'error' })
    }
  }, [])

  const startDraft = useCallback((
    draft: ProfileDraft,
    avatar: AvatarSpec,
    options: { targetId?: string; targetSource?: string } = {}
  ): void => {
    lastSavedMdRef.current = null
    lastSavedIdRef.current = null
    lastSavedSourceRef.current = null
    lastSyncedBuilderDraftRef.current = null
    setViewMode('edit')
    setAutoSaveState('idle')
    const nextDraft = draftWithAvatar(draft, avatar)
    setRoute({ name: 'builder', draft: nextDraft, id: null, source: null, readOnly: false, isNew: true, saveTarget: 'workspace', saving: false, avatar, created: false, updatedAt: null })
    setBuilderSession({
      targetId: options.targetId?.trim() || nextDraft.name.trim() || newDraftTargetId(),
      targetSource: options.targetSource?.trim() || 'workspace'
    })
  }, [])

  const newBlank = useCallback((): void => {
    startDraft(createEmptyDraft(), randomAvatar(), {
      targetId: newDraftTargetId(),
      targetSource: 'workspace'
    })
  }, [startDraft])

  const prefillIntroComposer = useCallback((text: string): void => {
    setIntroPrefillRequest({ id: ++introPrefillSeqRef.current, text })
  }, [])

  const prefillBuilderComposer = useCallback((text: string): void => {
    setBuilderPrefillRequest({ id: ++builderPrefillSeqRef.current, text })
  }, [])

  const startBuilderChatWithDraft = useCallback(async ({
    draft,
    targetId,
    targetSource,
    inputParts,
    config
  }: {
    draft: ProfileDraft
    targetId: string
    targetSource: string
    inputParts: InputComposerSubmitPayload['inputParts']
    config: ThreadConfigurationWire
  }): Promise<void> => {
    const md = toMarkdown(draft)
    latestBuilderDraftRef.current = md
    await startBuilderConversation({
      targetId,
      targetSource,
      initialDraftMarkdown: md,
      inputParts,
      config
    })
    lastSyncedBuilderDraftRef.current = md
  }, [startBuilderConversation])

  const startBlankFromIntro = useCallback(async (
    payload: InputComposerSubmitPayload,
    config: ThreadConfigurationWire
  ): Promise<void> => {
    const avatar = randomAvatar()
    const draft = draftWithAvatar(createEmptyDraft(), avatar)
    const targetId = newDraftTargetId()
    const targetSource = 'workspace'
    startDraft(draft, avatar, { targetId, targetSource })
    await startBuilderChatWithDraft({
      draft,
      targetId,
      targetSource,
      inputParts: payload.inputParts,
      config
    })
  }, [startDraft, startBuilderChatWithDraft])

  const startBuilderChatFromRoute = useCallback(async (
    payload: InputComposerSubmitPayload,
    config: ThreadConfigurationWire
  ): Promise<void> => {
    if (route.name !== 'builder') return
    const routeName = route.draft.name.trim()
    const targetId = builderSession?.targetId ?? route.id ?? (routeName || newDraftTargetId())
    const targetSource = builderSession?.targetSource ?? route.source ?? route.saveTarget ?? 'workspace'
    await startBuilderChatWithDraft({
      draft: route.draft,
      targetId,
      targetSource,
      inputParts: payload.inputParts,
      config
    })
  }, [builderSession, route, startBuilderChatWithDraft])

  const fromTemplate = useCallback(async (entry: ProfileEntry): Promise<void> => {
    try {
      const res = await rpc<{ profile?: ProfileEntry }>('agent/profiles/read', { id: entry.id, source: entry.source })
      const draft = parseProfile(res.profile?.rawContent)
      if (!draft.name) draft.name = entry.id
      startDraft(draft, draft.avatar ?? avatarForEntry(res.profile ?? entry), {
        targetId: entry.id,
        targetSource: entry.source
      })
    } catch (err) {
      showToast({ message: `Could not load template: ${errorText(err)}`, type: 'error' })
    }
  }, [startDraft])

  // Auto-save: persist the draft (debounced) — but only AFTER it has been created (existing profile,
  // or post-"Create"). A new/uncreated agent never auto-saves; it waits for the explicit Create button.
  // A name or save-target change moves the file. The Markdown baseline guards against re-saving
  // unchanged content (which would otherwise loop, since each save updates the route).
  useEffect(() => {
    if (route.name !== 'builder') {
      setAutoSaveState('idle')
      return undefined
    }
    if (!route.created) return undefined
    const name = route.draft.name.trim()
    const target = route.saveTarget
    const md = toMarkdown(route.draft)
    if (!name) return undefined
    if (md === lastSavedMdRef.current && target === lastSavedSourceRef.current) return undefined
    const timer = window.setTimeout(() => {
      void (async () => {
        setAutoSaveState('saving')
        try {
          const prevId = lastSavedIdRef.current
          const prevSource = lastSavedSourceRef.current
          if (prevId && (prevId !== name || prevSource !== target)) {
            await rpc('agent/profiles/remove', { id: prevId, source: prevSource }).catch(() => undefined)
          }
          await rpc('agent/profiles/upsert', { id: name, source: target, rawContent: md })
          lastSavedMdRef.current = md
          lastSavedIdRef.current = name
          lastSavedSourceRef.current = target
          setRoute((r) => (r.name === 'builder' ? { ...r, id: name, source: target, readOnly: false, isNew: false, updatedAt: new Date().toISOString() } : r))
          setAutoSaveState('saved')
          void loadProfiles()
        } catch {
          setAutoSaveState('error')
        }
      })()
    }, 900)
    return () => window.clearTimeout(timer)
  }, [route, loadProfiles])

  // Explicit creation for a new/uncreated agent. The save target (user vs workspace) is chosen in the
  // Create dialog rather than a persistent toggle. After this, auto-save takes over to that target.
  const createProfile = useCallback(async (target: SaveTarget): Promise<void> => {
    if (route.name !== 'builder') return
    const name = route.draft.name.trim()
    if (!name) {
      showToast({ message: 'Give the agent a name first.', type: 'error' })
      return
    }
    const md = toMarkdown(route.draft)
    setAutoSaveState('saving')
    try {
      await rpc('agent/profiles/upsert', { id: name, source: target, rawContent: md })
      lastSavedMdRef.current = md
      lastSavedIdRef.current = name
      lastSavedSourceRef.current = target
      setRoute((r) => (r.name === 'builder'
        ? { ...r, id: name, source: target, saveTarget: target, readOnly: false, isNew: false, created: true, updatedAt: new Date().toISOString() }
        : r))
      setAutoSaveState('saved')
      showToast({ message: `Created "${name}".`, type: 'success' })
      void loadProfiles()
    } catch (err) {
      setAutoSaveState('error')
      showToast({ message: `Create failed: ${errorText(err)}`, type: 'error' })
    }
  }, [route, loadProfiles])

  const removeProfile = useCallback(async (): Promise<void> => {
    if (route.name !== 'builder' || !route.id || route.readOnly) return
    const { id, source } = route
    try {
      await rpc('agent/profiles/remove', { id, source })
      showToast({ message: `Removed "${id}".`, type: 'info' })
      await loadProfiles()
      setBuilderSession(null)
      setRoute({ name: 'gallery' })
    } catch (err) {
      showToast({ message: `Remove failed: ${errorText(err)}`, type: 'error' })
    }
  }, [route, loadProfiles])

  const leaveBuilder = useCallback((): void => {
    setBuilderSession(null)
    setHighlight(null)
    setEditingField(null)
    setRoute({ name: 'gallery' })
  }, [])

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase()
    return profiles.filter((p) => {
      if (p.shadowed) return false
      if (!q) return true
      return p.id.toLowerCase().includes(q) || (p.description || '').toLowerCase().includes(q)
    })
  }, [profiles, query])

  if (route.name === 'builder') {
    const activeEditingField = editingField ?? highlight?.field ?? null
    const agentDriving = builderConversation.threadId !== null && (builderTurnStatus === 'running' || editingField !== null)
    const effectiveBuilderChatWidth = resolveAgentBuilderChatWidth(
      agentBuilderChatWidth,
      agentBuilderChatWidthRatio,
      builderSplitWidth ?? window.innerWidth
    )
    const builderChatpaneStyle = {
      width: effectiveBuilderChatWidth,
      flexBasis: effectiveBuilderChatWidth,
      minWidth: AGENT_BUILDER_CHAT_MIN_WIDTH,
      '--agent-builder-chat-divider-border': builderChatDividerActive
        ? 'var(--resize-divider-active)'
        : 'var(--border-default)'
    } as CSSProperties
    return (
      <div ref={builderSplitRef} className={`agent-builder-split is-chat${builderChatResizing ? ' is-resizing' : ''}`}>
        <div className={`agent-builder-split-main${agentDriving ? ' is-agent-driving' : ''}`}>
          <BuilderView
            route={route}
            setRoute={setRoute}
            setDraft={setDraft}
            toolCatalog={toolCatalog}
            skillCatalog={skillCatalog}
            mcpServers={mcpServers}
            viewMode={viewMode}
            setViewMode={setViewMode}
            autoSaveState={autoSaveState}
            editingField={activeEditingField}
            agentDriving={agentDriving}
            onBack={leaveBuilder}
            onDelete={removeProfile}
            onCreate={() => setCreateDialogOpen(true)}
          />
        </div>
        <aside className="agent-builder-chatpane" style={builderChatpaneStyle}>
          {builderConversationStatus === 'starting' ? (
            <div className="agent-builder-chat-loading">Starting builder…</div>
          ) : builderConversationStatus === 'ready' ? (
            <ConversationPanel
              workspacePath={workspacePath}
              minimalComposer
              mascotAvatar={route.avatar}
              variant="agentBuilder"
              onBeforeSend={flushBuilderDraft}
            />
          ) : (
            <DetachedAgentBuilderChat
              workspacePath={workspacePath}
              mascotAvatar={route.avatar}
              prefillRequest={builderPrefillRequest}
              error={builderConversationStatus === 'error' ? builderConversationError : null}
              onPrefill={prefillBuilderComposer}
              onSubmit={startBuilderChatFromRoute}
            />
          )}
        </aside>
        <DragHandle
          className="drag-handle--agent-builder-chat"
          onDrag={handleBuilderChatDrag}
          onActiveChange={setBuilderChatDividerActive}
          onDragStateChange={setBuilderChatResizing}
          style={{
            position: 'absolute',
            top: 0,
            bottom: 0,
            right: `${effectiveBuilderChatWidth - 4}px`
          }}
        />
        {createDialogOpen && (
          <AgentSaveTargetDialog
            name={route.draft.name.trim()}
            onChoose={(target) => {
              setCreateDialogOpen(false)
              void createProfile(target)
            }}
            onCancel={() => setCreateDialogOpen(false)}
          />
        )}
      </div>
    )
  }

  if (route.name === 'intro') {
    const templates = profiles.filter((p) => sectionFor(p.source) === 'builtIn')
    return (
      <div className="agent-builder">
        <div className="agent-builder-intro-top">
          <button type="button" className="agent-builder-iconbtn" title="Back" onClick={() => setRoute({ name: 'gallery' })}>
            <ArrowLeft size={18} />
          </button>
          <button type="button" className="agent-builder-intro-blank" onClick={() => newBlank()}>
            Start blank
          </button>
        </div>
        <div className="agent-builder-intro">
          <div className="agent-builder-intro-center">
            <h1 className="agent-builder-intro-title">Build a new agent</h1>
            <IntroBuilderComposer
              workspacePath={workspacePath}
              prefillRequest={introPrefillRequest}
              onSubmit={startBlankFromIntro}
            />
            <div className="agent-builder-intro-sugs">
              {SUGGESTIONS.map((s) => {
                const Icon = s.icon
                return (
                  <button key={s.title} type="button" className="agent-builder-intro-sug" onClick={() => prefillIntroComposer(s.prompt)}>
                    <span className="agent-builder-intro-sug-ic" aria-hidden><Icon size={16} /></span>
                    <span className="agent-builder-intro-sug-t">{s.title}</span>
                    <span className="agent-builder-intro-sug-d">{s.desc}</span>
                  </button>
                )
              })}
            </div>
          </div>
          {templates.length > 0 && (
            <TemplateDeck templates={templates} onPick={(p) => void fromTemplate(p)} />
          )}
        </div>
      </div>
    )
  }

  // ── Gallery (shares the Channels / Automations / Plugins catalog surface) ──
  const sections: { key: Filter; title: string }[] = [
    { key: 'builtIn', title: 'Built-in templates' },
    { key: 'user', title: 'My agents' },
    { key: 'workspace', title: 'This workspace' }
  ]
  const visibleSections = sections
    .map((sec) => ({ sec, items: filtered.filter((p) => sectionFor(p.source) === sec.key) }))
    .filter((group) => group.items.length > 0)

  return (
    <div style={catalogStyles.page}>
      <CatalogTopBar
        actions={(
          <>
            <Button variant="primary" size="toolbar" onClick={() => setRoute({ name: 'intro' })} iconLeft={<Plus size={14} aria-hidden />}>
              New agent
            </Button>
            <IconButton
              label="More actions"
              tooltipLabel="More actions"
              tooltipPlacement="bottom"
              size={CATALOG_TOOLBAR_CONTROL_SIZE}
              radius={CATALOG_TOOLBAR_CONTROL_RADIUS}
              aria-haspopup="menu"
              aria-expanded={menuPos != null}
              onClick={(e) => setMenuPos({ x: e.clientX, y: e.clientY })}
              icon={<MoreHorizontal size={15} aria-hidden />}
            />
          </>
        )}
      />
      <header style={catalogStyles.browseHeader}>
        <h1 style={catalogStyles.heroTitle}>Build your agents with DotCraft</h1>
        <div style={catalogStyles.searchRow}>
          <CatalogSearchBox value={query} placeholder="Search agents" onChange={setQuery} />
        </div>
      </header>
      <main style={catalogStyles.browseMain}>
        {status === 'loading' ? (
          <p style={catalogStyles.emptyText}>Loading agents…</p>
        ) : status === 'error' ? (
          <p style={catalogStyles.emptyText}>{loadError || 'Could not load agents.'}</p>
        ) : visibleSections.length === 0 ? (
          <p style={catalogStyles.emptyText}>No agents yet. Use “New agent” to build one.</p>
        ) : (
          visibleSections.map(({ sec, items }) => (
            <CatalogSection key={sec.key} title={sec.title}>
              <CatalogCompactGrid>
                {items.map((p) => (
                  <CatalogHoverButton key={`${p.source}:${p.id}`} type="button" baseStyle={catalogStyles.compactItem} onClick={() => void openProfile(p)}>
                    <span style={galleryAvatar}>
                      <RobotAvatar spec={avatarForEntry(p)} size={36} />
                    </span>
                    <span style={galleryText}>
                      <span style={catalogStyles.rowTitleLine}><strong style={catalogStyles.rowTitle}>{p.id}</strong></span>
                      <span style={catalogStyles.rowDesc}>{p.description || ''}</span>
                    </span>
                    {p.valid === false && <span style={catalogStyles.statusIcon}>Issues</span>}
                  </CatalogHoverButton>
                ))}
              </CatalogCompactGrid>
            </CatalogSection>
          ))
        )}
      </main>
      {menuPos && (
        <ContextMenu
          position={menuPos}
          onClose={() => setMenuPos(null)}
          items={[{ label: 'Refresh', icon: <RefreshCw size={14} aria-hidden />, onClick: () => void loadProfiles() }]}
        />
      )}
    </div>
  )
}

// ── "Build a new agent" input ──
// Uses the real conversation composer, but with a detached submit handler because the hidden builder
// thread is created only after the user sends the first intent.
function IntroBuilderComposer({
  workspacePath,
  prefillRequest,
  onSubmit
}: {
  workspacePath: string
  prefillRequest: { id: number; text: string } | null
  onSubmit: (payload: InputComposerSubmitPayload, config: ThreadConfigurationWire) => Promise<void> | void
}): JSX.Element {
  const modelControls = useComposerModelControls({
    workspacePath,
    mode: 'detached'
  })

  return (
    <div className="agent-builder-introcomposer">
      <InputComposer
        threadId="agent-builder-intro"
        transientVoiceOrigin
        workspacePath={workspacePath}
        minimalChrome
        mascotAvatar={AGENT_BUILDER_AVATAR}
        variant="agentBuilder"
        placeholder="Describe the agent you want…"
        prefillRequest={prefillRequest}
        submitOverride={(payload) => onSubmit(payload, modelControls.threadStartConfig)}
        modelName={modelControls.modelName}
        modelOptions={modelControls.modelOptions}
        modelCatalog={modelControls.modelCatalog}
        reasoningValue={modelControls.reasoningValue}
        modelLoading={modelControls.modelLoading}
        modelDisabled={modelControls.modelDisabled}
        modelListUnsupportedEndpoint={modelControls.modelListUnsupportedEndpoint}
        modelCatalogError={modelControls.modelCatalogError}
        modelCatalogErrorMessage={modelControls.modelCatalogErrorMessage}
        onModelChange={modelControls.onModelChange}
        onReasoningChange={modelControls.onReasoningChange}
        onModelCatalogRetry={modelControls.onModelCatalogRetry}
        contextMode={modelControls.contextMode}
        contextSupportsMax={modelControls.contextSupportsMax}
        contextDegraded={modelControls.contextDegraded}
        contextConfiguredWindow={modelControls.contextConfiguredWindow}
        onContextModeChange={modelControls.onContextModeChange}
        dockPadding={0}
      />
    </div>
  )
}

function DetachedAgentBuilderChat({
  workspacePath,
  mascotAvatar,
  prefillRequest,
  error,
  onPrefill,
  onSubmit
}: {
  workspacePath: string
  mascotAvatar: AvatarSpec
  prefillRequest: { id: number; text: string } | null
  error: string | null
  onPrefill: (prompt: string) => void
  onSubmit: (payload: InputComposerSubmitPayload, config: ThreadConfigurationWire) => Promise<void> | void
}): JSX.Element {
  const modelControls = useComposerModelControls({
    workspacePath,
    mode: 'detached'
  })

  return (
    <div className="agent-builder-detached-chat">
      {error && (
        <div className="agent-builder-chat-error" role="alert">
          Couldn’t start the builder chat: {error}
        </div>
      )}
      <AgentBuilderChatEmptyState onPick={onPrefill} />
      <InputComposer
        threadId="agent-builder-detached"
        transientVoiceOrigin
        workspacePath={workspacePath}
        minimalChrome
        mascotAvatar={mascotAvatar}
        variant="agentBuilder"
        prefillRequest={prefillRequest}
        submitOverride={(payload) => onSubmit(payload, modelControls.threadStartConfig)}
        modelName={modelControls.modelName}
        modelOptions={modelControls.modelOptions}
        modelCatalog={modelControls.modelCatalog}
        reasoningValue={modelControls.reasoningValue}
        modelLoading={modelControls.modelLoading}
        modelDisabled={modelControls.modelDisabled}
        modelListUnsupportedEndpoint={modelControls.modelListUnsupportedEndpoint}
        modelCatalogError={modelControls.modelCatalogError}
        modelCatalogErrorMessage={modelControls.modelCatalogErrorMessage}
        onModelChange={modelControls.onModelChange}
        onReasoningChange={modelControls.onReasoningChange}
        onModelCatalogRetry={modelControls.onModelCatalogRetry}
        contextMode={modelControls.contextMode}
        contextSupportsMax={modelControls.contextSupportsMax}
        contextDegraded={modelControls.contextDegraded}
        contextConfiguredWindow={modelControls.contextConfiguredWindow}
        onContextModeChange={modelControls.onContextModeChange}
      />
    </div>
  )
}

// ── Built-in template deck ──
// Fanned, bottom-clipped cards. Hover selection is driven by pointer X over the (stable) container
// rect, not per-card :hover — lifting a card vertically never changes which card is selected, so the
// overlap boundaries no longer cause the active card to oscillate.
function TemplateDeck({ templates, onPick }: { templates: ProfileEntry[]; onPick: (entry: ProfileEntry) => void }): JSX.Element {
  const ref = useRef<HTMLDivElement>(null)
  const [active, setActive] = useState<number | null>(null)
  const cards = templates.slice(0, 5)

  const trackPointer = (clientX: number): void => {
    const el = ref.current
    if (!el) return
    const rect = el.getBoundingClientRect()
    if (rect.width <= 0) return
    const ratio = (clientX - rect.left) / rect.width
    setActive(Math.max(0, Math.min(cards.length - 1, Math.floor(ratio * cards.length))))
  }

  return (
    <div
      ref={ref}
      className="agent-builder-intro-deck"
      onMouseMove={(e) => trackPointer(e.clientX)}
      onMouseLeave={() => setActive(null)}
    >
      {cards.map((p, i) => {
        const f = FAN[i % FAN.length]
        const isActive = active === i
        return (
          <button
            key={p.id}
            type="button"
            className={`agent-builder-deckcard${isActive ? ' is-active' : ''}`}
            style={{
              transform: isActive
                ? 'translateY(-84px) rotate(0deg) scale(1.04)'
                : `translateY(${f.y}px) rotate(${f.rot}deg)`,
              zIndex: isActive ? 40 : 5 - Math.abs((i % 5) - 2)
            }}
            onClick={() => onPick(p)}
          >
            <span className="agent-builder-deckcard-head">
              <RobotAvatar spec={avatarForEntry(p)} size={40} />
              <span className="agent-builder-deckcard-name">{p.id}</span>
            </span>
            <span className="agent-builder-deckcard-desc">{p.description || ''}</span>
          </button>
        )
      })}
    </div>
  )
}

// ── Builder (document editor) ──
interface BuilderViewProps {
  route: Extract<Route, { name: 'builder' }>
  setRoute: Dispatch<SetStateAction<Route>>
  setDraft: Dispatch<SetStateAction<ProfileDraft>>
  toolCatalog: ToolInfo[]
  skillCatalog: SkillInfo[]
  mcpServers: string[]
  viewMode: 'edit' | 'preview'
  setViewMode: Dispatch<SetStateAction<'edit' | 'preview'>>
  autoSaveState: 'idle' | 'saving' | 'saved' | 'error'
  editingField: BuilderField | null
  agentDriving: boolean
  onBack: () => void
  onDelete: () => void
  onCreate: () => void
}

function BuilderView({ route, setRoute, setDraft, toolCatalog, skillCatalog, mcpServers, viewMode, setViewMode, autoSaveState, editingField, agentDriving, onBack, onDelete, onCreate }: BuilderViewProps): JSX.Element {
  const locale = useLocale()
  const t = useT()
  const { draft, avatar } = route
  const nameMissing = !draft.name.trim()
  const preview = viewMode === 'preview'
  const [menuOpen, setMenuOpen] = useState(false)
  const menuRef = useRef<HTMLDivElement>(null)

  const reroll = (): void => setRoute((r) => {
    if (r.name !== 'builder') return r
    const avatar = randomAvatar(r.avatar)
    return { ...r, avatar, draft: { ...r.draft, avatar } }
  })

  useEffect(() => {
    if (!menuOpen) return undefined
    function onDown(event: MouseEvent): void {
      if (menuRef.current && !menuRef.current.contains(event.target as Node)) setMenuOpen(false)
    }
    document.addEventListener('mousedown', onDown, true)
    return () => document.removeEventListener('mousedown', onDown, true)
  }, [menuOpen])

  const providers = useProvidersStore((s) => s.providers)
  const models = useModelCatalogStore((s) => s.models)
  const modelCatalogStatus = useModelCatalogStore((s) => s.status)
  const modelCatalogError = useModelCatalogStore((s) => s.errorMessage)
  const effectiveCatalogProviderId = useModelCatalogStore((s) => s.providerId)
  const [workspaceDefaultPreference, setWorkspaceDefaultPreference] = useState<AgentProviderPreference | null>(null)
  const [workspaceProviderPreferences, setWorkspaceProviderPreferences] = useState<ProviderPreferences>({})

  useEffect(() => {
    void useProvidersStore.getState().reload()
    const getCore = window.api.workspaceConfig?.getCore
    if (typeof getCore !== 'function') return
    void getCore().then((core) => {
      const providerId = (core.workspace.providerId ?? core.userDefaults.providerId ?? '').trim()
      const preferences = mergeProviderPreferences(
        core.userDefaults.providerPreferences,
        core.workspace.providerPreferences
      )
      const preference = findProviderPreference(preferences, providerId)
      setWorkspaceProviderPreferences(preferences)
      setWorkspaceDefaultPreference(preference ? toAgentProviderPreference(providerId, preference) : null)
    }).catch(() => undefined)
  }, [])

  const selectedProviderId = draft.providerPreference?.providerId ?? null
  useEffect(() => {
    void useModelCatalogStore.getState().loadIfNeeded(false, selectedProviderId)
  }, [selectedProviderId])

  const selectedModel = useMemo(
    () => models.find((item) => item.id === draft.providerPreference?.model) ?? null,
    [draft.providerPreference?.model, models]
  )
  const pickerPreference = useMemo<ModelPreference | null>(() => {
    const preference = draft.providerPreference
    if (!preference) return null
    return {
      model: preference.model,
      reasoning: {
        enabled: preference.reasoning.enabled,
        effort: preference.reasoning.effort,
        output: selectedModel?.reasoning?.defaultOutput ?? 'full'
      },
      speed: preference.speed,
      contextWindow: { mode: preference.contextWindow.mode }
    }
  }, [draft.providerPreference, selectedModel])
  const providerSelectOptions = useMemo(() => {
    const options = providers.map((provider) => ({ value: provider.id, label: provider.displayName }))
    const current = draft.providerPreference?.providerId
    if (current && !providers.some((provider) => provider.id.toLowerCase() === current.toLowerCase())) {
      options.push({ value: current, label: current })
    }
    return options
  }, [draft.providerPreference?.providerId, providers])
  const seedProviderPreference = useCallback((): AgentProviderPreference | null => {
    if (workspaceDefaultPreference) return structuredClone(workspaceDefaultPreference)
    const providerId = effectiveCatalogProviderId ?? providers[0]?.id
    const model = models[0]
    if (!providerId || !model) return null
    return toAgentProviderPreference(
      providerId,
      createCatalogDefaultPreference(model, model.id)
    )
  }, [effectiveCatalogProviderId, models, providers, workspaceDefaultPreference])

  const updateProviderPreference = useCallback((
    update: (preference: AgentProviderPreference) => AgentProviderPreference
  ): void => {
    setDraft((current) => current.providerPreference
      ? { ...current, providerPreference: update(current.providerPreference) }
      : current)
  }, [setDraft])

  const selectProvider = useCallback((providerId: string): void => {
    updateProviderPreference((preference) => ({ ...preference, providerId }))
    void useModelCatalogStore.getState().loadIfNeeded(true, providerId).then(() => {
      const catalog = useModelCatalogStore.getState()
      const configured = findProviderPreference(workspaceProviderPreferences, providerId)
      const seeded = configured
        ?? createCatalogDefaultPreference(catalog.models[0], catalog.models[0]?.id ?? '')
      if (!seeded.model) return
      setDraft((current) => {
        if (current.providerPreference?.providerId !== providerId) return current
        return {
          ...current,
          providerPreference: toAgentProviderPreference(providerId, seeded)
        }
      })
    })
  }, [setDraft, updateProviderPreference, workspaceProviderPreferences])

  const inheritSummary = workspaceDefaultPreference
    ? `${workspaceDefaultPreference.providerId} · ${workspaceDefaultPreference.model}`
    : t('agentBuilder.model.inheritDescription')
  const pinnedProviderUnavailable = draft.providerPreference != null
    && providers.length > 0
    && !providers.some((provider) => provider.id.toLowerCase() === draft.providerPreference!.providerId.toLowerCase())
  return (
    <div className="agent-builder">
      <header className="agent-builder-edit-head">
        <div className="agent-builder-edit-left">
          <button type="button" className="agent-builder-iconbtn" title="Back" onClick={onBack}>
            <ArrowLeft size={18} />
          </button>
          <span className="agent-builder-headavatar">
            <RobotAvatar spec={avatar} size={26} />
          </span>
          <span className="agent-builder-headname">{draft.name || 'Untitled agent'}</span>
        </div>
        <div className="agent-builder-edit-right">
          {route.created && (
            <span className={`agent-builder-autosave${autoSaveState === 'error' ? ' is-error' : ''}`}>
              {autoSaveState === 'saving'
                ? 'Saving…'
                : autoSaveState === 'error'
                  ? 'Save failed'
                  : route.updatedAt
                    ? `Updated ${formatRelativeTime(route.updatedAt, new Date(), locale)}`
                    : 'Saved'}
            </span>
          )}
          <button type="button" className="agent-builder-btn-secondary" onClick={() => setViewMode(preview ? 'edit' : 'preview')}>
            {preview ? <><Pencil size={15} /> Edit</> : <><Eye size={15} /> Preview</>}
          </button>
          {route.created ? (
            <div className="agent-builder-menu" ref={menuRef}>
              <button type="button" className="agent-builder-iconbtn" aria-label="More actions" aria-expanded={menuOpen} onClick={() => setMenuOpen((v) => !v)}>
                <MoreHorizontal size={18} />
              </button>
              {menuOpen && (
                <div className="agent-builder-menu-pop" role="menu">
                  <button type="button" className="agent-builder-menu-item is-danger" role="menuitem" onClick={() => { setMenuOpen(false); onDelete() }}>
                    <Trash2 size={15} /> Delete
                  </button>
                </div>
              )}
            </div>
          ) : (
            <button type="button" className="agent-builder-btn" disabled={nameMissing} onClick={onCreate}>
              <Plus size={15} /> Create
            </button>
          )}
        </div>
      </header>

      <div className="agent-builder-scroll">
      <div className={`agent-builder-doc${agentDriving ? ' is-agent-driving' : ''}`}>
        {agentDriving && <div className="agent-builder-driving-veil" aria-hidden />}
        <div className="agent-builder-id">
          <span className="agent-builder-id-avatar">
            <RobotAvatar spec={avatar} size={64} animated />
            {!preview && (
              <button type="button" className="agent-builder-reroll" onClick={reroll} title="Re-roll avatar">
                <Shuffle size={12} />
              </button>
            )}
          </span>
          <div className="agent-builder-id-main">
            <FieldAnchor field="name" active={editingField === 'name'} className="agent-builder-field-anchor-name">
              <input
                className={`agent-builder-id-name${nameMissing ? ' is-empty' : ''}`}
                value={draft.name}
                placeholder="agent name"
                readOnly={preview}
                data-agent-builder-marker-target
                data-agent-builder-marker-offset-x={7}
                onChange={(e) => setDraft((d) => ({ ...d, name: e.target.value }))}
              />
            </FieldAnchor>
            <FieldAnchor field="description" active={editingField === 'description'} className="agent-builder-field-anchor-description">
              <input
                className="agent-builder-id-desc"
                value={draft.description}
                placeholder="One line about this agent…"
                readOnly={preview}
                data-agent-builder-marker-target
                data-agent-builder-marker-offset-x={7}
                onChange={(e) => setDraft((d) => ({ ...d, description: e.target.value }))}
              />
            </FieldAnchor>
            {route.readOnly && !preview && <div className="agent-builder-note">Built-in template — click Create to save it as a new agent in the selected source.</div>}
          </div>
        </div>

        <div className="agent-builder-divider" />

        <Section label="Tools">
          <FieldAnchor field="tools.policy" active={editingField === 'tools.policy'} className="agent-builder-tool-policy">
            <SettingsSelect<ToolPolicyMode>
              value={draft.tools.mode}
              ariaLabel={t('agentBuilder.tools.modeLabel')}
              onValueChange={(mode) => setDraft((d) => ({
                ...d,
                tools: { ...d.tools, mode, allow: [], deny: [] }
              }))}
              disabled={preview}
              valueProps={{ 'data-agent-builder-marker-target': '' }}
              options={[
                { value: 'all', label: t('agentBuilder.tools.mode.all') },
                { value: 'allowList', label: t('agentBuilder.tools.mode.allowList') },
                { value: 'denyList', label: t('agentBuilder.tools.mode.denyList') }
              ]}
            />
          </FieldAnchor>
          {draft.tools.mode === 'all' ? (
            <span className="agent-builder-pick-empty">{t('agentBuilder.tools.allHint')}</span>
          ) : (
            <CatalogField
              options={toolCatalog.map((tool) => ({ id: tool.name, label: tool.name, description: tool.description }))}
              selected={draft.tools.mode === 'allowList' ? draft.tools.allow : draft.tools.deny}
              onChange={(names) => setDraft((d) => ({
                ...d,
                tools: {
                  ...d.tools,
                  allow: d.tools.mode === 'allowList' ? names : [],
                  deny: d.tools.mode === 'denyList' ? names : []
                }
              }))}
              addLabel={t('agentBuilder.tools.add')}
              emptyHint={draft.tools.mode === 'allowList'
                ? t('agentBuilder.tools.allowEmpty')
                : undefined}
              kind="tool"
              field="tools.policy"
              editingField={editingField}
              readOnly={preview}
            />
          )}
        </Section>

        <Section label="MCP">
          <CatalogField
            options={mcpServers.map((name) => ({ id: name, label: name }))}
            selected={draft.mcp.servers}
            onChange={(servers) => setDraft((d) => ({ ...d, mcp: { ...d.mcp, servers } }))}
            addLabel="Add MCP server"
            kind="mcp"
            field="mcp.servers"
            editingField={editingField}
            readOnly={preview}
          />
        </Section>

        <Section label="Skills">
          <CatalogField
            options={skillCatalog.map((s) => ({ id: s.name, label: s.displayName || s.name, description: s.description }))}
            selected={draft.skills.preload}
            onChange={(preload) => setDraft((d) => ({ ...d, skills: { ...d.skills, preload } }))}
            addLabel="Add skill"
            kind="skill"
            field="skills.preload"
            editingField={editingField}
            readOnly={preview}
          />
        </Section>

        <Section label="Instructions">
          <InstructionsField
            value={draft.roleInstructions}
            preview={preview}
            editingField={editingField}
            onChange={(roleInstructions) => setDraft((d) => ({ ...d, roleInstructions }))}
          />
        </Section>

        <Section label="Details">
          <SettingsGroup>
            <SettingsRow
              label={t('agentBuilder.model.customSettings')}
              description={draft.providerPreference ? t('agentBuilder.model.customDescription') : inheritSummary}
              control={(
                <FieldAnchor field="providerPreference" active={editingField === 'providerPreference'} className="agent-builder-detail-toggle">
                  <PillSwitch
                    checked={draft.providerPreference != null}
                    onChange={(checked) => {
                      if (!checked) {
                        setDraft((current) => ({ ...current, providerPreference: null }))
                        return
                      }
                      const preference = seedProviderPreference()
                      if (preference) setDraft((current) => ({ ...current, providerPreference: preference }))
                    }}
                    disabled={preview || (!draft.providerPreference && !seedProviderPreference())}
                    aria-label={t('agentBuilder.model.customSettings')}
                  />
                </FieldAnchor>
              )}
            />
            {draft.providerPreference && (
              <div className="agent-builder-model-settings">
                {pinnedProviderUnavailable && (
                  <div className="agent-builder-model-warning" role="status">
                    {t('agentBuilder.model.providerUnavailable', { provider: draft.providerPreference.providerId })}
                  </div>
                )}
                <SettingsRow
                  label={t('agentBuilder.model.provider')}
                  controlMinWidth={200}
                  control={(
                    <SettingsSelect<string>
                      value={draft.providerPreference.providerId}
                      onValueChange={selectProvider}
                      disabled={preview}
                      style={{ width: '100%' }}
                      options={providerSelectOptions}
                    />
                  )}
                />
                {pickerPreference && (
                  <SettingsRow
                    label={t('agentBuilder.model.model')}
                    controlMinWidth={200}
                    control={(
                      <PreferenceModelPicker
                        preference={pickerPreference}
                        models={models}
                        loading={modelCatalogStatus === 'loading'}
                        disabled={preview}
                        errorMessage={modelCatalogError}
                        manualFallback={modelCatalogStatus !== 'loading' && models.length === 0}
                        onRetry={() => {
                          void useModelCatalogStore.getState().loadIfNeeded(
                            true,
                            draft.providerPreference?.providerId ?? null
                          )
                        }}
                        onChange={(preference) => {
                          updateProviderPreference((current) =>
                            toAgentProviderPreference(current.providerId, preference))
                        }}
                        inputId="agent-builder-provider-model"
                        inputAriaLabel={t('agentBuilder.model.model')}
                      />
                    )}
                  />
                )}
              </div>
            )}
            <SettingsRow
              label="Tool self-control"
              description="Whether the agent can manage its own available tools at runtime."
              controlMinWidth={200}
              control={(
                <FieldAnchor field="tools.agentControl" active={editingField === 'tools.agentControl'} className="agent-builder-detail-control">
                  <SettingsSelect<AgentControl>
                    value={draft.tools.agentControl}
                    onValueChange={(v) => setDraft((d) => ({ ...d, tools: { ...d.tools, agentControl: v } }))}
                    disabled={preview}
                    style={{ width: '100%' }}
                    valueProps={{ 'data-agent-builder-marker-target': '' }}
                    options={AGENT_CONTROL_OPTIONS}
                  />
                </FieldAnchor>
              )}
            />
            <SettingsRow
              label="Approval"
              controlMinWidth={200}
              control={(
                <FieldAnchor field="approval" active={editingField === 'approval'} className="agent-builder-detail-control">
                  <SettingsSelect<ApprovalPolicy>
                    value={draft.permissions.approvalPolicy}
                    onValueChange={(v) => setDraft((d) => ({ ...d, permissions: { ...d.permissions, approvalPolicy: v } }))}
                    disabled={preview}
                    style={{ width: '100%' }}
                    valueProps={{ 'data-agent-builder-marker-target': '' }}
                    options={APPROVAL_OPTIONS}
                  />
                </FieldAnchor>
              )}
            />
            <SettingsRow
              label="Approve outside workspace"
              description="Require approval before the agent touches paths outside this workspace."
              control={(
                <PillSwitch
                  checked={draft.permissions.requireApprovalOutsideWorkspace}
                  onChange={(checked) => setDraft((d) => ({ ...d, permissions: { ...d.permissions, requireApprovalOutsideWorkspace: checked } }))}
                  disabled={preview}
                  aria-label="Require approval outside workspace"
                />
              )}
            />
          </SettingsGroup>
        </Section>
      </div>
      </div>
    </div>
  )
}

function Section({ label, children }: { label: string; children: ReactNode }): JSX.Element {
  return (
    <section className="agent-builder-sec">
      <div className="agent-builder-sec-label">{label}</div>
      {children}
    </section>
  )
}

function FieldAnchor({
  field,
  active,
  className,
  children
}: {
  field: BuilderField
  active: boolean
  className?: string
  children: ReactNode
}): JSX.Element {
  const anchorRef = useRef<HTMLDivElement>(null)

  useLayoutEffect(() => {
    if (!active) return undefined
    const anchor = anchorRef.current
    if (!anchor) return undefined
    let frame = 0

    const measure = (): void => {
      const target = anchor.querySelector<HTMLElement>(MARKER_FALLBACK_SELECTOR)
      if (!target) return
      const anchorRect = anchor.getBoundingClientRect()
      const targetRect = target.getBoundingClientRect()
      const x = targetRect.left - anchorRect.left + markerContentEnd(target) + (markerOffset(target, MARKER_OFFSET_X_ATTR) ?? 0)
      const y = targetRect.top - anchorRect.top + (markerOffset(target, MARKER_OFFSET_Y_ATTR) ?? (targetRect.height / 2))
      anchor.style.setProperty('--agent-builder-marker-x', `${Math.round(x)}px`)
      anchor.style.setProperty('--agent-builder-marker-y', `${Math.round(y)}px`)

      // The label trails to the right of the cursor by default; flip it leftward
      // only when the content already reaches the field's right edge so it never
      // overflows the (clipped) editor pane.
      const marker = anchor.querySelector<HTMLElement>(MARKER_SELECTOR)
      if (marker) {
        marker.classList.toggle('is-marker-flipped', x + marker.offsetWidth + MARKER_FLIP_PAD > anchorRect.width)
      }
    }

    const scheduleMeasure = (): void => {
      if (frame) window.cancelAnimationFrame(frame)
      frame = window.requestAnimationFrame(measure)
    }

    scheduleMeasure()
    const target = anchor.querySelector<HTMLElement>(MARKER_FALLBACK_SELECTOR)
    const observer = typeof ResizeObserver !== 'undefined' ? new ResizeObserver(scheduleMeasure) : null
    observer?.observe(anchor)
    if (target) observer?.observe(target)
    window.addEventListener('resize', scheduleMeasure)
    window.addEventListener('scroll', scheduleMeasure, true)
    return () => {
      if (frame) window.cancelAnimationFrame(frame)
      observer?.disconnect()
      window.removeEventListener('resize', scheduleMeasure)
      window.removeEventListener('scroll', scheduleMeasure, true)
    }
  })

  return (
    <div ref={anchorRef} className={`agent-builder-field-anchor${className ? ` ${className}` : ''}`} data-builder-field-anchor={field}>
      {active && <AgentEditingMarker field={field} />}
      {children}
    </div>
  )
}

function AgentEditingMarker({ field }: { field: BuilderField }): JSX.Element {
  const t = useT()
  const fieldLabel = t(BUILDER_FIELD_LABEL_KEYS[field])
  const label = t('agentBuilder.editing.updatingField', { field: fieldLabel })

  return (
    <span className="agent-builder-edit-marker" aria-label={label}>
      <MousePointer2 className="agent-builder-edit-marker-arrow" size={17} aria-hidden />
      <span className="agent-builder-edit-marker-pill">{label}</span>
    </span>
  )
}

function InstructionsField({
  value,
  preview,
  editingField,
  onChange
}: {
  value: string
  preview: boolean
  editingField: BuilderField | null
  onChange: (value: string) => void
}): JSX.Element {
  const textareaRef = useRef<HTMLTextAreaElement>(null)

  useLayoutEffect(() => {
    const textarea = textareaRef.current
    if (!textarea) return
    textarea.style.height = 'auto'
    textarea.style.height = `${textarea.scrollHeight}px`
  }, [value, preview])

  return (
    <FieldAnchor field="instructions" active={editingField === 'instructions'} className="agent-builder-field-anchor-instructions">
      {preview ? (
        <div
          className="agent-builder-instr-preview"
          data-agent-builder-marker-target
          data-agent-builder-marker-offset-x={12}
          data-agent-builder-marker-offset-y={22}
        >
          {value.trim() ? (
            <MarkdownRenderer content={value} containOverflow enableMermaid={false} />
          ) : (
            <span className="agent-builder-instr-empty">{INSTRUCTIONS_PLACEHOLDER}</span>
          )}
        </div>
      ) : (
        <textarea
          ref={textareaRef}
          className="agent-builder-instr"
          rows={6}
          value={value}
          placeholder={INSTRUCTIONS_PLACEHOLDER}
          data-agent-builder-marker-target
          data-agent-builder-marker-offset-x={12}
          data-agent-builder-marker-offset-y={22}
          onChange={(e) => onChange(e.target.value)}
        />
      )}
    </FieldAnchor>
  )
}

interface CatalogOption {
  id: string
  label: string
  description?: string
}

/**
 * Selected items as removable chips (hover → X) + a dashed "+ Add" affordance that opens a
 * searchable popover of the remaining catalog. Selecting an item adds it; the popover stays open
 * for quick multi-add. Used for the active Tools policy list, MCP, and Skills selectors.
 */
function CatalogField({
  options,
  selected,
  onChange,
  addLabel,
  emptyHint,
  kind,
  field,
  editingField,
  readOnly = false
}: {
  options: CatalogOption[]
  selected: string[]
  onChange: (next: string[]) => void
  addLabel: string
  emptyHint?: string
  kind: CatalogKind
  field: BuilderField
  editingField: BuilderField | null
  readOnly?: boolean
}): JSX.Element {
  const [pickerOpen, setPickerOpen] = useState(false)
  const addRef = useRef<HTMLButtonElement>(null)
  const byId = useMemo(() => new Map(options.map((o) => [o.id, o])), [options])
  const addable = options.filter((o) => !selected.includes(o.id))

  if (readOnly && selected.length === 0) {
    return (
      <FieldAnchor field={field} active={editingField === field} className="agent-builder-field-anchor-catalog">
        <span className="agent-builder-pick-empty" data-agent-builder-marker-target>None</span>
      </FieldAnchor>
    )
  }

  return (
    <FieldAnchor field={field} active={editingField === field} className="agent-builder-field-anchor-catalog">
      <div className="agent-builder-pick">
        {!readOnly && selected.length === 0 && emptyHint && (
          <span className="agent-builder-pick-empty" data-agent-builder-marker-target>{emptyHint}</span>
        )}
        <div className="agent-builder-chiprow">
          {selected.map((id, index) => {
            const option = byId.get(id)
            const label = option?.label ?? id
            return (
              <AgentBuilderChip
                key={id}
                label={label}
                icon={catalogIcon(kind, id)}
                readOnly={readOnly}
                markerTarget={index === 0}
                onRemove={() => onChange(selected.filter((x) => x !== id))}
              />
            )
          })}
          {!readOnly && (
            <button
              ref={addRef}
              type="button"
              className="agent-builder-add"
              aria-expanded={pickerOpen}
              data-agent-builder-marker-target={selected.length === 0 && !emptyHint ? true : undefined}
              onClick={() => setPickerOpen((v) => !v)}
            >
              <Plus size={14} /> {addLabel}
            </button>
          )}
        </div>
        {pickerOpen && !readOnly && (
          <CatalogAddPopover
            anchor={addRef.current}
            options={addable}
            kind={kind}
            onPick={(id) => onChange([...selected, id])}
            onClose={() => setPickerOpen(false)}
          />
        )}
      </div>
    </FieldAnchor>
  )
}

function AgentBuilderChip({
  label,
  icon: Icon,
  readOnly,
  markerTarget,
  onRemove
}: {
  label: string
  icon: LucideIcon
  readOnly: boolean
  markerTarget?: boolean
  onRemove: () => void
}): JSX.Element {
  const t = useT()
  return (
    <span className={`agent-builder-chip${readOnly ? ' is-readonly' : ' is-removable'}`}>
      <span className="agent-builder-chip-icon-slot" aria-hidden={readOnly ? true : undefined}>
        <Icon className="agent-builder-chip-ic agent-builder-chip-ic-default" size={13} strokeWidth={2} aria-hidden />
        {!readOnly && (
          <button type="button" className="agent-builder-chip-remove" aria-label={t('agentBuilder.removeChip', { item: label })} onClick={onRemove}>
            <X size={12} strokeWidth={2.2} aria-hidden />
          </button>
        )}
      </span>
      <span className="agent-builder-chip-label" data-agent-builder-marker-target={markerTarget ? true : undefined}>{label}</span>
    </span>
  )
}

/** Portal popover anchored under the "+ Add" button: a search box + the addable catalog list. */
function CatalogAddPopover({
  anchor,
  options,
  kind,
  onPick,
  onClose
}: {
  anchor: HTMLElement | null
  options: CatalogOption[]
  kind: CatalogKind
  onPick: (id: string) => void
  onClose: () => void
}): JSX.Element | null {
  const menuRef = useRef<HTMLDivElement>(null)
  const [query, setQuery] = useState('')
  const [pos, setPos] = useState<{ top: number; left: number; width: number } | null>(null)

  useLayoutEffect(() => {
    if (!anchor) return
    const rect = anchor.getBoundingClientRect()
    const width = Math.max(rect.width, 260)
    const left = Math.max(8, Math.min(rect.left, window.innerWidth - width - 8))
    setPos({ top: rect.bottom + 6, left, width })
  }, [anchor])

  useEffect(() => {
    function onDown(event: MouseEvent): void {
      const target = event.target as Node
      if (menuRef.current?.contains(target) || anchor?.contains(target)) return
      onClose()
    }
    function onKey(event: KeyboardEvent): void {
      if (event.key === 'Escape') onClose()
    }
    document.addEventListener('mousedown', onDown, true)
    document.addEventListener('keydown', onKey, true)
    return () => {
      document.removeEventListener('mousedown', onDown, true)
      document.removeEventListener('keydown', onKey, true)
    }
  }, [anchor, onClose])

  if (!pos) return null
  const q = query.trim().toLowerCase()
  const filtered = q ? options.filter((o) => o.label.toLowerCase().includes(q) || o.id.toLowerCase().includes(q)) : options

  return createPortal(
    <div ref={menuRef} className="agent-builder-addmenu" style={{ top: pos.top, left: pos.left, width: pos.width }} role="listbox">
      <div className="agent-builder-addmenu-search">
        <Search size={13} />
        <Input bare autoFocus value={query} placeholder="Search…" onChange={(e) => setQuery(e.target.value)} />
      </div>
      <div className="agent-builder-addmenu-list">
        {filtered.length === 0 ? (
          <div className="agent-builder-addmenu-empty">Nothing to add</div>
        ) : (
          filtered.map((o) => {
            const Icon = catalogIcon(kind, o.id)
            return (
              <button key={o.id} type="button" role="option" className="agent-builder-addmenu-opt" onClick={() => onPick(o.id)}>
                <span className="agent-builder-addmenu-ic" aria-hidden><Icon size={14} strokeWidth={2} /></span>
                <span className="agent-builder-addmenu-copy">
                  <span className="agent-builder-addmenu-name">{o.label}</span>
                  {o.description && <span className="agent-builder-addmenu-desc">{o.description}</span>}
                </span>
              </button>
            )
          })
        )}
      </div>
    </div>,
    document.body
  )
}
