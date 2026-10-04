import type { ModelCatalogItem, ThreadConfiguration, ThreadRuntimeState, UserInputQuestion } from '@dotcraft/sdk/contracts'
import type { UsageWindow } from './accountUsage'
import { chatState, needsYou, type ChatState } from './chatState'
import { applyContext, type ContextUpdate, type ContextUsage } from './contextUsage'
import type { ReferenceEntry } from './draft'
import type { RelayInfo } from './gateway'
import { applyEvent, emptyHistory, type ChatHistory, type Echo, type HistoryEvent, type HistoryItem } from './history'
import type { PairingOffer } from './pairing'

export interface ComputerRecord {
  name: string
  version: string | null
  port: number
  fingerprint: string
  addresses: string[]
  relay: RelayInfo | null
  lastAddress: string | null
  deviceId: string
  pairedAt: string
}

export interface ProjectInfo {
  id: string
  name: string
  isChats: boolean
  running: boolean
  lastActiveAt: string | null
}

export type ProjectPhase = 'connecting' | 'ready' | 'starting' | 'cantStart'

export interface ChatSummary {
  key: string
  projectId: string
  threadId: string
  title: string | null
  updatedAt: string | null
  runtime: ThreadRuntimeState | null
  lastTurnFailed: boolean
  profileId: string | null
}

export interface ChatDetail {
  loading: boolean
  history: ChatHistory
  profileName: string | null
  config: ThreadConfiguration | null
  workspacePath: string | null
  context: ContextUsage | null
}

export interface ProjectModels {
  canConfigure: boolean
  canListModels: boolean
  canFork: boolean
  fileSystem: boolean
  canListCommands: boolean
  canListSkills: boolean
  canReadUsage: boolean
  defaultProviderId: string | null
  providers: { id: string; name: string; signsIn: boolean }[]
  catalogs: Record<string, ModelCatalogItem[]>
  usage: UsageWindow[] | null
}

export type PendingRequest =
  | {
      kind: 'approval'
      requestId: string
      approvalType: string
      operation: string
      target: string
      targetLabel: string | null
      reason: string
    }
  | { kind: 'question'; requestId: string; isBlocking: boolean; questions: UserInputQuestion[] }

export type PairingState =
  | { step: 'idle' }
  | { step: 'reaching'; offer: PairingOffer }
  | { step: 'allow'; offer: PairingOffer; allowing: boolean }
  | { step: 'unreachable'; offer: PairingOffer }
  | { step: 'invalid' }
  | { step: 'connected'; name: string }

export type Link = 'idle' | 'connecting' | 'online' | 'offline'

export interface MobileState {
  hydrated: boolean
  computer: ComputerRecord | null
  accessOff: boolean
  link: Link
  identityChanged: boolean
  revokedBy: string | null
  syncing: boolean
  syncedAt: string | null
  projects: ProjectInfo[]
  phases: Record<string, ProjectPhase>
  chats: Record<string, ChatSummary>
  details: Record<string, ChatDetail>
  pending: Record<string, PendingRequest[]>
  models: Record<string, ProjectModels>
  references: Record<string, ReferenceEntry[]>
  pairing: PairingState
}

export interface PersistedState {
  computer: ComputerRecord | null
  accessOff: boolean
  syncedAt: string | null
  projects: ProjectInfo[]
  chats: Record<string, ChatSummary>
  details: Record<string, ChatDetail>
}

export type Action =
  | { type: 'hydrated'; persisted: PersistedState | null }
  | { type: 'paired'; computer: ComputerRecord }
  | { type: 'forgotten'; revokedBy: string | null }
  | { type: 'revokedNoticeSeen' }
  | { type: 'link'; link: Link }
  | { type: 'accessOff' }
  | { type: 'identityChanged' }
  | { type: 'syncing'; value: boolean; at?: string }
  | { type: 'computerSeen'; patch: Partial<Pick<ComputerRecord, 'name' | 'version' | 'port' | 'addresses' | 'relay' | 'lastAddress'>> }
  | { type: 'projects'; projects: ProjectInfo[] }
  | { type: 'projectRunning'; projectId: string; running: boolean }
  | { type: 'phase'; projectId: string; phase: ProjectPhase | null }
  | { type: 'threads'; projectId: string; chats: ChatSummary[] }
  | { type: 'chatUpserted'; chat: ChatSummary }
  | { type: 'chatPatched'; key: string; patch: Partial<Omit<ChatSummary, 'key' | 'projectId' | 'threadId'>> }
  | { type: 'chatRemoved'; key: string }
  | { type: 'detailLoading'; key: string }
  | {
      type: 'detailLoaded'
      key: string
      history: ChatHistory
      profileName: string | null
      config: ThreadConfiguration | null
      workspacePath: string | null
      context: ContextUsage | null
    }
  | { type: 'chatConfig'; key: string; config: ThreadConfiguration }
  | { type: 'context'; key: string; update: ContextUpdate }
  | {
      type: 'capabilities'
      projectId: string
      capabilities: Pick<ProjectModels, 'canConfigure' | 'canListModels' | 'canFork' | 'fileSystem' | 'canListCommands' | 'canListSkills' | 'canReadUsage'>
    }
  | { type: 'usage'; projectId: string; windows: UsageWindow[] }
  | { type: 'references'; projectId: string; entries: ReferenceEntry[] }
  | { type: 'catalog'; projectId: string; providerId: string; models: ModelCatalogItem[]; isDefault: boolean }
  | { type: 'providers'; projectId: string; providers: ProjectModels['providers'] }
  | { type: 'history'; key: string; event: HistoryEvent }
  | { type: 'echo'; key: string; echo: Echo }
  | { type: 'echoDropped'; key: string; clientId: string }
  | { type: 'pendingAdded'; key: string; request: PendingRequest }
  | { type: 'pendingRemoved'; key: string; requestId: string }
  | { type: 'pendingCleared'; projectId: string }
  | { type: 'pairing'; pairing: PairingState }

export function initialState(): MobileState {
  return {
    hydrated: false,
    computer: null,
    accessOff: false,
    link: 'idle',
    identityChanged: false,
    revokedBy: null,
    syncing: false,
    syncedAt: null,
    projects: [],
    phases: {},
    chats: {},
    details: {},
    pending: {},
    models: {},
    references: {},
    pairing: { step: 'idle' },
  }
}

export function chatKey(projectId: string, threadId: string): string {
  return `${projectId}:${threadId}`
}

function cleared(state: MobileState): MobileState {
  return {
    ...state,
    accessOff: false,
    link: 'idle',
    identityChanged: false,
    syncing: false,
    syncedAt: null,
    projects: [],
    phases: {},
    chats: {},
    details: {},
    pending: {},
    models: {},
    references: {},
  }
}

function withoutKey<T>(record: Record<string, T>, key: string): Record<string, T> {
  if (!(key in record)) return record
  const next = { ...record }
  delete next[key]
  return next
}

function patchModels(state: MobileState, projectId: string, patch: Partial<ProjectModels>): MobileState {
  const current = state.models[projectId] ?? {
    canConfigure: false,
    canListModels: false,
    canFork: false,
    fileSystem: false,
    canListCommands: false,
    canListSkills: false,
    canReadUsage: false,
    defaultProviderId: null,
    providers: [],
    catalogs: {},
    usage: null,
  }
  return { ...state, models: { ...state.models, [projectId]: { ...current, ...patch } } }
}

function patchDetail(state: MobileState, key: string, update: (history: ChatHistory) => ChatHistory): MobileState {
  const detail = state.details[key]
  if (!detail) return state
  return { ...state, details: { ...state.details, [key]: { ...detail, history: update(detail.history) } } }
}

export function reducer(state: MobileState, action: Action): MobileState {
  switch (action.type) {
    case 'hydrated':
      return action.persisted ? { ...state, ...action.persisted, hydrated: true } : { ...state, hydrated: true }
    case 'paired':
      return { ...cleared(state), computer: action.computer, revokedBy: null, syncing: true }
    case 'forgotten':
      return { ...cleared(state), computer: null, revokedBy: action.revokedBy, pairing: { step: 'idle' } }
    case 'revokedNoticeSeen':
      return state.revokedBy === null ? state : { ...state, revokedBy: null }
    case 'link':
      if (state.link === action.link) return state
      return action.link === 'online' ? { ...state, link: 'online', accessOff: false } : { ...state, link: action.link }
    case 'accessOff':
      return state.accessOff ? state : { ...state, accessOff: true }
    case 'identityChanged':
      return { ...state, identityChanged: true, link: 'offline', syncing: false, pending: {}, phases: {} }
    case 'syncing':
      return { ...state, syncing: action.value, syncedAt: action.at ?? state.syncedAt }
    case 'computerSeen':
      return state.computer ? { ...state, computer: { ...state.computer, ...action.patch } } : state
    case 'projects':
      return { ...state, projects: action.projects }
    case 'projectRunning': {
      const projects = state.projects.map((project) =>
        project.id === action.projectId ? { ...project, running: action.running } : project,
      )
      return { ...state, projects }
    }
    case 'phase': {
      if (action.phase === null) return { ...state, phases: withoutKey(state.phases, action.projectId) }
      if (state.phases[action.projectId] === action.phase) return state
      return { ...state, phases: { ...state.phases, [action.projectId]: action.phase } }
    }
    case 'threads': {
      const chats: Record<string, ChatSummary> = {}
      for (const [key, chat] of Object.entries(state.chats)) {
        if (chat.projectId !== action.projectId) chats[key] = chat
      }
      for (const chat of action.chats) {
        const previous = state.chats[chat.key]
        chats[chat.key] = {
          ...chat,
          title: chat.title ?? previous?.title ?? null,
          lastTurnFailed: chat.runtime?.running ? false : (previous?.lastTurnFailed ?? chat.lastTurnFailed),
          profileId: chat.profileId ?? previous?.profileId ?? null,
        }
      }
      return { ...state, chats }
    }
    case 'chatUpserted':
      return { ...state, chats: { ...state.chats, [action.chat.key]: action.chat } }
    case 'chatPatched': {
      const chat = state.chats[action.key]
      return chat ? { ...state, chats: { ...state.chats, [action.key]: { ...chat, ...action.patch } } } : state
    }
    case 'chatRemoved':
      return {
        ...state,
        chats: withoutKey(state.chats, action.key),
        details: withoutKey(state.details, action.key),
        pending: withoutKey(state.pending, action.key),
      }
    case 'detailLoading': {
      const detail = state.details[action.key]
      return {
        ...state,
        details: {
          ...state.details,
          [action.key]: detail
            ? { ...detail, loading: true }
            : { loading: true, history: emptyHistory(), profileName: null, config: null, workspacePath: null, context: null },
        },
      }
    }
    case 'detailLoaded':
      return {
        ...state,
        details: {
          ...state.details,
          [action.key]: {
            loading: false,
            history: action.history,
            profileName: action.profileName,
            config: action.config,
            workspacePath: action.workspacePath,
            context: action.context,
          },
        },
      }
    case 'chatConfig': {
      const detail = state.details[action.key]
      return detail ? { ...state, details: { ...state.details, [action.key]: { ...detail, config: action.config } } } : state
    }
    case 'context': {
      const detail = state.details[action.key]
      if (!detail) return state
      return { ...state, details: { ...state.details, [action.key]: { ...detail, context: applyContext(detail.context ?? null, action.update) } } }
    }
    case 'capabilities':
      return patchModels(state, action.projectId, action.capabilities)
    case 'usage':
      return patchModels(state, action.projectId, { usage: action.windows })
    case 'references':
      return { ...state, references: { ...state.references, [action.projectId]: action.entries } }
    case 'catalog': {
      const current = state.models[action.projectId]
      return patchModels(state, action.projectId, {
        catalogs: { ...current?.catalogs, [action.providerId]: action.models },
        ...(action.isDefault ? { defaultProviderId: action.providerId } : {}),
      })
    }
    case 'providers':
      return patchModels(state, action.projectId, { providers: action.providers })
    case 'history':
      return patchDetail(state, action.key, (history) => applyEvent(history, action.event))
    case 'echo':
      return patchDetail(state, action.key, (history) => ({ ...history, echoes: [...history.echoes, action.echo] }))
    case 'echoDropped':
      return patchDetail(state, action.key, (history) => ({
        ...history,
        echoes: history.echoes.filter((echo) => echo.clientId !== action.clientId),
      }))
    case 'pendingAdded': {
      const list = (state.pending[action.key] ?? []).filter((request) => request.requestId !== action.request.requestId)
      return { ...state, pending: { ...state.pending, [action.key]: [...list, action.request] } }
    }
    case 'pendingRemoved': {
      const list = state.pending[action.key]
      if (!list?.some((request) => request.requestId === action.requestId)) return state
      const rest = list.filter((request) => request.requestId !== action.requestId)
      return { ...state, pending: rest.length > 0 ? { ...state.pending, [action.key]: rest } : withoutKey(state.pending, action.key) }
    }
    case 'pendingCleared': {
      const pending: Record<string, PendingRequest[]> = {}
      for (const [key, list] of Object.entries(state.pending)) {
        if (!key.startsWith(`${action.projectId}:`)) pending[key] = list
      }
      return { ...state, pending }
    }
    case 'pairing':
      return { ...state, pairing: action.pairing }
  }
}

export type ComputerStatus = 'online' | 'connecting' | 'offline' | 'access-off'

export function computerStatus(state: Pick<MobileState, 'link' | 'accessOff'>): ComputerStatus {
  if (state.link === 'online') return 'online'
  if (state.accessOff) return 'access-off'
  return state.link === 'offline' ? 'offline' : 'connecting'
}

export function isReachable(status: ComputerStatus): boolean {
  return status === 'online' || status === 'connecting'
}

export function stateOf(chat: ChatSummary): ChatState {
  return chatState(chat.runtime, chat.lastTurnFailed)
}

function time(value: string | null): number {
  return Date.parse(value ?? '') || 0
}

function byRecent(left: ChatSummary, right: ChatSummary): number {
  return time(right.updatedAt) - time(left.updatedAt)
}

export function projectById(state: Pick<MobileState, 'projects'>, projectId: string): ProjectInfo | undefined {
  return state.projects.find((project) => project.id === projectId)
}

export function runningChats(state: Pick<MobileState, 'projects' | 'chats'>): ChatSummary[] {
  const running = new Set(state.projects.filter((project) => project.running).map((project) => project.id))
  return Object.values(state.chats)
    .filter((chat) => running.has(chat.projectId))
    .sort(byRecent)
}

export function projectChats(state: Pick<MobileState, 'chats'>, projectId: string): ChatSummary[] {
  return Object.values(state.chats)
    .filter((chat) => chat.projectId === projectId)
    .sort(byRecent)
}

export function homeLists(state: Pick<MobileState, 'projects' | 'chats'>): { waiting: ChatSummary[]; recent: ChatSummary[] } {
  const visible = runningChats(state)
  return {
    waiting: visible.filter((chat) => needsYou(stateOf(chat))),
    recent: visible.filter((chat) => !needsYou(stateOf(chat))).slice(0, 5),
  }
}

export function projectsByRecentUse(state: Pick<MobileState, 'projects' | 'chats'>): ProjectInfo[] {
  const latest = new Map<string, number>()
  for (const chat of Object.values(state.chats)) {
    latest.set(chat.projectId, Math.max(latest.get(chat.projectId) ?? 0, time(chat.updatedAt)))
  }
  const score = (project: ProjectInfo) => Math.max(latest.get(project.id) ?? 0, time(project.lastActiveAt))
  return [...state.projects].sort((left, right) => score(right) - score(left))
}

function withoutImageData(item: HistoryItem): HistoryItem {
  if (item.type === 'imageGeneration') return { ...item, payload: { ...item.payload, result: undefined, imageDropped: true } }
  const parts = item.payload.nativeInputParts
  if (item.type === 'userMessage' && Array.isArray(parts)) {
    return { ...item, payload: { ...item.payload, nativeInputParts: parts.filter((part: { type?: string }) => part.type !== 'image') } }
  }
  const contentItems = item.payload.contentItems
  if (!Array.isArray(contentItems)) return item
  return { ...item, payload: { ...item.payload, contentItems: contentItems.filter((entry: { type?: string }) => entry.type !== 'image') } }
}

function trimmed(history: ChatHistory): ChatHistory {
  const items = history.items.slice(-200).map(withoutImageData)
  const turnIds = new Set(items.map((item) => item.turnId))
  return { items, turns: history.turns.filter((turn) => turnIds.has(turn.id)), echoes: [] }
}

export function persistable(state: MobileState): PersistedState {
  const details: PersistedState['details'] = {}
  const keys = Object.keys(state.details)
    .filter((key) => state.chats[key])
    .sort((left, right) => byRecent(state.chats[left], state.chats[right]))
    .slice(0, 8)
  for (const key of keys) {
    const detail = state.details[key]
    details[key] = { ...detail, loading: false, history: trimmed(detail.history) }
  }
  return {
    computer: state.computer,
    accessOff: state.accessOff,
    syncedAt: state.syncedAt,
    projects: state.projects,
    chats: state.chats,
    details,
  }
}
