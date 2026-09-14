interface BrowserSubscription {
  wanted: boolean
  intent: number
  turnId?: string
  ready?: Promise<void>
}

/** Keeps a browser turn's existing thread stream alive until its terminal notification. */
export class BrowserTurnSubscriptions {
  private readonly threads = new Map<string, BrowserSubscription>()

  constructor(private readonly rawRequest: (method: string, params: { threadId: string; replayRecent?: boolean }) => Promise<unknown>) {}

  async request<T>(method: string, params: unknown, send: () => Promise<T>): Promise<T> {
    const threadId = readThreadId(params)
    if (!threadId || (method !== 'thread/subscribe' && method !== 'thread/unsubscribe')) return send()
    const state = this.state(threadId)
    const intent = ++state.intent
    const previous = state.wanted
    state.wanted = method === 'thread/subscribe'
    if (!state.wanted && state.turnId) return {} as T
    try {
      const result = await send()
      if (state.intent === intent && !state.wanted && !state.turnId && this.threads.get(threadId) === state) {
        this.threads.delete(threadId)
      }
      return result
    } catch (error) {
      if (state.intent === intent) state.wanted = previous
      throw error
    }
  }

  retain(threadId: string, turnId: string): Promise<void> {
    const state = this.state(threadId)
    state.turnId = turnId
    if (!state.ready) {
      state.ready = this.rawRequest('thread/subscribe', { threadId, replayRecent: false }).then(() => undefined)
    }
    return state.ready
  }

  async release(threadId: string, turnId: string): Promise<void> {
    const state = this.threads.get(threadId)
    if (!state || state.turnId !== turnId) return
    state.turnId = undefined
    await state.ready?.catch(() => undefined)
    if (this.threads.get(threadId) !== state || state.turnId) return
    state.ready = undefined
    if (!state.wanted) {
      this.threads.delete(threadId)
      await this.rawRequest('thread/unsubscribe', { threadId })
    }
  }

  clear(): void { this.threads.clear() }

  private state(threadId: string): BrowserSubscription {
    let state = this.threads.get(threadId)
    if (!state) {
      state = { wanted: false, intent: 0 }
      this.threads.set(threadId, state)
    }
    return state
  }
}

function readThreadId(params: unknown): string | undefined {
  if (!params || typeof params !== 'object') return
  const threadId = (params as { threadId?: unknown }).threadId
  return typeof threadId === 'string' && threadId.length > 0 ? threadId : undefined
}
