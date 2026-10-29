import type {
  AgentProfileEntry,
  ApprovalResponseResult,
  InputPart,
  ItemDeltaNotification,
  ItemNotification,
  ProviderListResult,
  SessionThread,
  ServerNotificationMethods,
  ThreadConfiguration,
  ThreadReadState,
  ThreadRuntimeState,
  TurnNotification,
  UserInputResponseResult,
} from '@dotcraft/sdk/contracts'
import { DotCraftWireClient, ERR_TURN_IN_PROGRESS, JsonRpcError } from '@dotcraft/sdk/wire'
import { providerUsage } from './accountUsage'
import type { Timers } from './backoff'
import { StreamBatch } from './streamBatch'
import { readTurnPage } from './projectHistory'
import { AttachmentUploadError, uploadAttachments, uploadPhotos, type UploadedPhoto } from './attachments'
import type { BackgroundTask } from './backgroundTasks'
import { BackgroundTaskSync } from './backgroundTaskSync'
import { imageKey, imageScope, rememberImage } from './imageCache'
import { followUpMethod } from './chatState'
import { contextUsageOf, systemEventUpdate, usageDeltaUpdate, type ContextUpdate } from './contextUsage'
import { systemEventSignals, turnSignals } from './signals'
import { inputParts, visibleText, type MessageDraft, type ReferenceEntry } from './draft'
import { GatewayError, type GatewayClient } from './gateway'
import {
  applyEvents,
  emptyHistory,
  historyFromTurns,
  restoreEchoes,
  type HistoryEvent,
  type HistoryItem,
} from './history'
import { HTTP_REJECTED } from './pinned'
import { PinnedSocketTransport, SocketOpenError } from './pinnedSocketTransport'
import type { PinnedSockets, SocketEnd } from './sockets'
import { applyChange, changesWorkspaceApproval, workspaceApprovalOf, type ConfigChange } from './threadConfig'
import { chatKey, NO_SIGNALS, type ChatSignals, type ChatSummary, type ComputerAction, type ComputerState, type PendingRequest, type ProjectModels } from './state'
import type { Store } from './store'

const PHONE_IDENTITY = { channelName: 'dotcraft-desktop', userId: 'local' }

const OPTED_OUT_NOTIFICATIONS = ['item/toolCall/argumentsDelta', 'subagent/progress', 'plan/updated', 'terminal/outputDelta']

const COMPACTION_TIMEOUT_MS = 5 * 60 * 1000

export type ApprovalDecision = 'once' | 'session' | 'reject'

export interface CompactionResult {
  outcome: string
  message: string | null
}

const APPROVAL_DECISIONS: Record<ApprovalDecision, 'accept' | 'acceptForSession' | 'decline'> = {
  once: 'accept',
  session: 'acceptForSession',
  reject: 'decline',
}

export class ProjectNotRunningError extends Error {
  constructor() {
    super('The project is not running on the computer.')
    this.name = 'ProjectNotRunningError'
  }
}

export class NotConnectedError extends Error {
  constructor() {
    super('The project is not connected.')
    this.name = 'NotConnectedError'
  }
}

export interface ProjectConnectionOptions {
  projectId: string
  sockets: PinnedSockets
  gateway: GatewayClient
  store: Store<ComputerState, ComputerAction>
  appVersion: string
  timers?: Timers
  openThreads(): string[]
  onLost(end: SocketEnd): void
}

function titleFrom(text: string): string | null {
  const words = text.trim().split(/\s+/).slice(0, 6).join(' ')
  return words ? words.charAt(0).toUpperCase() + words.slice(1) : null
}

function newId(): string {
  return `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`
}

function isHidden(thread: SessionThread): boolean {
  return Boolean(thread.parentThreadId || thread.ephemeral || thread.source.kind === 'subagent' || thread.metadata['dotcraft.internal'])
}

const now = () => new Date().toISOString()

function providerEntries(result: ProviderListResult): ProjectModels['providers'] {
  return (result.providers ?? []).flatMap((provider) =>
    provider.id ? [{ id: provider.id, name: provider.displayName || provider.id }] : [],
  )
}

export class ProjectConnection {
  private client: DotCraftWireClient | null = null
  private closing = false
  private readyValue = false
  private readonly held = new Map<string, { resolve: (result: object) => void; reject: (error: Error) => void }>()
  private readonly subscribed = new Set<string>()
  private readonly capturing = new Map<string, HistoryEvent[]>()
  private readonly syncing = new Map<string, Promise<void>>()
  private readonly loadingOlder = new Set<string>()
  private readonly lookedUp = new Set<string>()
  private profiles: AgentProfileEntry[] | null = null
  private readonly catalogs = new Map<string, Promise<void>>()
  private references: Promise<void> | null = null
  private tasks: BackgroundTaskSync | null = null
  private readonly stream: StreamBatch

  constructor(private readonly options: ProjectConnectionOptions) {
    this.stream = new StreamBatch((threadId, deltas) => {
      if (this.subscribed.has(threadId)) this.store.dispatch({ type: 'historyBatch', key: this.key(threadId), deltas })
    }, options.timers)
  }

  get projectId(): string {
    return this.options.projectId
  }

  get ready(): boolean {
    return this.readyValue
  }

  private get store(): Store<ComputerState, ComputerAction> {
    return this.options.store
  }

  private key(threadId: string): string {
    return chatKey(this.projectId, threadId)
  }

  private patch(threadId: string, patch: Extract<ComputerAction, { type: 'chatPatched' }>['patch']): void {
    this.store.dispatch({ type: 'chatPatched', key: this.key(threadId), patch })
  }

  async connect(): Promise<void> {
    const { sockets, gateway } = this.options
    let transport: PinnedSocketTransport
    try {
      transport = await PinnedSocketTransport.open(sockets, gateway.socket(`/m/projects/${this.projectId}/appserver`))
    } catch (error) {
      if (error instanceof SocketOpenError && error.end.kind === 'failed' && error.end.code === HTTP_REJECTED) {
        if (error.end.status === 409) throw new ProjectNotRunningError()
        if (error.end.status === 401) throw new GatewayError(401, error.end.message)
      }
      throw error
    }
    if (this.closing) {
      await transport.close()
      throw new NotConnectedError()
    }
    const client = new DotCraftWireClient(transport)
    this.client = client
    this.register(client)
    transport.onEnd((end) => {
      if (this.client !== client) return
      this.stream.flush()
      for (const threadId of [...this.capturing.keys()]) this.flushCaptured(threadId)
      this.client = null
      this.readyValue = false
      this.held.clear()
      this.subscribed.clear()
      this.capturing.clear()
      if (!this.closing) this.options.onLost(end)
    })
    await client.initialize({
      clientName: 'dotcraft-mobile',
      clientTitle: 'DotCraft Mobile',
      clientVersion: this.options.appVersion,
      approvalSupport: true,
      requestUserInputSupport: true,
      streamingSupport: true,
      configChange: true,
      optOutNotifications: OPTED_OUT_NOTIFICATIONS,
      extraCapabilities: { backgroundTerminals: true },
    })
    if (this.client !== client || this.closing) throw new NotConnectedError()
    const capabilities = client.initializeResult?.capabilities
    this.store.dispatch({
      type: 'capabilities',
      projectId: this.projectId,
      capabilities: {
        canConfigure: capabilities?.configOverride === true,
        canListModels: capabilities?.modelCatalogManagement === true,
        canFork: capabilities?.threadFork === true,
        fileSystem: capabilities?.fileSystem === true,
        canListCommands: capabilities?.commandManagement === true,
        canListSkills: capabilities?.skillsManagement === true,
        canReadUsage: capabilities?.providerManagement === true,
        canCompact: capabilities?.manualCompaction === true,
      },
    })
    const tasks = new BackgroundTaskSync(client, this.projectId, this.store)
    this.tasks = tasks
    await Promise.all([this.loadThreads(client), this.loadApprovalDefault(client).catch(() => undefined)])
    if (this.client !== client || this.closing) throw new NotConnectedError()
    this.readyValue = true
    const open = this.options.openThreads()
    tasks.reloadKnown(open)
    await Promise.all(open.map((threadId) => this.syncChat(threadId).catch(() => undefined)))
  }

  async close(): Promise<void> {
    this.stream.flush()
    for (const threadId of [...this.capturing.keys()]) this.flushCaptured(threadId)
    this.closing = true
    this.readyValue = false
    this.held.clear()
    this.subscribed.clear()
    this.capturing.clear()
    const client = this.client
    this.client = null
    await client?.stop()
  }

  private requireClient(): DotCraftWireClient {
    if (!this.client || !this.readyValue) throw new NotConnectedError()
    return this.client
  }

  private register(client: DotCraftWireClient): void {
    const on = <M extends keyof ServerNotificationMethods>(method: M, handler: (params: ServerNotificationMethods[M]['params']) => void) =>
      client.on(method, (params) => {
        if (this.client !== client || this.closing) return
        const scope = params as { threadId?: string; thread?: { id: string }; turn?: { threadId: string } }
        const threadId = scope.threadId ?? scope.thread?.id ?? scope.turn?.threadId
        if (threadId && method !== 'item/agentMessage/delta' && method !== 'item/reasoning/delta') this.stream.flush(threadId)
        handler(params)
      })
    client.registerServerRequestHandler('item/approval/request', (_id, params) =>
      this.hold<ApprovalResponseResult>(params.threadId, {
        kind: 'approval',
        requestId: params.requestId,
        approvalType: params.approvalType,
        operation: params.operation,
        target: params.target,
        targetLabel: params.targetLabel ?? null,
        reason: params.reason ?? '',
      }),
    )
    client.registerServerRequestHandler('item/tool/requestUserInput', (_id, params) =>
      this.hold<UserInputResponseResult>(params.threadId, {
        kind: 'question',
        requestId: params.requestId,
        isBlocking: params.isBlocking,
        questions: params.questions,
      }),
    )

    on('thread/status/changed', ({ threadId, runtime }) => {
      if (!threadId) return
      if (!this.store.getState().chats[this.key(threadId)]) {
        if (!this.tasks?.childChanged(threadId)) this.lookUp(client, threadId)
        return
      }
      this.patch(threadId, { runtime: runtime ?? null, updatedAt: now(), ...(runtime?.running ? { lastTurnFailed: false } : {}) })
    })
    on('thread/started', ({ thread }) => this.threadSeen(thread))
    on('thread/updated', ({ thread }) => {
      if (thread.configuration && this.subscribed.has(thread.id)) {
        this.store.dispatch({ type: 'chatConfig', key: this.key(thread.id), config: thread.configuration })
      }
    })
    on('thread/readState/changed', ({ threadId, readState }) => this.patch(threadId, { readState }))
    on('thread/renamed', ({ threadId, displayName }) => {
      if (threadId && displayName) this.patch(threadId, { title: displayName })
    })
    on('thread/deleted', ({ threadId }) => this.store.dispatch({ type: 'chatRemoved', key: this.key(threadId) }))
    on('thread/archived', ({ threadId }) => this.store.dispatch({ type: 'chatRemoved', key: this.key(threadId) }))
    const turn = (failed: boolean | null) => (params: TurnNotification) => {
      this.route(params.turn.threadId, { kind: 'turn', turn: params.turn })
      this.signal(params.turn.threadId, (signals) => turnSignals(signals, params.turn))
      this.patch(params.turn.threadId, { updatedAt: now(), ...(failed === null ? {} : { lastTurnFailed: failed }) })
    }
    on('turn/started', turn(null))
    on('turn/completed', turn(false))
    on('turn/failed', turn(true))
    on('turn/cancelled', turn(false))
    const item = (params: ItemNotification) => this.route(params.threadId, { kind: 'item', item: params.item })
    on('item/started', item)
    on('item/completed', item)
    const delta = (itemType: 'agentMessage' | 'reasoningContent') => (params: ItemDeltaNotification) => {
      if (!params.itemId || !params.turnId) return
      this.route(params.threadId, { kind: 'delta', itemId: params.itemId, turnId: params.turnId, itemType, delta: params.delta })
    }
    on('item/agentMessage/delta', delta('agentMessage'))
    on('item/reasoning/delta', delta('reasoningContent'))
    const resolved = (params: ItemNotification) => {
      const { requestId } = params.item.payload as { requestId: string }
      this.held.delete(requestId)
      this.store.dispatch({ type: 'pendingRemoved', key: this.key(params.threadId), requestId })
      item(params)
    }
    on('item/approval/resolved', resolved)
    on('item/tool/requestUserInput/resolved', resolved)
    on('turn/diff/updated', ({ threadId, turnId, diff }) => this.route(threadId, { kind: 'diff', turnId, diff }))
    on('item/usage/delta', (params) => this.contextChanged(params.threadId, usageDeltaUpdate(params)))
    on('system/event', (params) => {
      this.contextChanged(params.threadId, systemEventUpdate(params))
      if (params.threadId) this.signal(params.threadId, (signals) => systemEventSignals(signals, params))
    })
    on('provider/auth/usage/updated', (params) =>
      this.store.dispatch({ type: 'usage', projectId: this.projectId, providerId: params.providerId, usage: providerUsage(params) }),
    )
    on('config/changed', ({ regions }) => {
      if (changesWorkspaceApproval(regions)) void this.loadApprovalDefault(client).catch(() => undefined)
    })
  }

  private signal(threadId: string, change: (signals: ChatSignals) => ChatSignals): void {
    const key = this.key(threadId)
    const current = this.store.getState().signals[key] ?? NO_SIGNALS
    const signals = change(current)
    if (signals !== current) this.store.dispatch({ type: 'signals', key, signals })
  }

  private contextChanged(threadId: string | null | undefined, update: ContextUpdate | null): void {
    if (threadId && update && (this.subscribed.has(threadId) || this.capturing.has(threadId))) {
      this.store.dispatch({ type: 'context', key: this.key(threadId), update })
    }
  }

  private lookUp(client: DotCraftWireClient, threadId: string): void {
    if (this.lookedUp.has(threadId)) return
    this.lookedUp.add(threadId)
    client.request('thread/read', { threadId }).then(
      ({ thread }) => this.threadSeen(thread),
      () => this.lookedUp.delete(threadId),
    )
  }

  private ensureSummary(threadId: string): void {
    const key = this.key(threadId)
    if (this.store.getState().chats[key]) return
    this.store.dispatch({
      type: 'chatUpserted',
      chat: { key, projectId: this.projectId, threadId, title: null, updatedAt: now(), runtime: null, lastTurnFailed: false, profileId: null, readState: null },
    })
  }

  private threadSeen(thread: SessionThread): void {
    if (isHidden(thread)) return
    const key = this.key(thread.id)
    const previous = this.store.getState().chats[key]
    this.store.dispatch({
      type: 'chatUpserted',
      chat: {
        key,
        projectId: this.projectId,
        threadId: thread.id,
        title: thread.displayName ?? previous?.title ?? null,
        updatedAt: thread.lastActiveAt,
        runtime: thread.runtime,
        lastTurnFailed: previous?.lastTurnFailed ?? false,
        profileId: thread.configuration?.agentProfileId ?? null,
        spawnedFrom: thread.source.spawnedFromThreadId ?? null,
        readState: thread.readState ?? previous?.readState ?? null,
      },
    })
  }

  private route(threadId: string, event: HistoryEvent): void {
    if (this.closing || !this.client) return
    const buffer = this.capturing.get(threadId)
    if (buffer) buffer.push(event)
    else if (this.subscribed.has(threadId)) {
      if (event.kind === 'delta') this.stream.push(threadId, event)
      else this.store.dispatch({ type: 'history', key: this.key(threadId), event })
    }
  }

  private flushCaptured(threadId: string): boolean {
    const events = this.capturing.get(threadId)
    if (!events) return false
    this.capturing.delete(threadId)
    const key = this.key(threadId)
    const detail = this.store.getState().details[key]
    if (detail) this.store.dispatch({ type: 'detailLoaded', key, ...detail, history: applyEvents(detail.history, events) })
    return true
  }

  private hold<R extends object>(threadId: string, request: PendingRequest): Promise<R> {
    if (this.closing || !this.client) throw new NotConnectedError()
    this.stream.flush(threadId)
    this.ensureSummary(threadId)
    return new Promise<R>((resolve, reject) => {
      this.held.set(request.requestId, { resolve: resolve as (result: object) => void, reject })
      this.store.dispatch({ type: 'pendingAdded', key: this.key(threadId), request })
    })
  }

  private async loadThreads(client: DotCraftWireClient): Promise<void> {
    const result = await client.request('thread/list', { identity: PHONE_IDENTITY, scope: 'workspace', limit: 100 })
    const chats: ChatSummary[] = result.data.map((summary) => ({
      key: this.key(summary.id),
      projectId: this.projectId,
      threadId: summary.id,
      title: summary.displayName ?? null,
      updatedAt: summary.lastActiveAt ?? summary.createdAt ?? null,
      runtime: summary.runtime ?? null,
      lastTurnFailed: false,
      profileId: null,
      spawnedFrom: summary.source?.spawnedFromThreadId ?? null,
      readState: result.readStates?.[summary.id] ?? null,
    }))
    this.store.dispatch({ type: 'threads', projectId: this.projectId, chats })
  }

  private async loadApprovalDefault(client: DotCraftWireClient): Promise<void> {
    if (!client.initializeResult?.capabilities?.workspaceConfigManagement) return
    const { config } = await client.request('config/read', {})
    this.store.dispatch({ type: 'approvalDefault', projectId: this.projectId, policy: workspaceApprovalOf(config) })
  }

  async refresh(): Promise<void> {
    const client = this.requireClient()
    await Promise.all([this.loadThreads(client), this.loadApprovalDefault(client)])
  }

  private async profileName(client: DotCraftWireClient, profileId: string): Promise<string> {
    if (!this.profiles && client.initializeResult?.capabilities.agentProfileManagement) {
      this.profiles = (await client.request('agent/profiles/list', {})).profiles ?? []
    }
    return this.profiles?.find((profile) => profile.id === profileId)?.name || profileId
  }

  isOpen(threadId: string): boolean {
    return this.subscribed.has(threadId)
  }

  syncChat(threadId: string): Promise<void> {
    const current = this.syncing.get(threadId)
    if (current && this.capturing.has(threadId)) return current
    const loading = this.readChat(threadId).finally(() => {
      if (this.syncing.get(threadId) === loading) this.syncing.delete(threadId)
    })
    this.syncing.set(threadId, loading)
    return loading
  }

  private async readChat(threadId: string): Promise<void> {
    const client = this.requireClient()
    const key = this.key(threadId)
    this.stream.flush(threadId)
    const buffer: HistoryEvent[] = []
    this.capturing.set(threadId, buffer)
    this.store.dispatch({ type: 'detailLoading', key })
    try {
      if (!this.subscribed.has(threadId)) {
        await client.request('thread/subscribe', { threadId })
        if (this.client !== client || this.capturing.get(threadId) !== buffer) return
        this.subscribed.add(threadId)
      }
      const [read, page] = await Promise.all([client.request('thread/read', { threadId }), readTurnPage(client, threadId, null)])
      const thread = read.thread
      const profileId = thread.configuration?.agentProfileId
      const profileName = profileId ? await this.profileName(client, profileId) : null
      if (this.client !== client || this.capturing.get(threadId) !== buffer) return
      const history = restoreEchoes(
        applyEvents(historyFromTurns(page.turns, page.nextCursor), buffer),
        this.store.getState().details[key]?.history.echoes ?? [],
      )
      this.threadSeen(thread)
      this.patch(threadId, { lastTurnFailed: !thread.runtime.running && page.turns.at(-1)?.turn.status === 'failed' })
      this.store.dispatch({
        type: 'detailLoaded',
        key,
        history,
        profileName,
        config: thread.configuration ?? null,
        workspacePath: thread.workspacePath || null,
        effectiveWorkspacePath: thread.effectiveWorkspacePath || null,
        context: contextUsageOf(thread.contextUsage),
      })
      this.dropAnswered(key, history.items)
      void this.tasks?.load(threadId).catch(() => undefined)
    } catch (error) {
      const existing = this.store.getState().details[key]
      if (existing && this.client === client && this.capturing.get(threadId) === buffer) this.flushCaptured(threadId)
      throw error
    } finally {
      if (this.capturing.get(threadId) === buffer) this.capturing.delete(threadId)
    }
  }

  /** Loads the next page of older whole Turns of an open chat; one load per chat at a time. */
  async loadOlder(threadId: string): Promise<void> {
    const key = this.key(threadId)
    const cursor = this.store.getState().details[key]?.history.olderCursor
    if (!cursor || this.loadingOlder.has(threadId)) return
    this.loadingOlder.add(threadId)
    this.store.dispatch({ type: 'olderLoading', key, value: true })
    try {
      const page = await readTurnPage(this.requireClient(), threadId, cursor)
      this.store.dispatch({ type: 'olderLoaded', key, cursor, turns: page.turns, olderCursor: page.nextCursor })
    } finally {
      this.loadingOlder.delete(threadId)
      this.store.dispatch({ type: 'olderLoading', key, value: false })
    }
  }

  private dropAnswered(key: string, items: HistoryItem[]): void {
    const answered = new Set(
      items.filter((item) => item.type === 'approvalResponse' || item.type === 'userInputResponse').map((item) => item.payload.requestId),
    )
    for (const { requestId } of this.store.getState().pending[key] ?? []) {
      if (!answered.has(requestId)) continue
      this.held.delete(requestId)
      this.store.dispatch({ type: 'pendingRemoved', key, requestId })
    }
  }

  async markRead(threadId: string, current: ThreadReadState): Promise<void> {
    const client = this.requireClient()
    try {
      const { readState } = await client.request('thread/readState/update', {
        threadId,
        expectedRevision: current.revision,
        operation: { type: 'read' },
      })
      this.patch(threadId, { readState })
    } catch (error) {
      const data = error instanceof JsonRpcError ? (error.data as { code?: string; readState?: ThreadReadState } | undefined) : undefined
      if (data?.code !== 'readStateConflict' || !data.readState) throw error
      this.patch(threadId, { readState: data.readState })
    }
  }

  async unsubscribe(threadId: string): Promise<void> {
    this.stream.flush(threadId)
    const capturing = this.flushCaptured(threadId)
    if (!this.subscribed.delete(threadId) && !capturing) return
    await this.client?.request('thread/unsubscribe', { threadId }).catch(() => undefined)
  }

  async startThread(title: string, config?: ThreadConfiguration): Promise<string> {
    const client = this.requireClient()
    const { thread } = await client.request('thread/start', { identity: PHONE_IDENTITY, ...(config ? { config } : {}) })
    const key = this.key(thread.id)
    this.threadSeen(thread)
    this.patch(thread.id, { title: thread.displayName ?? titleFrom(title) })
    this.store.dispatch({
      type: 'detailLoaded',
      key,
      history: emptyHistory(),
      profileName: null,
      config: thread.configuration ?? null,
      workspacePath: thread.workspacePath || null,
      effectiveWorkspacePath: thread.effectiveWorkspacePath || null,
      context: contextUsageOf(thread.contextUsage),
    })
    await client.request('thread/subscribe', { threadId: thread.id })
    this.subscribed.add(thread.id)
    return key
  }

  async send(threadId: string, draft: MessageDraft): Promise<void> {
    const client = this.requireClient()
    const { files, photos } =
      draft.files.length + draft.photos.length > 0 ? await this.upload(client, threadId, draft) : { files: [], photos: [] }
    const input = inputParts(draft, files, photos)
    const shown = inputParts(draft, files)
    try {
      await this.submit(client, threadId, input, shown, this.store.getState().chats[this.key(threadId)]?.runtime ?? null)
    } catch (error) {
      if (!(error instanceof JsonRpcError) || error.rpcCode !== ERR_TURN_IN_PROGRESS) throw error
      const { thread } = await client.request('thread/read', { threadId })
      this.patch(threadId, { runtime: thread.runtime })
      await this.submit(client, threadId, input, shown, thread.runtime)
    }
  }

  private async upload(
    client: DotCraftWireClient,
    threadId: string,
    draft: MessageDraft,
  ): Promise<{ files: { path: string; name: string }[]; photos: UploadedPhoto[] }> {
    let root = this.store.getState().details[this.key(threadId)]?.workspacePath
    if (!root) {
      try {
        root = (await client.request('thread/read', { threadId })).thread.workspacePath
      } catch (error) {
        throw new AttachmentUploadError(draft.files[0]?.name ?? 'photo', error)
      }
    }
    const fileSystem = {
      createDirectory: async (path: string) => {
        await client.request('fs/createDirectory', { path, recursive: true })
      },
      writeFile: async (path: string, dataBase64: string) => {
        await client.request('fs/writeFile', { path, dataBase64 })
      },
    }
    const files = await uploadAttachments(fileSystem, root, draft.files, newId)
    const photos = await uploadPhotos(fileSystem, root, draft.photos, newId)
    const fingerprint = this.store.getState().computer?.fingerprint
    if (fingerprint) {
      const scope = imageScope(fingerprint, this.projectId)
      photos.forEach((photo, index) => rememberImage(imageKey(scope, photo.path), draft.photos[index].dataUrl))
    }
    return { files, photos }
  }

  private async submit(
    client: DotCraftWireClient,
    threadId: string,
    input: InputPart[],
    shown: InputPart[],
    runtime: ThreadRuntimeState | null,
  ): Promise<void> {
    const key = this.key(threadId)
    const method = followUpMethod(runtime)
    const clientUserMessageId = newId()
    const echo = { clientId: clientUserMessageId, text: visibleText(input), parts: shown, added: method !== 'start' }
    this.store.dispatch({ type: 'echo', key, echo })
    try {
      if (method === 'start') {
        await client.request('turn/start', { threadId, input, clientUserMessageId })
      } else if (method === 'steer' && runtime?.activeTurnId) {
        await client.request('turn/steer', { threadId, expectedTurnId: runtime.activeTurnId, input, clientUserMessageId })
      } else {
        await client.request('turn/enqueue', { threadId, input, clientUserMessageId })
      }
    } catch (error) {
      this.store.dispatch({ type: 'echoDropped', key, clientId: clientUserMessageId })
      throw error
    }
  }

  loadModels(providerId: string | null): Promise<void> {
    const client = this.requireClient()
    const slot = providerId ?? ''
    const known = this.catalogs.get(slot)
    if (known) return known
    const loading = this.fetchModels(client, providerId).catch((error: unknown) => {
      this.catalogs.delete(slot)
      throw error
    })
    this.catalogs.set(slot, loading)
    return loading
  }

  private async fetchModels(client: DotCraftWireClient, providerId: string | null): Promise<void> {
    const [catalog, providers] = await Promise.all([
      client.request('model/list', { providerId }),
      providerId === null && client.initializeResult?.capabilities?.providerManagement ? client.request('provider/list', {}) : null,
    ])
    if (catalog.providerId) {
      this.store.dispatch({
        type: 'catalog',
        projectId: this.projectId,
        providerId: catalog.providerId,
        models: catalog.success === false ? [] : (catalog.models ?? []),
        isDefault: providerId === null,
      })
    }
    if (providers) this.store.dispatch({ type: 'providers', projectId: this.projectId, providers: providerEntries(providers) })
  }

  async loadUsage(providerId: string | null): Promise<void> {
    const client = this.requireClient()
    if (!client.initializeResult?.capabilities?.providerManagement) return
    const defaultProvider = () => this.store.getState().models[this.projectId]?.defaultProviderId ?? null
    if (!providerId && !defaultProvider()) await this.loadModels(null)
    const id = providerId ?? defaultProvider()
    if (!id) return
    const usage = await client.request('provider/auth/usage', { providerId: id })
    this.store.dispatch({ type: 'usage', projectId: this.projectId, providerId: id, usage: providerUsage(usage) })
  }

  async readFile(path: string): Promise<string> {
    return (await this.requireClient().request('fs/readFile', { path })).dataBase64
  }

  loadReferences(): Promise<void> {
    const client = this.requireClient()
    this.references ??= this.fetchReferences(client).catch((error: unknown) => {
      this.references = null
      throw error
    })
    return this.references
  }

  private async fetchReferences(client: DotCraftWireClient): Promise<void> {
    const capabilities = client.initializeResult?.capabilities
    const [commands, skills] = await Promise.all([
      capabilities?.commandManagement ? client.request('command/list', { includeBuiltins: false }) : null,
      capabilities?.skillsManagement ? client.request('skills/list', {}) : null,
    ])
    const entries: ReferenceEntry[] = [
      ...(commands?.commands ?? []).flatMap((command): ReferenceEntry[] => {
        const name = command.name?.replace(/^\//, '')
        if (!name || command.category === 'builtin') return []
        return [{ kind: 'command', name, description: command.fallbackDescription || command.description || '' }]
      }),
      ...(skills?.skills ?? []).flatMap((skill): ReferenceEntry[] => {
        if (!skill.name || skill.enabled === false) return []
        return [{ kind: 'skill', name: skill.name, description: skill.shortDescription || skill.description || '' }]
      }),
    ]
    this.store.dispatch({ type: 'references', projectId: this.projectId, entries })
  }

  async setMode(threadId: string, mode: 'plan' | 'agent'): Promise<void> {
    await this.requireClient().request('thread/mode/set', { threadId, mode })
    const key = this.key(threadId)
    this.store.dispatch({ type: 'chatConfig', key, config: { ...this.store.getState().details[key]?.config, mode } })
  }

  async retry(threadId: string): Promise<void> {
    await this.requireClient().request('turn/start', { threadId, input: [] })
  }

  async compact(threadId: string): Promise<CompactionResult> {
    const result = await this.requireClient().request('thread/compact/start', { threadId }, COMPACTION_TIMEOUT_MS)
    const snapshot = contextUsageOf(result.contextUsage)
    if (snapshot) this.store.dispatch({ type: 'context', key: this.key(threadId), update: { kind: 'snapshot', snapshot } })
    return { outcome: result.outcome ?? 'failed', message: result.message ?? null }
  }

  async updateConfig(threadId: string, change: ConfigChange): Promise<void> {
    const client = this.requireClient()
    const { thread } = await client.request('thread/read', { threadId })
    const config = applyChange(thread.configuration ?? {}, change)
    await client.request('thread/config/update', { threadId, config })
    this.store.dispatch({ type: 'chatConfig', key: this.key(threadId), config })
  }

  async rename(threadId: string, displayName: string): Promise<void> {
    await this.requireClient().request('thread/rename', { threadId, displayName })
    this.patch(threadId, { title: displayName })
  }

  async fork(threadId: string): Promise<string> {
    const { thread } = await this.requireClient().request('thread/fork', { threadId })
    if (!thread) throw new Error('The fork response did not include a thread.')
    this.threadSeen(thread)
    return this.key(thread.id)
  }

  async archive(threadId: string): Promise<void> {
    await this.requireClient().request('thread/archive', { threadId })
    this.store.dispatch({ type: 'chatRemoved', key: this.key(threadId) })
  }

  async stopTask(threadId: string, task: BackgroundTask): Promise<void> {
    if (!this.tasks || !this.readyValue) throw new NotConnectedError()
    await this.tasks.stop(threadId, task)
  }

  async stop(threadId: string): Promise<void> {
    this.stream.flush(threadId)
    const client = this.requireClient()
    let turnId = this.store.getState().chats[this.key(threadId)]?.runtime?.activeTurnId
    if (!turnId) turnId = (await client.request('thread/read', { threadId })).thread.runtime.activeTurnId
    if (!turnId) return
    await client.request('turn/interrupt', { threadId, turnId })
    this.signal(threadId, (signals) => ({ ...signals, stopped: [...signals.stopped, turnId] }))
  }

  decide(threadId: string, requestId: string, decision: ApprovalDecision): void {
    this.release(threadId, requestId, { decision: APPROVAL_DECISIONS[decision] })
  }

  answer(threadId: string, requestId: string, answers: Record<string, string[]>): void {
    const mapped = Object.fromEntries(Object.entries(answers).map(([id, values]) => [id, { answers: values }]))
    this.release(threadId, requestId, { answers: mapped })
  }

  cancelRequest(threadId: string, requestId: string): void {
    const held = this.held.get(requestId)
    if (!held) return
    this.held.delete(requestId)
    held.reject(new Error('Interactive request was cancelled with its turn.'))
    this.store.dispatch({ type: 'pendingRemoved', key: this.key(threadId), requestId })
  }

  private release(threadId: string, requestId: string, result: object): void {
    this.stream.flush(threadId)
    const held = this.held.get(requestId)
    if (!held) return
    this.held.delete(requestId)
    held.resolve(result)
    this.store.dispatch({ type: 'pendingRemoved', key: this.key(threadId), requestId })
  }
}
