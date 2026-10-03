import type {
  AgentProfileEntry,
  ApprovalResponseResult,
  ItemDeltaNotification,
  ItemNotification,
  SessionThread,
  ThreadRuntimeState,
  TurnNotification,
  UserInputResponseResult,
} from '@dotcraft/sdk/contracts'
import { DotCraftWireClient, ERR_TURN_IN_PROGRESS, JsonRpcError } from '@dotcraft/sdk/wire'
import { followUpMethod } from './chatState'
import { GatewayError, type GatewayClient } from './gateway'
import { applyEvent, historyFromPages, restoreEchoes, type HistoryEvent } from './history'
import { HTTP_REJECTED } from './pinned'
import { PinnedSocketTransport, SocketOpenError } from './pinnedSocketTransport'
import type { PinnedSockets, SocketEnd } from './sockets'
import { chatKey, type Action, type ChatSummary, type MobileState, type PendingRequest } from './state'
import type { Store } from './store'

const PHONE_IDENTITY = { channelName: 'dotcraft-desktop', userId: 'local' }

const OPTED_OUT_NOTIFICATIONS = ['turn/diff/updated', 'item/usage/delta', 'item/toolCall/argumentsDelta', 'subagent/progress', 'plan/updated']

export type ApprovalDecision = 'once' | 'session' | 'reject'

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
  store: Store<MobileState, Action>
  appVersion: string
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

export class ProjectConnection {
  private client: DotCraftWireClient | null = null
  private closing = false
  private readyValue = false
  private readonly held = new Map<string, (result: object) => void>()
  private readonly subscribed = new Set<string>()
  private readonly capturing = new Map<string, HistoryEvent[]>()
  private profiles: AgentProfileEntry[] | null = null

  constructor(private readonly options: ProjectConnectionOptions) {}

  get projectId(): string {
    return this.options.projectId
  }

  get ready(): boolean {
    return this.readyValue
  }

  private get store(): Store<MobileState, Action> {
    return this.options.store
  }

  private key(threadId: string): string {
    return chatKey(this.projectId, threadId)
  }

  private patch(threadId: string, patch: Extract<Action, { type: 'chatPatched' }>['patch']): void {
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
      this.readyValue = false
      this.held.clear()
      if (!this.closing) this.options.onLost(end)
    })
    await client.initialize({
      clientName: 'dotcraft-mobile',
      clientTitle: 'DotCraft Mobile',
      clientVersion: this.options.appVersion,
      approvalSupport: true,
      requestUserInputSupport: true,
      streamingSupport: true,
      optOutNotifications: OPTED_OUT_NOTIFICATIONS,
    })
    await this.loadThreads(client)
    this.readyValue = true
    await Promise.all(this.options.openThreads().map((threadId) => this.syncChat(threadId).catch(() => undefined)))
  }

  async close(): Promise<void> {
    this.closing = true
    this.readyValue = false
    this.held.clear()
    this.subscribed.clear()
    this.capturing.clear()
    await this.client?.stop()
  }

  private requireClient(): DotCraftWireClient {
    if (!this.client || !this.readyValue) throw new NotConnectedError()
    return this.client
  }

  private register(client: DotCraftWireClient): void {
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
      this.hold<UserInputResponseResult>(params.threadId, { kind: 'question', requestId: params.requestId, questions: params.questions }),
    )

    client.on('thread/runtimeChanged', ({ threadId, runtime }) => {
      if (!threadId) return
      this.ensureSummary(threadId)
      this.patch(threadId, { runtime: runtime ?? null, updatedAt: now(), ...(runtime?.running ? { lastTurnFailed: false } : {}) })
    })
    client.on('thread/started', ({ thread }) => this.threadSeen(thread))
    client.on('thread/renamed', ({ threadId, displayName }) => {
      if (threadId && displayName) this.patch(threadId, { title: displayName })
    })
    client.on('thread/deleted', ({ threadId }) => this.store.dispatch({ type: 'chatRemoved', key: this.key(threadId) }))
    client.on('thread/statusChanged', ({ threadId, newStatus }) => {
      if (threadId && newStatus === 'archived') this.store.dispatch({ type: 'chatRemoved', key: this.key(threadId) })
    })
    const turn = (failed: boolean | null) => (params: TurnNotification) => {
      this.route(params.turn.threadId, { kind: 'turn', turn: params.turn })
      this.patch(params.turn.threadId, { updatedAt: now(), ...(failed === null ? {} : { lastTurnFailed: failed }) })
    }
    client.on('turn/started', turn(null))
    client.on('turn/completed', turn(false))
    client.on('turn/failed', turn(true))
    client.on('turn/cancelled', turn(false))
    const item = (params: ItemNotification) => this.route(params.threadId, { kind: 'item', item: params.item })
    client.on('item/started', item)
    client.on('item/completed', item)
    const delta = (itemType: 'agentMessage' | 'reasoningContent') => (params: ItemDeltaNotification) => {
      if (!params.itemId || !params.turnId) return
      this.route(params.threadId, { kind: 'delta', itemId: params.itemId, turnId: params.turnId, itemType, delta: params.delta })
    }
    client.on('item/agentMessage/delta', delta('agentMessage'))
    client.on('item/reasoning/delta', delta('reasoningContent'))
    const resolved = (params: ItemNotification) => {
      const { requestId } = params.item.payload as { requestId: string }
      this.held.delete(requestId)
      this.store.dispatch({ type: 'pendingRemoved', key: this.key(params.threadId), requestId })
      item(params)
    }
    client.on('item/approval/resolved', resolved)
    client.on('item/tool/requestUserInput/resolved', resolved)
  }

  private ensureSummary(threadId: string): void {
    const key = this.key(threadId)
    if (this.store.getState().chats[key]) return
    this.store.dispatch({
      type: 'chatUpserted',
      chat: { key, projectId: this.projectId, threadId, title: null, updatedAt: now(), runtime: null, lastTurnFailed: false, profileId: null },
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
      },
    })
  }

  private route(threadId: string, event: HistoryEvent): void {
    const buffer = this.capturing.get(threadId)
    if (buffer) buffer.push(event)
    else if (this.subscribed.has(threadId)) this.store.dispatch({ type: 'history', key: this.key(threadId), event })
  }

  private hold<R extends object>(threadId: string, request: PendingRequest): Promise<R> {
    this.ensureSummary(threadId)
    return new Promise<R>((resolve) => {
      this.held.set(request.requestId, resolve as (result: object) => void)
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
    }))
    this.store.dispatch({ type: 'threads', projectId: this.projectId, chats })
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

  async syncChat(threadId: string): Promise<void> {
    const client = this.requireClient()
    const key = this.key(threadId)
    const buffer: HistoryEvent[] = []
    this.capturing.set(threadId, buffer)
    this.store.dispatch({ type: 'detailLoading', key })
    try {
      await client.request('thread/subscribe', { threadId })
      this.subscribed.add(threadId)
      const [read, turns, items] = await Promise.all([
        client.request('thread/read', { threadId }),
        client.request('thread/turns/list', { threadId, limit: 20, sortDirection: 'descending' }),
        client.request('thread/items/list', { threadId, limit: 200, sortDirection: 'descending' }),
      ])
      const thread = read.thread
      const profileId = thread.configuration?.agentProfileId
      const profileName = profileId ? await this.profileName(client, profileId) : null
      const history = restoreEchoes(
        buffer.reduce(applyEvent, historyFromPages(items.data, turns.data)),
        this.store.getState().details[key]?.history.echoes ?? [],
      )
      this.threadSeen(thread)
      this.patch(threadId, { lastTurnFailed: !thread.runtime.running && turns.data[0]?.status === 'failed' })
      this.store.dispatch({ type: 'detailLoaded', key, history, profileName })
    } catch (error) {
      const existing = this.store.getState().details[key]
      if (existing) this.store.dispatch({ type: 'detailLoaded', key, history: existing.history, profileName: existing.profileName })
      throw error
    } finally {
      this.capturing.delete(threadId)
    }
  }

  async unsubscribe(threadId: string): Promise<void> {
    if (!this.subscribed.delete(threadId)) return
    await this.client?.request('thread/unsubscribe', { threadId }).catch(() => undefined)
  }

  async startThread(text: string): Promise<string> {
    const client = this.requireClient()
    const { thread } = await client.request('thread/start', { identity: PHONE_IDENTITY })
    const key = this.key(thread.id)
    this.threadSeen(thread)
    this.patch(thread.id, { title: thread.displayName ?? titleFrom(text) })
    this.store.dispatch({ type: 'detailLoaded', key, history: { items: [], turns: [], echoes: [] }, profileName: null })
    await client.request('thread/subscribe', { threadId: thread.id })
    this.subscribed.add(thread.id)
    await this.send(thread.id, text)
    return key
  }

  async send(threadId: string, text: string): Promise<void> {
    const client = this.requireClient()
    try {
      await this.submit(client, threadId, text, this.store.getState().chats[this.key(threadId)]?.runtime ?? null)
    } catch (error) {
      if (!(error instanceof JsonRpcError) || error.rpcCode !== ERR_TURN_IN_PROGRESS) throw error
      const { thread } = await client.request('thread/read', { threadId })
      this.patch(threadId, { runtime: thread.runtime })
      await this.submit(client, threadId, text, thread.runtime)
    }
  }

  private async submit(client: DotCraftWireClient, threadId: string, text: string, runtime: ThreadRuntimeState | null): Promise<void> {
    const key = this.key(threadId)
    const method = followUpMethod(runtime)
    const clientUserMessageId = newId()
    const input = [{ type: 'text' as const, text }]
    this.store.dispatch({ type: 'echo', key, echo: { clientId: clientUserMessageId, text, added: method !== 'start' } })
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

  async stop(threadId: string): Promise<void> {
    const client = this.requireClient()
    let turnId = this.store.getState().chats[this.key(threadId)]?.runtime?.activeTurnId
    if (!turnId) turnId = (await client.request('thread/read', { threadId })).thread.runtime.activeTurnId
    if (turnId) await client.request('turn/interrupt', { threadId, turnId })
  }

  decide(threadId: string, requestId: string, decision: ApprovalDecision): void {
    this.release(threadId, requestId, { decision: APPROVAL_DECISIONS[decision] })
  }

  answer(threadId: string, requestId: string, answers: Record<string, string[]>): void {
    const mapped = Object.fromEntries(Object.entries(answers).map(([id, values]) => [id, { answers: values }]))
    this.release(threadId, requestId, { answers: mapped })
  }

  private release(threadId: string, requestId: string, result: object): void {
    const resolve = this.held.get(requestId)
    if (!resolve) return
    this.held.delete(requestId)
    resolve(result)
    this.store.dispatch({ type: 'pendingRemoved', key: this.key(threadId), requestId })
  }
}
