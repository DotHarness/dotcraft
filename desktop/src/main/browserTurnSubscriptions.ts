interface BrowserSubscription {
  threadId: string
  operations: number
  attached: boolean
  owners: Set<string>
  turnId?: string
  taskTurnId?: string
  currentTurnId?: string
  currentTurnActive?: boolean
  pending: Promise<unknown>
}

interface ResumeState {
  currentState: { turnId?: string; turnStatus?: string }
}

type ResponseHook<T> = (result: T) => void

export class BrowserTurnSubscriptions {
  private readonly threads = new Map<string, BrowserSubscription>()
  private viewGeneration = 0
  private connectionGeneration = 0

  constructor(
    private readonly rawRequest: (method: string, params: { threadId: string }, onResponse?: ResponseHook<unknown>) => Promise<unknown>,
    private readonly onTurnEnded: (threadId: string, turnId: string) => void = () => {}
  ) {}

  request<T>(method: string, params: unknown, send: (onResponse?: ResponseHook<T>) => Promise<T>, owner = 'view', onResponse?: ResponseHook<T>): Promise<T> {
    const threadId = readString(params, 'threadId')
    const generation = this.viewGeneration
    const connectionGeneration = this.connectionGeneration
    if (method === 'thread/unsubscribe' && threadId) {
      const state = this.threads.get(threadId)
      if (!state) { onResponse?.({} as T); return Promise.resolve({} as T) }
      return this.enqueue(state, async () => {
        state.owners.delete(owner)
        await this.detachUnused(threadId, state)
        onResponse?.({} as T)
        return {} as T
      })
    }
    if (method === 'thread/resume' && threadId) {
      const state = this.state(threadId)
      return this.enqueue(state, () => {
        const pin = state.turnId
        return send(result => {
          if (this.threads.get(threadId) !== state) throw new Error('The AppServer connection is no longer active.')
          this.acquired(state, owner, generation)
          this.snapshot(threadId, state, result as ResumeState, pin)
          onResponse?.(result)
          void this.enqueue(state, () => this.detachUnused(threadId, state)).catch(() => {})
        })
      })
    }
    if (['thread/start', 'thread/fork', 'worktree/createAndStart'].includes(method)) {
      return send(result => {
        if (connectionGeneration !== this.connectionGeneration) throw new Error('The AppServer connection is no longer active.')
        const id = readString(readObject(result, 'thread'), 'id')
        if (id) this.acquired(this.state(id), owner, generation)
        onResponse?.(result)
        if (id) {
          const state = this.state(id)
          void this.enqueue(state, () => this.detachUnused(id, state)).catch(() => {})
        }
      })
    }
    if (method === 'turn/start' && owner === 'task' && threadId) {
      return send(result => {
        const state = this.threads.get(threadId)
        const turnId = readString(readObject(result, 'turn'), 'id')
        if (state && turnId) {
          state.taskTurnId = turnId
          state.currentTurnId = turnId
          state.currentTurnActive = true
          state.owners.delete('task')
        }
        onResponse?.(result)
      }).catch(async error => {
        const state = this.threads.get(threadId)
        if (state) {
          state.owners.delete('task')
          await this.detachUnused(threadId, state)
        }
        throw error
      })
    }
    return send(onResponse)
  }

  async retain(threadId: string, turnId: string): Promise<boolean> {
    const state = this.state(threadId)
    state.turnId = turnId
    return this.enqueue(state, async () => {
      if (this.threads.get(threadId) !== state || state.turnId !== turnId) return false
      if (state.attached && state.currentTurnId === turnId && state.currentTurnActive) return true
      try {
        await this.rawRequest('thread/resume', { threadId }, result => {
          if (this.threads.get(threadId) !== state) return
          state.attached = true
          this.snapshot(threadId, state, result as ResumeState, turnId)
        })
        await this.detachUnused(threadId, state)
        return state.turnId === turnId
      } catch (error) {
        if (state.turnId === turnId) state.turnId = undefined
        throw error
      }
    })
  }

  release(threadId: string, turnId: string): Promise<void> {
    const state = this.threads.get(threadId)
    if (!state) return Promise.resolve()
    if (state.turnId === turnId) state.turnId = undefined
    if (state.taskTurnId === turnId) state.taskTurnId = undefined
    return this.enqueue(state, () => this.detachUnused(threadId, state))
  }

  async releaseViews(): Promise<void> {
    ++this.viewGeneration
    await Promise.all([...this.threads].map(([threadId, state]) => this.enqueue(state, async () => {
      for (const owner of state.owners) if (owner !== 'task' && owner !== 'warm') state.owners.delete(owner)
      await this.detachUnused(threadId, state)
    })))
  }

  get retained(): boolean {
    return [...this.threads.values()].some(state => this.used(state))
  }

  hasWarmThread(threadId: string): boolean {
    return this.threads.get(threadId)?.owners.has('warm') === true
  }

  clear(): void { ++this.viewGeneration; ++this.connectionGeneration; this.threads.clear() }

  observeTurn(threadId: string, turnId: string, terminal: boolean): void {
    const state = this.threads.get(threadId)
    if (!state) return
    if (terminal && state.currentTurnId && state.currentTurnId !== turnId) return
    state.currentTurnId = turnId
    state.currentTurnActive = !terminal
  }

  private snapshot(threadId: string, state: BrowserSubscription, result: ResumeState, pin?: string): void {
    state.currentTurnId = result.currentState.turnId
    state.currentTurnActive = result.currentState.turnStatus === 'inProgress'
    if (state.turnId && state.turnId === pin && (state.turnId !== state.currentTurnId || !state.currentTurnActive)) {
      const turnId = state.turnId
      state.turnId = undefined
      this.onTurnEnded(threadId, turnId)
    }
    if (state.taskTurnId && (state.taskTurnId !== state.currentTurnId || !state.currentTurnActive)) state.taskTurnId = undefined
  }

  private acquired(state: BrowserSubscription, owner: string, generation: number): void {
    state.attached = true
    if (owner === 'task' || owner === 'warm' || generation === this.viewGeneration) state.owners.add(owner)
  }

  private used(state: BrowserSubscription): boolean {
    return state.owners.size > 0 || state.turnId != null || state.taskTurnId != null
  }

  private async detachUnused(threadId: string, state: BrowserSubscription): Promise<void> {
    if (this.threads.get(threadId) !== state || this.used(state) || !state.attached) return
    await this.rawRequest('thread/unsubscribe', { threadId })
    state.attached = false
  }

  private enqueue<T>(state: BrowserSubscription, run: () => Promise<T>): Promise<T> {
    ++state.operations
    const result = state.pending.then(run).finally(() => {
      --state.operations
      if (state.operations === 0 && !state.attached && !this.used(state) && this.threads.get(state.threadId) === state) {
        this.threads.delete(state.threadId)
      }
    })
    state.pending = result.catch(() => undefined)
    return result
  }

  private state(threadId: string): BrowserSubscription {
    let state = this.threads.get(threadId)
    if (!state) {
      state = { threadId, operations: 0, attached: false, owners: new Set(), pending: Promise.resolve() }
      this.threads.set(threadId, state)
    }
    return state
  }
}

function readObject(value: unknown, key: string): unknown {
  return value && typeof value === 'object' ? (value as Record<string, unknown>)[key] : undefined
}

function readString(value: unknown, key: string): string | undefined {
  const candidate = readObject(value, key)
  return typeof candidate === 'string' && candidate.length > 0 ? candidate : undefined
}
