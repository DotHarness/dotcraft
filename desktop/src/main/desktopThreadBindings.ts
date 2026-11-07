export interface DesktopBindingClient {
  sendRequest<T = unknown>(method: string, params?: unknown, timeoutMs?: number | null, attachmentOwner?: string, onResponse?: (result: T) => void, onError?: (error: unknown) => void): Promise<T>
}

interface BindingOptions {
  attachmentOwner?: string
  onResponse?: (result: unknown) => void
  onError?: (error: unknown) => void
  supportsDynamicToolRebind?: boolean
}

interface ConnectionBindings {
  bound: Set<string>
  helpers: Set<string>
  visualizations: Set<string>
  pending: Map<string, Promise<void>>
}

interface ToolDeclaration {
  withTools(params: unknown): unknown
  includesTools(params: unknown): boolean
}

export class DesktopThreadBindings {
  private readonly clients = new WeakMap<DesktopBindingClient, ConnectionBindings>()

  constructor(private readonly tools: ToolDeclaration) {}

  reset(client: DesktopBindingClient): void {
    this.clients.delete(client)
  }

  hasTools(client: DesktopBindingClient | null, threadId: string, options: BindingOptions): boolean {
    return client !== null && (options.supportsDynamicToolRebind === true || this.clients.get(client)?.bound.has(threadId) === true)
  }

  async sendRequest<T>(
    client: DesktopBindingClient,
    method: string,
    params: unknown,
    timeoutMs: number | null | undefined,
    options: BindingOptions
  ): Promise<T> {
    const state = this.forClient(client)
    const threadId = stringProperty(params, 'threadId')
    if (threadId && (method === 'turn/start' || method === 'turn/enqueue')) {
      await this.ensureTools(client, state, threadId, options)
    }
    if (threadId && method === 'visualization/view/open') {
      await this.ensureVisualization(client, state, threadId, options)
    }
    this.assertCurrent(client, state)

    if (method === 'thread/start' && isRecord(params) && params.ephemeral === true) {
      const apply = (result: T): void => {
        this.assertCurrent(client, state)
        const createdId = resultThreadId(result)
        if (createdId) state.helpers.add(createdId)
        options.onResponse?.(result)
      }
      const result = await request<T>(client, method, params, timeoutMs, options.attachmentOwner, options.onResponse ? apply : undefined, options.onError)
      if (!options.onResponse) apply(result)
      return result
    }

    const startsThread = method === 'thread/start' || method === 'thread/fork' || method === 'worktree/createAndStart'
    const nextParams = startsThread
      ? this.tools.withTools(params)
      : params
    const apply = (result: T): void => {
      this.assertCurrent(client, state)
      if (startsThread || method === 'thread/resume') {
        const id = resultThreadId(result) ?? (method === 'thread/resume' ? threadId : null)
        if (id) {
          state.visualizations.add(id)
          if (this.tools.includesTools(nextParams)) state.bound.add(id)
        }
      }
      options.onResponse?.(result)
    }
    const result = await request<T>(client, method, nextParams, timeoutMs, options.attachmentOwner, options.onResponse ? apply : undefined, options.onError)
    if (!options.onResponse) apply(result)
    return result
  }

  private async ensureTools(client: DesktopBindingClient, state: ConnectionBindings, threadId: string, options: BindingOptions): Promise<void> {
    if (options.supportsDynamicToolRebind !== true || state.bound.has(threadId) || state.helpers.has(threadId)) return
    const pending = state.pending.get(threadId)
    if (pending) {
      await pending
      this.assertCurrent(client, state)
      return this.ensureTools(client, state, threadId, options)
    }
    await this.bind(client, state, threadId, true, options.attachmentOwner)
  }

  private async ensureVisualization(client: DesktopBindingClient, state: ConnectionBindings, threadId: string, options: BindingOptions): Promise<void> {
    if (state.visualizations.has(threadId)) return
    const pending = state.pending.get(threadId)
    if (pending) {
      await pending
      this.assertCurrent(client, state)
      if (state.visualizations.has(threadId)) return
    }
    await this.bind(client, state, threadId, options.supportsDynamicToolRebind === true, options.attachmentOwner)
  }

  private async bind(client: DesktopBindingClient, state: ConnectionBindings, threadId: string, dynamicTools: boolean, attachmentOwner?: string): Promise<void> {
    this.assertCurrent(client, state)
    const params = dynamicTools ? this.tools.withTools({ threadId }) : { threadId }
    const binding = request(client, 'thread/resume', params, undefined, attachmentOwner).then(() => {
      this.assertCurrent(client, state)
      state.visualizations.add(threadId)
      if (dynamicTools) state.bound.add(threadId)
    })
    state.pending.set(threadId, binding)
    try {
      await binding
    } finally {
      if (state.pending.get(threadId) === binding) state.pending.delete(threadId)
    }
  }

  private forClient(client: DesktopBindingClient): ConnectionBindings {
    let state = this.clients.get(client)
    if (!state) {
      state = { bound: new Set(), helpers: new Set(), visualizations: new Set(), pending: new Map() }
      this.clients.set(client, state)
    }
    return state
  }

  private assertCurrent(client: DesktopBindingClient, state: ConnectionBindings): void {
    if (this.clients.get(client) !== state) throw new Error('The AppServer connection is no longer active.')
  }
}

function request<T>(client: DesktopBindingClient, method: string, params: unknown, timeoutMs?: number | null, owner?: string, onResponse?: (result: T) => void, onError?: (error: unknown) => void): Promise<T> {
  if (onResponse || onError) return client.sendRequest<T>(method, params, timeoutMs, owner, onResponse, onError)
  return owner === undefined ? client.sendRequest<T>(method, params, timeoutMs) : client.sendRequest<T>(method, params, timeoutMs, owner)
}

function resultThreadId(result: unknown): string | null {
  return isRecord(result) ? stringProperty(result.thread, 'id') : null
}

function stringProperty(value: unknown, key: string): string | null {
  if (!isRecord(value)) return null
  const property = value[key]
  return typeof property === 'string' && property.trim() !== '' ? property : null
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
}
