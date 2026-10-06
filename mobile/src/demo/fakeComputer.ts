import type { RelayInfo } from '../core/gateway'
import { FakeFiles } from './fakeFiles'

export interface FakeItem {
  id: string
  turnId: string
  type: string
  status: string
  payload: Record<string, unknown>
  createdAt: string
  completedAt: string | null
}

export interface FakeTurn {
  id: string
  threadId: string
  status: 'running' | 'completed' | 'failed' | 'cancelled'
  startedAt: string
  completedAt?: string
  error?: string
}

export type FakePending =
  | { kind: 'approval'; requestId: string; approvalType: string; operation: string; target: string; reason: string }
  | {
      kind: 'question'
      requestId: string
      isBlocking: boolean
      questions: {
        id: string
        header: string
        question: string
        options: { label: string; description: string }[]
        isOther: boolean
        isSecret: boolean
      }[]
    }

export interface FakeThread {
  id: string
  displayName: string | null
  profileId: string | null
  createdAt: string
  lastActiveAt: string
  turns: FakeTurn[]
  items: FakeItem[]
  pending: FakePending[]
  stream: string | null
  continuation: string
  source?: 'user' | 'subagent'
  config?: Record<string, unknown>
  archived?: boolean
  planned?: boolean
  context?: number
}

export interface FakeModel {
  id: string
  isDefault?: boolean
  reasoning?: {
    supportsDisable: boolean
    supportedEfforts: { effort: string; label: string }[]
    defaultEffort: string
    supportedOutputs: string[]
    defaultOutput: string
  }
  speed?: { supportedModes: string[]; defaultMode: string }
}

export interface FakeProvider {
  id: string
  displayName: string
  authMethod?: string
  models: FakeModel[]
}

export interface FakeProject {
  id: string
  name: string
  running: boolean
  cantStart: boolean
  lastActiveAt: string
  path?: string
  threads: FakeThread[]
}

export interface FakeComputerSeed {
  id: string
  name: string
  version: string
  port: number
  fingerprint: string
  addresses: string[]
  projects: FakeProject[]
  pairingCodes?: string[]
  credentials?: Record<string, string>
  profiles?: { id: string; name: string }[]
  providers?: FakeProvider[]
  commands?: { name: string; description: string; category: 'builtin' | 'custom' }[]
  skills?: { name: string; description: string; enabled: boolean }[]
  files?: Record<string, string>
  tooLargeFiles?: string[]
  accountUsage?: Record<string, unknown>
}

export interface FakeSocketSink {
  open(): void
  message(text: string): void
  close(code: number, reason: string): void
  fail(code: string, message: string, status?: number): void
}

interface Connection {
  project: FakeProject
  sink: FakeSocketSink
  initialized: boolean
  subscribed: Set<string>
  held: Map<string | number, { threadId: string; requestId: string }>
}

type Outcome = { result?: unknown; error?: { code: number; message: string }; after?: () => void }

const CONTEXT_WINDOW = 400_000

export function sequenceId(prefix: string, sequence: number): string {
  return `${prefix}_${sequence.toString().padStart(3, '0')}`
}

type FakeInputPart = { type: string; text?: string; name?: string; rawText?: string; path?: string }

function inputText(input: unknown): string {
  return (input as FakeInputPart[])
    .map((part) => {
      if (part.type === 'text') return part.text ?? ''
      if (part.type === 'commandRef') return part.rawText ?? `/${part.name}`
      if (part.type === 'skillRef') return `$${part.name}`
      if (part.type === 'fileRef') return `@${part.path}`
      return ''
    })
    .join('')
}

export class FakeComputer {
  readonly id: string
  readonly name: string
  readonly version: string
  readonly port: number
  readonly addresses: string[]
  readonly projects: FakeProject[]
  certificate: string
  relay: RelayInfo | null = null
  gatewayOn = true
  reachable = true
  streamDelayMs = 85
  readonly calls: { method: string; params: Record<string, unknown> }[] = []
  readonly decisions: { requestId: string; decision: string }[] = []
  readonly answers: { requestId: string; answers: unknown }[] = []
  readonly removedDevices: string[] = []
  readonly failingMethods = new Set<string>()
  readonly files: FakeFiles
  private readonly credentials: Map<string, string>
  private readonly pairingCodes: Set<string>
  private readonly profiles: { id: string; name: string }[]
  private readonly providers: FakeProvider[]
  private readonly commands: NonNullable<FakeComputerSeed['commands']>
  private readonly skills: NonNullable<FakeComputerSeed['skills']>
  private readonly accountUsage: FakeComputerSeed['accountUsage']
  private readonly connections = new Map<string, Connection>()
  private readonly events = new Map<string, FakeSocketSink>()
  private readonly streams = new Map<string, ReturnType<typeof setTimeout>>()
  private counter = 0

  constructor(seed: FakeComputerSeed) {
    this.id = seed.id
    this.name = seed.name
    this.version = seed.version
    this.port = seed.port
    this.addresses = seed.addresses
    this.projects = seed.projects
    this.certificate = seed.fingerprint
    this.pairingCodes = new Set(seed.pairingCodes)
    this.credentials = new Map(Object.entries(seed.credentials ?? {}))
    this.profiles = seed.profiles ?? []
    this.providers = seed.providers ?? []
    this.commands = seed.commands ?? []
    this.skills = seed.skills ?? []
    this.accountUsage = seed.accountUsage
    this.files = new FakeFiles(this.projects.flatMap((project) => (project.path ? [project.path] : [])), seed.files)
    for (const path of seed.tooLargeFiles ?? []) this.files.tooLarge.add(path)
    for (const project of this.projects) for (const thread of project.threads) thread.config = { ...this.defaultConfig(thread.config?.providerId as string | undefined), ...thread.config }
  }

  private nextId(prefix: string): string {
    this.counter += 1
    return `${prefix}_${this.counter.toString(36)}`
  }

  private stamp(): string {
    return new Date().toISOString()
  }

  private deviceFor(authorization: string | undefined): string | undefined {
    return this.credentials.get(authorization?.replace(/^Bearer /, '') ?? '')
  }

  addPairingCode(code: string): void {
    this.pairingCodes.add(code)
  }

  private project(projectId: string): FakeProject | undefined {
    return this.projects.find((project) => project.id === projectId)
  }

  thread(threadId: string): { project: FakeProject; thread: FakeThread } {
    for (const project of this.projects) {
      const thread = project.threads.find((entry) => entry.id === threadId)
      if (thread) return { project, thread }
    }
    throw new Error(`Unknown thread ${threadId}`)
  }

  get connectionCount(): number {
    return this.connections.size + this.events.size
  }

  http(method: string, path: string, headers: Record<string, string>, body: string | undefined): { status: number; body: unknown } {
    const error = (status: number, code: string) => ({ status, body: { error: { code, message: code } } })
    const computer = { computerId: this.id, name: this.name, port: this.port, fingerprint: this.certificate, addresses: this.addresses }
    if (method === 'POST' && path === '/m/pair') {
      if (!this.pairingCodes.delete((JSON.parse(body ?? '{}') as { code: string }).code)) return error(400, 'pairingCodeInvalid')
      const credential = this.nextId('credential')
      const deviceId = this.nextId('dev')
      this.credentials.set(credential, deviceId)
      return { status: 200, body: { deviceId, credential, computer } }
    }
    const device = this.deviceFor(headers.Authorization)
    if (!device) return error(401, 'unauthorized')
    if (path === '/m/hello') return { status: 200, body: { ...computer, version: this.version, relay: this.relay } }
    if (path === '/m/projects') return { status: 200, body: { projects: this.projects.map(projectBody) } }
    if (path === '/m/device') {
      this.removedDevices.push(device)
      for (const [credential, id] of this.credentials) if (id === device) this.credentials.delete(credential)
      return { status: 204, body: null }
    }
    const project = this.project(/^\/m\/projects\/([^/]+)\/ensure$/.exec(path)?.[1] ?? '')
    if (!project) return error(404, 'projectNotFound')
    if (project.cantStart) return error(500, 'appServerStartFailed')
    this.startProject(project.id)
    return { status: 200, body: projectBody(project) }
  }

  socket(path: string, headers: Record<string, string>, sink: FakeSocketSink): string | null {
    if (!this.deviceFor(headers.Authorization)) {
      sink.fail('ERR_HTTP', 'unauthorized', 401)
      return null
    }
    const id = this.nextId('socket')
    if (path === '/m/events') {
      this.events.set(id, sink)
      sink.open()
      return id
    }
    const project = this.project(/^\/m\/projects\/([^/]+)\/appserver$/.exec(path)?.[1] ?? '')
    if (!project?.running) {
      sink.fail('ERR_HTTP', 'projectNotRunning', 409)
      return null
    }
    this.connections.set(id, { project, sink, initialized: false, subscribed: new Set(), held: new Map() })
    sink.open()
    return id
  }

  socketClosed(id: string): void {
    this.connections.delete(id)
    this.events.delete(id)
  }

  receive(id: string, text: string): void {
    const connection = this.connections.get(id)
    if (!connection) return
    const message = JSON.parse(text) as { id?: string | number; method?: string; params?: Record<string, unknown>; result?: unknown }
    if (message.method === 'initialized') {
      connection.initialized = true
    } else if (message.method) {
      const params = message.params ?? {}
      this.calls.push({ method: message.method, params })
      const { after, ...reply } = this.failingMethods.has(message.method)
        ? { error: { code: -32603, message: `${message.method} failed` } }
        : this.request(connection, message.method, params)
      this.write(connection, { jsonrpc: '2.0', id: message.id, ...reply })
      after?.()
    } else if (message.id !== undefined) {
      const held = connection.held.get(message.id)
      if (!held) return
      if (message.result) this.resolvePending(held.threadId, held.requestId, message.result as Record<string, unknown>)
      else this.dropPending(held.threadId, held.requestId)
    }
  }

  private write(connection: Connection, frame: Record<string, unknown>): void {
    connection.sink.message(JSON.stringify(frame))
  }

  private projectConnections(project: FakeProject): Connection[] {
    return [...this.connections.values()].filter((connection) => connection.project === project && connection.initialized)
  }

  private broadcast(project: FakeProject, method: string, params: Record<string, unknown>): void {
    for (const connection of this.projectConnections(project)) this.write(connection, { jsonrpc: '2.0', method, params })
  }

  private toSubscribers(project: FakeProject, threadId: string, method: string, params: Record<string, unknown>): void {
    for (const connection of this.projectConnections(project)) {
      if (connection.subscribed.has(threadId)) this.write(connection, { jsonrpc: '2.0', method, params })
    }
  }

  private activeTurn(thread: FakeThread): FakeTurn | null {
    const turn = thread.turns.at(-1)
    return turn?.status === 'running' ? turn : null
  }

  private summary(thread: FakeThread): Record<string, unknown> {
    const active = this.activeTurn(thread)
    return {
      id: thread.id,
      displayName: thread.displayName,
      createdAt: thread.createdAt,
      lastActiveAt: thread.lastActiveAt,
      runtime: {
        running: active !== null,
        busy: active !== null,
        waitingOnApproval: thread.pending.some((request) => request.kind === 'approval'),
        waitingOnInput: thread.pending.some((request) => request.kind === 'question'),
        waitingOnPlanConfirmation: active === null && thread.planned === true && thread.config?.mode === 'plan',
        activeTurnId: active?.id ?? null,
      },
    }
  }

  private threadBody(thread: FakeThread): Record<string, unknown> {
    return {
      ...this.summary(thread),
      workspacePath: this.thread(thread.id).project.path ?? '',
      configuration: { ...thread.config, agentProfileId: thread.profileId },
      ...(thread.context === undefined ? {} : { contextUsage: this.contextUsage(thread.context) }),
      metadata: {},
      ephemeral: false,
      source: { kind: thread.source ?? 'user' },
    }
  }

  private contextUsage(tokens: number): Record<string, unknown> {
    return { tokens, contextWindow: CONTEXT_WINDOW, percentLeft: Math.max(0, 1 - tokens / CONTEXT_WINDOW) }
  }

  private statusChanged(project: FakeProject, thread: FakeThread): void {
    thread.lastActiveAt = this.stamp()
    this.broadcast(project, 'thread/status/changed', { threadId: thread.id, runtime: this.summary(thread).runtime })
  }

  private request(connection: Connection, method: string, params: Record<string, unknown>): Outcome {
    const project = connection.project
    switch (method) {
      case 'initialize':
        return {
          result: {
            serverInfo: { name: 'dotcraft', version: this.version },
            capabilities: {
              agentProfileManagement: true,
              threadFork: true,
              configOverride: this.providers.length > 0,
              modelCatalogManagement: this.providers.length > 0,
              providerManagement: this.providers.length > 0,
              fileSystem: true,
              commandManagement: this.commands.length > 0,
              skillsManagement: this.skills.length > 0,
              authOpenAiUsage: this.accountUsage !== undefined,
            },
          },
        }
      case 'thread/list':
        return { result: { data: project.threads.filter((thread) => !thread.archived).map((thread) => this.summary(thread)) } }
      case 'agent/profiles/list':
        return { result: { profiles: this.profiles } }
      case 'provider/list':
        return { result: { providers: this.providers.map(({ id, displayName, authMethod }) => ({ id, displayName, authMethod: authMethod ?? 'apiKey' })) } }
      case 'auth/openai/usage':
        return this.accountUsage ? { result: this.accountUsage } : { error: { code: -32601, message: `Method not found: ${method}` } }
      case 'model/list': {
        const provider = this.providers.find((entry) => entry.id === (params.providerId ?? this.providers[0]?.id))
        if (!provider) return { result: { success: false, providerId: params.providerId ?? null, models: [], errorCode: 'ProviderNotFound' } }
        return { result: { success: true, providerId: provider.id, models: provider.models } }
      }
      case 'command/list':
        return {
          result: {
            commands: this.commands
              .filter((command) => params.includeBuiltins !== false || command.category === 'custom')
              .map((command) => ({ ...command, name: `/${command.name}`, aliases: [], descriptionKey: '', fallbackDescription: command.description, requiresAdmin: false })),
          },
        }
      case 'skills/list':
        return { result: { skills: this.skills.map((skill) => ({ ...skill, source: 'workspace', path: `${project.path ?? ''}/.craft/skills/${skill.name}/SKILL.md`, metadata: {} })) } }
      case 'thread/start': {
        const overlay = params.config as Record<string, unknown> | undefined
        const thread: FakeThread = {
          id: this.nextId('thread'),
          displayName: null,
          profileId: null,
          createdAt: this.stamp(),
          lastActiveAt: this.stamp(),
          turns: [],
          items: [],
          pending: [],
          stream: null,
          continuation: 'Picking up from here.',
          config: { ...this.defaultConfig(overlay?.providerId as string | undefined), ...overlay },
          context: 0,
        }
        project.threads.push(thread)
        return {
          result: { thread: this.threadBody(thread) },
          after: () => this.broadcast(project, 'thread/started', { thread: this.threadBody(thread) }),
        }
      }
    }
    const files = this.files.handle(method, params)
    if (files) return files
    const { thread } = this.thread(params.threadId as string)
    switch (method) {
      case 'thread/read':
        return { result: { thread: this.threadBody(thread) } }
      case 'thread/turns/list':
        return { result: { data: [...thread.turns].reverse() } }
      case 'thread/items/list':
        return { result: { data: [...thread.items].reverse().map((item) => ({ turnId: item.turnId, item })) } }
      case 'thread/subscribe':
        connection.subscribed.add(thread.id)
        return { result: {}, after: () => this.afterSubscribe(connection, project, thread) }
      case 'thread/unsubscribe':
        connection.subscribed.delete(thread.id)
        return { result: {} }
      case 'thread/rename':
        thread.displayName = params.displayName as string
        return { result: {}, after: () => this.broadcast(project, 'thread/renamed', { threadId: thread.id, displayName: thread.displayName }) }
      case 'thread/fork': {
        const id = this.nextId('thread')
        const fork: FakeThread = {
          ...thread,
          id,
          createdAt: this.stamp(),
          lastActiveAt: this.stamp(),
          turns: thread.turns.map((turn) => ({ ...turn, threadId: id, status: turn.status === 'running' ? 'cancelled' : turn.status })),
          items: thread.items.map((item) => ({ ...item, status: 'completed' })),
          pending: [],
          stream: null,
        }
        project.threads.push(fork)
        return { result: { thread: this.threadBody(fork) }, after: () => this.broadcast(project, 'thread/started', { thread: this.threadBody(fork) }) }
      }
      case 'thread/archive':
        thread.archived = true
        return { result: {}, after: () => this.broadcast(project, 'thread/archived', { threadId: thread.id }) }
      case 'thread/config/update': {
        const config = { ...(params.config as Record<string, unknown>) }
        delete config.agentProfileId
        thread.config = config
        return { result: {}, after: () => this.broadcast(project, 'thread/updated', { thread: this.threadBody(thread) }) }
      }
      case 'thread/mode/set':
        thread.config = { ...thread.config, mode: params.mode }
        return { result: {}, after: () => this.broadcast(project, 'thread/updated', { thread: this.threadBody(thread) }) }
      case 'turn/start': {
        if (this.activeTurn(thread)) return { error: { code: -32012, message: 'A turn is already running on this thread.' } }
        const turn: FakeTurn = { id: sequenceId('turn', thread.turns.length + 1), threadId: thread.id, status: 'running', startedAt: this.stamp() }
        thread.turns.push(turn)
        thread.planned = false
        return {
          result: { turn },
          after: () => {
            const user = this.addItem(thread, turn.id, 'userMessage', {
              text: inputText(params.input),
              nativeInputParts: params.input,
              clientUserMessageId: params.clientUserMessageId,
            })
            this.toSubscribers(project, thread.id, 'turn/started', { turn: { ...turn, items: [user] } })
            this.statusChanged(project, thread)
            const reply =
              thread.turns.length === 1 ? `Starting on ${this.name} in ${project.name}. I’ll read the project first and report back here.` : thread.continuation
            this.streamReply(project, thread, turn, reply, true)
          },
        }
      }
      case 'turn/steer': {
        const active = this.activeTurn(thread)
        if (!active || active.id !== params.expectedTurnId) return { error: { code: -32602, message: 'The active turn does not accept this input.' } }
        return {
          result: { turnId: active.id },
          after: () => {
            const item = this.addItem(thread, active.id, 'userMessage', {
              text: inputText(params.input),
              deliveryMode: 'guidance',
              clientUserMessageId: params.clientUserMessageId,
            })
            this.toSubscribers(project, thread.id, 'item/completed', { threadId: thread.id, turnId: active.id, item })
          },
        }
      }
      case 'turn/enqueue':
        return { result: {} }
      case 'turn/interrupt': {
        const active = this.activeTurn(thread)
        if (!active) return { error: { code: -32014, message: 'The turn is not running.' } }
        return { result: {}, after: () => this.finishTurn(project, thread, active, 'cancelled') }
      }
      default:
        return { error: { code: -32601, message: `Method not found: ${method}` } }
    }
  }

  private defaultConfig(providerId?: string): Record<string, unknown> {
    const provider = this.providers.find((entry) => entry.id === providerId) ?? this.providers[0]
    const model = provider?.models.find((entry) => entry.isDefault)
    if (!provider || !model) return {}
    return {
      providerId: provider.id,
      model: model.id,
      ...(model.reasoning ? { reasoning: { enabled: true, effort: model.reasoning.defaultEffort, output: model.reasoning.defaultOutput } } : {}),
      speed: 'standard',
    }
  }

  addItem(thread: FakeThread, turnId: string, type: string, payload: Record<string, unknown>, status = 'completed'): FakeItem {
    const item: FakeItem = {
      id: sequenceId('item', thread.items.filter((entry) => entry.turnId === turnId).length + 1),
      turnId,
      type,
      status,
      payload,
      createdAt: this.stamp(),
      completedAt: status === 'completed' ? this.stamp() : null,
    }
    thread.items.push(item)
    return item
  }

  private deliver(connection: Connection, thread: FakeThread, turnId: string, pending: FakePending): void {
    if ([...connection.held.values()].some((held) => held.requestId === pending.requestId)) return
    const id = this.nextId('rpc')
    connection.held.set(id, { threadId: thread.id, requestId: pending.requestId })
    const { kind, ...fields } = pending
    const method = kind === 'approval' ? 'item/approval/request' : 'item/tool/requestUserInput'
    this.write(connection, { jsonrpc: '2.0', id, method, params: { threadId: thread.id, turnId, ...fields } })
  }

  private afterSubscribe(connection: Connection, project: FakeProject, thread: FakeThread): void {
    const active = this.activeTurn(thread)
    const diff = active ? this.turnDiff(thread, active.id) : ''
    if (active && diff) this.write(connection, { jsonrpc: '2.0', method: 'turn/diff/updated', params: { threadId: thread.id, turnId: active.id, diff } })
    if (active) for (const pending of thread.pending) this.deliver(connection, thread, active.id, pending)
    if (active && thread.stream && thread.pending.length === 0) {
      const stream = thread.stream
      thread.stream = null
      this.streamReply(project, thread, active, stream, false)
    }
  }

  private turnDiff(thread: FakeThread, turnId: string): string {
    return thread.items
      .filter((item) => item.turnId === turnId && item.type === 'toolResult')
      .flatMap((item) => (item.payload.structuredContent as { changes?: { diff?: string }[] } | undefined)?.changes ?? [])
      .map((change) => change.diff ?? '')
      .join('')
  }

  ask(threadId: string, pending: FakePending): void {
    const { project, thread } = this.thread(threadId)
    const active = this.activeTurn(thread)
    if (!active) return
    thread.pending = [...thread.pending, pending]
    for (const connection of this.projectConnections(project)) {
      if (connection.subscribed.has(thread.id)) this.deliver(connection, thread, active.id, pending)
    }
    this.statusChanged(project, thread)
  }

  endTurn(threadId: string, status: 'completed' | 'cancelled' | 'failed'): void {
    const { project, thread } = this.thread(threadId)
    const active = this.activeTurn(thread)
    if (active) this.finishTurn(project, thread, active, status)
  }

  answerFromComputer(threadId: string, result: Record<string, unknown>): void {
    const pending = this.thread(threadId).thread.pending[0]
    if (pending) this.resolvePending(threadId, pending.requestId, result)
  }

  private forgetHeld(requestId: string): void {
    for (const connection of this.connections.values()) {
      for (const [id, held] of connection.held) if (held.requestId === requestId) connection.held.delete(id)
    }
  }

  private resolvePending(threadId: string, requestId: string, result: Record<string, unknown>): void {
    const { project, thread } = this.thread(threadId)
    const pending = thread.pending.find((request) => request.requestId === requestId)
    const active = this.activeTurn(thread)
    if (!pending || !active) return
    thread.pending = thread.pending.filter((request) => request !== pending)
    this.forgetHeld(requestId)
    let item: FakeItem
    let reply = thread.continuation
    if (pending.kind === 'approval') {
      const decision = String(result.decision)
      this.decisions.push({ requestId, decision })
      item = this.addItem(thread, active.id, 'approvalResponse', { requestId, approved: decision.startsWith('accept'), decision })
      if (decision === 'decline') reply = 'Understood, I won’t run that. I’ll look for a way to finish without it.'
    } else {
      this.answers.push({ requestId, answers: result.answers })
      item = this.addItem(thread, active.id, 'userInputResponse', { requestId, response: { answers: result.answers } })
    }
    const resolved = pending.kind === 'approval' ? 'item/approval/resolved' : 'item/tool/requestUserInput/resolved'
    for (const method of [resolved, 'item/completed']) this.toSubscribers(project, thread.id, method, { threadId: thread.id, turnId: active.id, item })
    this.statusChanged(project, thread)
    if (thread.pending.length === 0) this.streamReply(project, thread, active, reply, true)
  }

  private dropPending(threadId: string, requestId: string): void {
    const { project, thread } = this.thread(threadId)
    if (!thread.pending.some((request) => request.requestId === requestId)) return
    thread.pending = thread.pending.filter((request) => request.requestId !== requestId)
    this.forgetHeld(requestId)
    this.statusChanged(project, thread)
  }

  private completeItem(project: FakeProject, thread: FakeThread, item: FakeItem): void {
    item.status = 'completed'
    item.completedAt = this.stamp()
    this.toSubscribers(project, thread.id, 'item/completed', { threadId: thread.id, turnId: item.turnId, item })
  }

  private streamReply(project: FakeProject, thread: FakeThread, turn: FakeTurn, text: string, complete: boolean): void {
    const payload = { text: '' }
    const message = this.addItem(thread, turn.id, 'agentMessage', payload, 'started')
    this.toSubscribers(project, thread.id, 'item/started', { threadId: thread.id, turnId: turn.id, item: message })
    const pieces = text.split(/(\s+)/).filter(Boolean)
    const step = () => {
      if (this.activeTurn(thread) !== turn) return
      const delta = pieces.splice(0, 2).join('')
      payload.text += delta
      this.toSubscribers(project, thread.id, 'item/agentMessage/delta', { threadId: thread.id, turnId: turn.id, itemId: message.id, delta })
      if (pieces.length > 0) {
        this.streams.set(thread.id, setTimeout(step, this.streamDelayMs))
        return
      }
      this.streams.delete(thread.id)
      this.completeItem(project, thread, message)
      if (thread.context !== undefined) {
        thread.context += 1200 + text.length * 4
        this.toSubscribers(project, thread.id, 'item/usage/delta', {
          threadId: thread.id,
          turnId: turn.id,
          inputTokens: thread.context,
          outputTokens: text.length,
          contextUsage: this.contextUsage(thread.context),
        })
      }
      if (complete) this.finishTurn(project, thread, turn, 'completed')
    }
    this.streams.set(thread.id, setTimeout(step, this.streamDelayMs))
  }

  private finishTurn(project: FakeProject, thread: FakeThread, turn: FakeTurn, status: 'completed' | 'cancelled' | 'failed'): void {
    clearTimeout(this.streams.get(thread.id))
    this.streams.delete(thread.id)
    for (const item of thread.items) if (item.turnId === turn.id && item.status === 'started') this.completeItem(project, thread, item)
    for (const pending of thread.pending) this.forgetHeld(pending.requestId)
    thread.pending = []
    thread.stream = null
    turn.status = status
    turn.completedAt = this.stamp()
    thread.planned = status === 'completed' && thread.config?.mode === 'plan'
    this.toSubscribers(project, thread.id, `turn/${status}`, { turn })
    this.statusChanged(project, thread)
  }

  private sendEvent(type: string, projectId: string): void {
    for (const sink of this.events.values()) sink.message(JSON.stringify({ type, projectId }))
  }

  startProject(projectId: string): void {
    const project = this.project(projectId)
    if (!project || project.running) return
    project.running = true
    project.lastActiveAt = this.stamp()
    this.sendEvent('projectStarted', projectId)
  }

  stopProject(projectId: string): void {
    const project = this.project(projectId)
    if (!project?.running) return
    project.running = false
    project.lastActiveAt = this.stamp()
    for (const [id, connection] of this.connections) {
      if (connection.project !== project) continue
      this.connections.delete(id)
      connection.sink.close(1000, '')
    }
    this.sendEvent('projectStopped', projectId)
  }

  private closeAll(reason: string, code: number): void {
    for (const sink of this.events.values()) {
      sink.message(JSON.stringify({ type: reason }))
      sink.close(code, reason)
    }
    for (const { sink } of this.connections.values()) sink.close(code, reason)
    this.events.clear()
    this.connections.clear()
  }

  revokeAll(): void {
    this.credentials.clear()
    this.closeAll('deviceRevoked', 1008)
  }

  turnGatewayOff(): void {
    this.gatewayOn = false
    this.closeAll('gatewayOff', 1001)
  }

  dropConnections(): void {
    for (const sink of [...this.events.values(), ...[...this.connections.values()].map((connection) => connection.sink)]) {
      sink.fail('ERR_UNREACHABLE', 'The connection was lost.')
    }
    this.events.clear()
    this.connections.clear()
  }
}

function projectBody(project: FakeProject) {
  return { projectId: project.id, displayName: project.name, running: project.running, lastActiveAt: project.lastActiveAt }
}
