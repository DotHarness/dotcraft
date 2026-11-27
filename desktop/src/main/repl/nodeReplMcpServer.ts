import { createServer, type IncomingMessage, type Server as HttpServer, type ServerResponse } from 'node:http'
import { randomBytes, randomUUID, timingSafeEqual } from 'node:crypto'
import type { BrowserWindow } from 'electron'
import { Server } from '@modelcontextprotocol/sdk/server/index.js'
import { StreamableHTTPServerTransport } from '@modelcontextprotocol/sdk/server/streamableHttp.js'
import { CallToolRequestSchema, ListToolsRequestSchema, type CallToolResult } from '@modelcontextprotocol/sdk/types.js'
import { browserUseManagers } from '../browserUseManagers'
import { computerUseManager, rememberAlwaysAllowedApp } from '../computerUse/runtime'
import { nodeReplManager, type NodeReplApprovalRequest, type NodeReplEvaluateResult, type NodeReplManager } from '../nodeReplManager'

export type NodeReplMcpSurface = 'browser' | 'chrome' | 'computer'

export interface NodeReplMcpWorkspaceBinding {
  workspacePath: string
  projectId: string
  getOwnerWindow: () => BrowserWindow | null
  isActive: () => boolean
  isComputerEnabled: () => boolean
  defaultSurface: () => NodeReplMcpSurface | undefined
  retainBrowserTurn: (threadId: string, turnId: string) => Promise<boolean>
  releaseBrowserTurn: (threadId: string, turnId: string) => Promise<void>
}

export interface NodeReplMcpRouteRegistration {
  routeId: string
  url: string
  token: string
  dispose: () => void
}

interface ActiveWorkspaceRoute {
  routeId: string
  token: string
  binding: NodeReplMcpWorkspaceBinding
}

interface ActiveMcpSession {
  routeId: string
  server: Server
  transport: StreamableHTTPServerTransport
}

interface ThreadBinding {
  routeId: string
  owner: BrowserWindow
  turnId?: string
  isSubagent?: boolean
  completedTurnIds: Set<string>
}

interface CallContextMeta {
  threadId?: string
  turnId?: string
  callId?: string
  workspacePath?: string
  isSubagent?: boolean
}

const MAX_BODY_BYTES = 2 * 1024 * 1024
// JS setTimeout caps delays at 2^31 - 1 ms, so use this instead of the MCP SDK's 60s default to keep approvals open until answered or cancelled.
const MAX_ELICITATION_TIMEOUT_MS = 2_147_483_000

const NODE_REPL_TOOL_DEFINITION = {
  name: 'NodeReplJs',
  description: 'Execute JavaScript in a persistent thread-scoped Node.js REPL for browser automation and Windows computer use.',
  inputSchema: {
    type: 'object' as const,
    properties: {
      title: {
        type: 'string',
        description: 'Short user-facing description of what this run does (under 80 characters).'
      },
      code: {
        type: 'string',
        description: 'JavaScript code to execute.'
      },
      timeoutMs: {
        type: 'integer',
        minimum: 1000,
        maximum: 120000,
        description: 'Optional evaluation timeout in milliseconds.'
      }
    },
    required: ['code'],
    additionalProperties: false
  },
  annotations: {
    readOnlyHint: false,
    destructiveHint: false,
    openWorldHint: false
  }
}

const TURN_ENDED_TOOL_DEFINITION = {
  name: 'turn_ended',
  description: 'Internal lifecycle hook handler that finalizes browser tabs and computer-use state when a turn ends.',
  inputSchema: {
    type: 'object' as const,
    properties: {},
    additionalProperties: false
  },
  _meta: {
    ui: {
      visibility: ['app']
    }
  }
}

export interface NodeReplMcpServerDeps {
  replManager?: Pick<NodeReplManager, 'evaluate' | 'cancel' | 'reset'>
  browserManagers?: Pick<typeof browserUseManagers, 'handleTurnNotification'>
  computerManager?: Pick<typeof computerUseManager, 'handleTurnEnded' | 'clearThreadApprovals'>
  rememberAlwaysAllowed?: typeof rememberAlwaysAllowedApp
}

export class NodeReplMcpServer {
  private httpServer: HttpServer | null = null
  private listeningPromise: Promise<number> | null = null
  private port: number | null = null
  private readonly routes = new Map<string, ActiveWorkspaceRoute>()
  private readonly sessions = new Map<string, ActiveMcpSession>()
  private readonly threads = new Map<string, ThreadBinding>()
  private readonly deps: NodeReplMcpServerDeps

  constructor(deps: NodeReplMcpServerDeps = {}) {
    this.deps = deps
  }

  private get replManager(): Pick<NodeReplManager, 'evaluate' | 'cancel' | 'reset'> {
    return this.deps.replManager ?? nodeReplManager
  }

  private get browserManagers(): Pick<typeof browserUseManagers, 'handleTurnNotification'> {
    return this.deps.browserManagers ?? browserUseManagers
  }

  private get computerManager(): Pick<typeof computerUseManager, 'handleTurnEnded' | 'clearThreadApprovals'> {
    return this.deps.computerManager ?? computerUseManager
  }

  private get rememberAlwaysAllowed(): typeof rememberAlwaysAllowedApp {
    return this.deps.rememberAlwaysAllowed ?? rememberAlwaysAllowedApp
  }

  async ensureStarted(): Promise<number> {
    if (this.port !== null) return this.port
    if (this.listeningPromise) return this.listeningPromise

    this.listeningPromise = new Promise<number>((resolve, reject) => {
      const server = createServer((req, res) => {
        void this.handleHttpRequest(req, res)
      })
      server.once('error', (error) => {
        this.listeningPromise = null
        reject(error)
      })
      server.listen(0, '127.0.0.1', () => {
        const address = server.address()
        if (!address || typeof address === 'string') {
          this.listeningPromise = null
          server.close()
          reject(new Error('Failed to bind NodeRepl MCP loopback port.'))
          return
        }
        this.httpServer = server
        this.port = address.port
        resolve(address.port)
      })
    })
    return this.listeningPromise
  }

  async registerWorkspace(binding: NodeReplMcpWorkspaceBinding): Promise<NodeReplMcpRouteRegistration> {
    const port = await this.ensureStarted()
    const routeId = randomBytes(16).toString('hex')
    const token = randomBytes(32).toString('hex')
    this.routes.set(routeId, { routeId, token, binding })
    return {
      routeId,
      url: `http://127.0.0.1:${port}/mcp/${routeId}`,
      token,
      dispose: () => this.unregisterWorkspace(routeId)
    }
  }

  unregisterWorkspace(routeId: string): void {
    this.routes.delete(routeId)
    for (const [sessionId, session] of [...this.sessions]) {
      if (session.routeId !== routeId) continue
      this.sessions.delete(sessionId)
      void session.transport.close().catch(() => {})
    }
    for (const [threadId, thread] of [...this.threads]) {
      if (thread.routeId !== routeId) continue
      if (thread.turnId) this.computerManager.handleTurnEnded(threadId, thread.turnId)
      this.computerManager.clearThreadApprovals(threadId)
      this.threads.delete(threadId)
      this.replManager.reset(threadId, { preserveBrowserTabs: !thread.isSubagent })
    }
  }

  ownsThread(routeId: string, threadId: string): boolean {
    return this.threads.get(threadId)?.routeId === routeId
  }

  handleThreadDeleted(routeId: string, threadId: string): void {
    const thread = this.threads.get(threadId)
    if (!thread || thread.routeId !== routeId) return
    if (thread.turnId) this.computerManager.handleTurnEnded(threadId, thread.turnId)
    this.computerManager.clearThreadApprovals(threadId)
    this.threads.delete(threadId)
    this.replManager.reset(threadId)
    const route = this.routes.get(routeId)
    if (thread.turnId && route) {
      void route.binding.releaseBrowserTurn(threadId, thread.turnId).catch(console.warn)
    }
  }

  handleRetainedTurnEnded(routeId: string, threadId: string, turnId: string): void {
    const thread = this.threads.get(threadId)
    if (!thread || thread.routeId !== routeId || thread.turnId !== turnId) return
    this.finalizeTurn(routeId, threadId, turnId, thread.isSubagent === true)
  }

  async dispose(): Promise<void> {
    for (const routeId of [...this.routes.keys()]) {
      this.unregisterWorkspace(routeId)
    }
    const server = this.httpServer
    this.httpServer = null
    this.port = null
    this.listeningPromise = null
    if (!server) return
    await new Promise<void>((resolve) => {
      server.close(() => resolve())
    })
  }

  private async handleHttpRequest(req: IncomingMessage, res: ServerResponse): Promise<void> {
    const urlPath = (req.url ?? '').split('?', 1)[0]
    const match = /^\/mcp\/([a-f0-9]+)$/.exec(urlPath)
    if (!match) {
      res.writeHead(404).end('Not Found')
      return
    }

    const route = this.routes.get(match[1])
    if (!route || !route.binding.isActive()) {
      res.writeHead(404).end('Route unavailable')
      return
    }

    if (!verifyBearerToken(req.headers.authorization, route.token)) {
      res.writeHead(401).end('Unauthorized')
      return
    }

    const sessionIdHeader = req.headers['mcp-session-id']
    const sessionId = typeof sessionIdHeader === 'string' && sessionIdHeader.trim() ? sessionIdHeader.trim() : undefined

    if (sessionId) {
      const existing = this.sessions.get(sessionId)
      if (!existing || existing.routeId !== route.routeId) {
        res.writeHead(404).end('Session not found')
        return
      }
      try {
        await existing.transport.handleRequest(req, res)
      } catch (error) {
        if (!res.headersSent) res.writeHead(500).end(error instanceof Error ? error.message : 'Internal error')
      }
      return
    }

    if (req.method !== 'POST') {
      res.writeHead(400).end('Missing mcp-session-id header')
      return
    }

    let parsedBody: unknown
    try {
      parsedBody = await readJsonBody(req)
    } catch (error) {
      res.writeHead(400).end(error instanceof Error ? error.message : 'Invalid JSON body')
      return
    }

    if (!isInitializeBody(parsedBody)) {
      res.writeHead(400).end('Initialization request required')
      return
    }

    const server = this.createMcpServer(route)
    const transport = new StreamableHTTPServerTransport({
      sessionIdGenerator: () => randomUUID(),
      onsessioninitialized: (initializedSessionId) => {
        this.sessions.set(initializedSessionId, { routeId: route.routeId, server, transport })
      }
    })
    transport.onclose = () => {
      if (transport.sessionId) this.sessions.delete(transport.sessionId)
    }

    try {
      await server.connect(transport)
      await transport.handleRequest(req, res, parsedBody)
    } catch (error) {
      if (transport.sessionId) this.sessions.delete(transport.sessionId)
      if (!res.headersSent) res.writeHead(500).end(error instanceof Error ? error.message : 'Internal error')
    }
  }

  private createMcpServer(route: ActiveWorkspaceRoute): Server {
    const server = new Server(
      { name: 'dotcraft-node-repl', version: '1.0.0' },
      { capabilities: { tools: {} } }
    )

    server.setRequestHandler(ListToolsRequestSchema, async () => ({
      tools: [NODE_REPL_TOOL_DEFINITION, TURN_ENDED_TOOL_DEFINITION]
    }))

    server.setRequestHandler(CallToolRequestSchema, async (request, extra) => {
      const meta = readRequestMeta(request.params, extra)
      const callContext = readCallContext(meta)
      const hookEvent = typeof meta['dotcraft/hookEvent'] === 'string' ? meta['dotcraft/hookEvent'] : undefined

      if (request.params.name === 'turn_ended') {
        const threadId = callContext.threadId
        if (threadId) {
          const dispose = callContext.isSubagent === true || hookEvent === 'SubagentStop'
          this.finalizeTurn(route.routeId, threadId, callContext.turnId, dispose)
        }
        return { content: [{ type: 'text', text: 'ok' }] }
      }

      if (request.params.name !== 'NodeReplJs') {
        return {
          content: [{ type: 'text', text: `Unknown tool: ${request.params.name}` }],
          isError: true
        }
      }

      return await this.handleEvaluate(route, server, request.params.arguments, callContext, extra)
    })

    return server
  }

  private async handleEvaluate(
    route: ActiveWorkspaceRoute,
    server: Server,
    rawArgs: unknown,
    callContext: CallContextMeta,
    extra: { requestId: string | number; signal: AbortSignal }
  ): Promise<CallToolResult> {
    const args = isRecord(rawArgs) ? rawArgs : {}
    if (typeof args.code !== 'string' || !args.code.trim()) {
      return { content: [{ type: 'text', text: 'NodeReplJs requires non-empty code.' }], isError: true }
    }
    const threadId = callContext.threadId
    if (!threadId) {
      return { content: [{ type: 'text', text: 'NodeReplJs requires dotcraft/callContext.threadId.' }], isError: true }
    }
    if (!route.binding.isActive()) {
      return { content: [{ type: 'text', text: 'Workspace connection is no longer active.' }], isError: true }
    }
    const owner = route.binding.getOwnerWindow()
    if (!owner || owner.isDestroyed()) {
      return { content: [{ type: 'text', text: 'Desktop window is not available for NodeReplJs.' }], isError: true }
    }

    let threadBinding: ThreadBinding
    try {
      threadBinding = this.bindThread(route.routeId, owner, threadId)
    } catch (error) {
      return {
        content: [{ type: 'text', text: error instanceof Error ? error.message : String(error) }],
        isError: true
      }
    }
    if (callContext.isSubagent) threadBinding.isSubagent = true

    const evaluationId = callContext.callId || randomUUID()
    const turnId = callContext.turnId
    threadBinding.turnId = turnId ?? evaluationId
    if (turnId) {
      threadBinding.completedTurnIds.delete(turnId)
      const retained = await route.binding.retainBrowserTurn(threadId, turnId)
      if (!retained) {
        return { content: [{ type: 'text', text: 'The Node REPL turn is no longer active.' }], isError: true }
      }
    }

    const onAbort = (): void => {
      this.replManager.cancel(threadId, evaluationId)
    }
    if (extra.signal.aborted) {
      onAbort()
      return { content: [{ type: 'text', text: 'NodeReplJs cancelled before it started.' }], isError: true }
    }
    extra.signal.addEventListener('abort', onAbort, { once: true })

    try {
      const result = await this.replManager.evaluate(owner, {
        threadId,
        turnId,
        evaluationId,
        browserSession: {
          protocolVersion: 1,
          sessionId: threadId,
          threadId,
          turnId: turnId ?? evaluationId,
          evaluationId,
          ...(callContext.isSubagent ? { isSubagent: true } : {})
        },
        code: args.code,
        timeoutMs: typeof args.timeoutMs === 'number' ? args.timeoutMs : undefined,
        workspacePath: callContext.workspacePath || route.binding.workspacePath,
        projectKey: route.binding.projectId,
        allowComputerUse: route.binding.isComputerEnabled(),
        requestApproval: (approval) => this.requestComputerApproval(route, server, approval, extra)
      })
      return formatEvaluateResult(result, route.binding.defaultSurface())
    } finally {
      extra.signal.removeEventListener('abort', onAbort)
    }
  }

  private async requestComputerApproval(
    route: ActiveWorkspaceRoute,
    server: Server,
    request: NodeReplApprovalRequest,
    extra: { requestId: string | number; signal: AbortSignal }
  ): Promise<boolean> {
    if (!route.binding.isActive() || extra.signal.aborted) return false
    const appLabel = request.targetLabel || request.target
    try {
      const response = await server.elicitInput(
        {
          mode: 'form',
          message: `Allow DotCraft to use ${appLabel}?`,
          requestedSchema: {
            type: 'object',
            properties: {
              decision: {
                type: 'string',
                title: 'Permission',
                oneOf: [
                  { const: 'acceptForSession', title: 'Allow in this chat' },
                  { const: 'acceptAlways', title: 'Always allow' }
                ]
              }
            },
            required: ['decision']
          },
          _meta: {
            approvalKind: 'computerUse',
            operation: request.operation,
            target: request.target,
            targetLabel: appLabel
          }
        },
        {
          relatedRequestId: extra.requestId,
          signal: extra.signal,
          timeout: MAX_ELICITATION_TIMEOUT_MS
        }
      )
      if (response.action !== 'accept') return false
      if (response.content?.decision === 'acceptAlways') {
        await this.rememberAlwaysAllowed({ id: request.target, displayName: appLabel })
      }
      return true
    } catch {
      return false
    }
  }

  private bindThread(routeId: string, owner: BrowserWindow, threadId: string): ThreadBinding {
    const existing = this.threads.get(threadId)
    if (existing && (existing.routeId !== routeId || existing.owner !== owner)) {
      throw new Error('Node REPL thread is already bound to another workspace or window.')
    }
    if (existing) return existing
    const created: ThreadBinding = { routeId, owner, completedTurnIds: new Set() }
    this.threads.set(threadId, created)
    return created
  }

  private finalizeTurn(routeId: string, threadId: string, turnId: string | undefined, dispose: boolean): void {
    const thread = this.threads.get(threadId)
    if (thread && thread.routeId !== routeId) return
    const effectiveTurnId = turnId?.trim() || thread?.turnId
    const shouldDispose = dispose || thread?.isSubagent === true
    const route = this.routes.get(routeId)
    if (shouldDispose) {
      if (effectiveTurnId) this.computerManager.handleTurnEnded(threadId, effectiveTurnId)
      this.computerManager.clearThreadApprovals(threadId)
      this.threads.delete(threadId)
      this.replManager.reset(threadId, { preserveBrowserTabs: false })
      if (effectiveTurnId && route) {
        void route.binding.releaseBrowserTurn(threadId, effectiveTurnId).catch(console.warn)
      }
      return
    }
    if (!thread || !effectiveTurnId) return
    if (thread.completedTurnIds.has(effectiveTurnId)) return
    thread.completedTurnIds.add(effectiveTurnId)
    this.computerManager.handleTurnEnded(threadId, effectiveTurnId)
    this.browserManagers.handleTurnNotification(
      'turn/completed',
      { threadId, turnId: effectiveTurnId },
      route?.binding.workspacePath
    )
    if (route) {
      void route.binding.releaseBrowserTurn(threadId, effectiveTurnId).catch(console.warn)
    }
  }
}

export const nodeReplMcpServer = new NodeReplMcpServer()

function formatEvaluateResult(
  result: NodeReplEvaluateResult,
  defaultSurface: NodeReplMcpSurface | undefined
): CallToolResult {
  const content: CallToolResult['content'] = []
  const textParts: string[] = []
  if (result.logs.length > 0) textParts.push(result.logs.join('\n'))
  const valueText = result.resultText ?? result.text
  if (valueText) textParts.push(valueText)
  if (result.error) textParts.push(result.error)
  if (textParts.length > 0) {
    content.push({ type: 'text', text: textParts.join('\n') })
  }
  for (const image of result.images) {
    if (!image.dataBase64 || !image.mediaType) continue
    content.push({ type: 'image', data: image.dataBase64, mimeType: image.mediaType })
  }
  if (content.length === 0) {
    content.push({ type: 'text', text: '' })
  }
  const surface = result.surface ?? defaultSurface
  return {
    content,
    ...(surface ? { structuredContent: { surface } } : {}),
    ...(result.error ? { isError: true } : {})
  }
}

function verifyBearerToken(authorization: string | undefined, expectedToken: string): boolean {
  if (!authorization || !authorization.startsWith('Bearer ')) return false
  const supplied = Buffer.from(authorization.slice('Bearer '.length))
  const expected = Buffer.from(expectedToken)
  return supplied.length === expected.length && timingSafeEqual(supplied, expected)
}

function readJsonBody(req: IncomingMessage): Promise<unknown> {
  return new Promise((resolve, reject) => {
    const chunks: Buffer[] = []
    let total = 0
    req.on('data', (chunk: Buffer) => {
      total += chunk.length
      if (total > MAX_BODY_BYTES) {
        req.destroy()
        reject(new Error('Request body too large'))
        return
      }
      chunks.push(chunk)
    })
    req.on('end', () => {
      try {
        const text = Buffer.concat(chunks).toString('utf8')
        resolve(text ? JSON.parse(text) : undefined)
      } catch {
        reject(new Error('Invalid JSON body'))
      }
    })
    req.on('error', reject)
  })
}

function isInitializeBody(body: unknown): boolean {
  if (Array.isArray(body)) return body.some((entry) => isRecord(entry) && entry.method === 'initialize')
  return isRecord(body) && body.method === 'initialize'
}

function readRequestMeta(
  params: Record<string, unknown>,
  extra: { _meta?: Record<string, unknown> }
): Record<string, unknown> {
  const fromParams = isRecord(params._meta) ? params._meta : undefined
  const fromExtra = isRecord(extra._meta) ? extra._meta : undefined
  return { ...(fromExtra ?? {}), ...(fromParams ?? {}) }
}

function readCallContext(meta: Record<string, unknown>): CallContextMeta {
  const raw = meta['dotcraft/callContext']
  if (!isRecord(raw)) return {}
  return {
    threadId: nonEmptyString(raw.threadId),
    turnId: nonEmptyString(raw.turnId),
    callId: nonEmptyString(raw.callId),
    workspacePath: nonEmptyString(raw.workspacePath),
    isSubagent: raw.isSubagent === true
  }
}

function nonEmptyString(value: unknown): string | undefined {
  return typeof value === 'string' && value.trim() ? value.trim() : undefined
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}
