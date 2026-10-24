import { create, type StoreApi, type UseBoundStore } from 'zustand'
import { readAppServerErrorFields } from '../../shared/appServerError'

export const WORKTREE_CREATION_STEPS = ['createWorktree', 'copyFiles', 'setupEnvironment'] as const

export type WorktreeCreationStep = (typeof WORKTREE_CREATION_STEPS)[number]
export type WorktreeCreationStepStatus = 'pending' | 'started' | 'completed' | 'skipped' | 'failed'
export type WorktreeCreationPhase = 'creating' | 'cancelling' | 'failed' | 'retrying'

const MAX_OUTPUT_CHARS = 200_000

export interface WorktreeCreationEnvironment {
  id: string
  name: string
  path: string
}

export interface WorktreeSetupOutcome {
  status: string
  exitCode?: number
  outputTail: string
}

export interface WorktreeCreation {
  anchorId: string
  workspacePath: string
  environment: WorktreeCreationEnvironment | null
  operationId: string
  phase: WorktreeCreationPhase
  steps: Record<WorktreeCreationStep, WorktreeCreationStepStatus>
  output: string
  threadId: string | null
  setup: WorktreeSetupOutcome | null
  error: string | null
}

export interface WorktreeCreationResult<T> {
  thread?: T
  setup?: WorktreeSetupOutcome
}

export interface WorktreeCreationRequest<T extends { id: string }> {
  anchorId: string
  workspacePath: string
  environment: WorktreeCreationEnvironment | null
  create(operationId: string): Promise<WorktreeCreationResult<T>>
  createWithoutWorktree(): Promise<T>
}

export interface WorktreeCreationDeps {
  sendRequest(method: string, params: unknown, timeoutMs?: number | null): Promise<unknown>
  onNotification(listener: (method: string, params: unknown) => void): () => void
  newOperationId(): string
}

interface WorktreeCreationState {
  creations: Record<string, WorktreeCreation>
  run<T extends { id: string }>(request: WorktreeCreationRequest<T>): Promise<T>
  cancel(anchorId: string): Promise<void>
  createWithoutWorktree(anchorId: string): Promise<void>
  continueAnyway(anchorId: string): void
  retry(anchorId: string): Promise<void>
}

export class WorktreeCreationCancelledError extends Error {
  constructor() {
    super('Worktree creation was cancelled')
    this.name = 'WorktreeCreationCancelledError'
  }
}

export function isWorktreeCreationCancelled(error: unknown): boolean {
  return error instanceof WorktreeCreationCancelledError
}

function isCreationCancelledError(error: unknown): boolean {
  return readAppServerErrorFields(error).data?.code === 'creationCancelled'
}

interface Control {
  intent: 'cancel' | 'local' | null
  thread: { id: string } | null
  request: WorktreeCreationRequest<{ id: string }>
  resolve(thread: { id: string }): void
  reject(error: unknown): void
}

function initialSteps(): Record<WorktreeCreationStep, WorktreeCreationStepStatus> {
  return { createWorktree: 'pending', copyFiles: 'pending', setupEnvironment: 'pending' }
}

function isStep(value: unknown): value is WorktreeCreationStep {
  return typeof value === 'string' && (WORKTREE_CREATION_STEPS as readonly string[]).includes(value)
}

function isStepStatus(value: unknown): value is WorktreeCreationStepStatus {
  return value === 'started' || value === 'completed' || value === 'skipped' || value === 'failed'
}

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error)
}

export function createWorktreeCreationStore(deps: WorktreeCreationDeps): UseBoundStore<StoreApi<WorktreeCreationState>> {
  const controls = new Map<string, Control>()
  let unsubscribe: (() => void) | null = null

  return create<WorktreeCreationState>((set, get) => {
    const patch = (anchorId: string, update: (creation: WorktreeCreation) => Partial<WorktreeCreation>): void => {
      set((state) => {
        const creation = state.creations[anchorId]
        return creation ? { creations: { ...state.creations, [anchorId]: { ...creation, ...update(creation) } } } : {}
      })
    }

    const byOperation = (operationId: unknown): WorktreeCreation | undefined =>
      Object.values(get().creations).find((creation) => creation.operationId === operationId)

    const listen = (): void => {
      if (unsubscribe) return
      unsubscribe = deps.onNotification((method, raw) => {
        const params = (raw ?? {}) as { operationId?: unknown; step?: unknown; status?: unknown; delta?: unknown }
        const creation = byOperation(params.operationId)
        if (!creation) return
        if (method === 'worktree/createProgress' && isStep(params.step) && isStepStatus(params.status)) {
          const step = params.step
          const status = params.status
          patch(creation.anchorId, (current) => ({ steps: { ...current.steps, [step]: status } }))
        } else if (method === 'worktree/setupOutput' && typeof params.delta === 'string') {
          const delta = params.delta
          patch(creation.anchorId, (current) => ({ output: (current.output + delta).slice(-MAX_OUTPUT_CHARS) }))
        }
      })
    }

    const finish = (anchorId: string): Control | undefined => {
      const control = controls.get(anchorId)
      controls.delete(anchorId)
      set((state) => {
        const { [anchorId]: _removed, ...rest } = state.creations
        return { creations: rest }
      })
      if (controls.size === 0 && unsubscribe) {
        unsubscribe()
        unsubscribe = null
      }
      return control
    }

    const settleSetup = (anchorId: string, setup: WorktreeSetupOutcome | undefined, thread: { id: string }): void => {
      if (setup?.status === 'failed') {
        patch(anchorId, (current) => ({
          phase: 'failed',
          threadId: thread.id,
          setup,
          error: null,
          steps: { ...current.steps, setupEnvironment: 'failed' }
        }))
        return
      }
      finish(anchorId)?.resolve(thread)
    }

    const execute = async (anchorId: string, operationId: string): Promise<void> => {
      const control = controls.get(anchorId)
      if (!control) return
      try {
        const result = await control.request.create(operationId)
        const thread = result.thread
        if (!thread?.id) throw new Error('Worktree creation did not return a thread')
        control.thread = thread
        settleSetup(anchorId, result.setup, thread)
      } catch (error) {
        const intent = control.intent
        finish(anchorId)
        if (intent === 'local') {
          try {
            control.resolve(await control.request.createWithoutWorktree())
          } catch (localError) {
            control.reject(localError)
          }
        } else if (intent === 'cancel' || isCreationCancelledError(error)) {
          control.reject(new WorktreeCreationCancelledError())
        } else {
          control.reject(error)
        }
      }
    }

    const stop = async (anchorId: string, intent: 'cancel' | 'local'): Promise<void> => {
      const creation = get().creations[anchorId]
      const control = controls.get(anchorId)
      if (!creation || !control || creation.phase !== 'creating') return
      control.intent = intent
      patch(anchorId, () => ({ phase: 'cancelling' }))
      await deps.sendRequest('worktree/cancelCreate', { operationId: creation.operationId }).catch(() => undefined)
    }

    return {
      creations: {},

      run<T extends { id: string }>(request: WorktreeCreationRequest<T>): Promise<T> {
        if (controls.has(request.anchorId)) {
          return Promise.reject(new Error('A worktree is already being created here'))
        }
        const operationId = deps.newOperationId()
        listen()
        set((state) => ({
          creations: {
            ...state.creations,
            [request.anchorId]: {
              anchorId: request.anchorId,
              workspacePath: request.workspacePath,
              environment: request.environment,
              operationId,
              phase: 'creating',
              steps: initialSteps(),
              output: '',
              threadId: null,
              setup: null,
              error: null
            }
          }
        }))
        return new Promise<T>((resolve, reject) => {
          controls.set(request.anchorId, {
            intent: null,
            thread: null,
            request: request as unknown as WorktreeCreationRequest<{ id: string }>,
            resolve: resolve as (thread: { id: string }) => void,
            reject
          })
          void execute(request.anchorId, operationId)
        })
      },

      cancel(anchorId) {
        const creation = get().creations[anchorId]
        if (creation?.phase === 'retrying') {
          return deps.sendRequest('worktree/cancelCreate', { operationId: creation.operationId }).then(() => undefined, () => undefined)
        }
        return stop(anchorId, 'cancel')
      },

      createWithoutWorktree(anchorId) {
        return stop(anchorId, 'local')
      },

      continueAnyway(anchorId) {
        const creation = get().creations[anchorId]
        const control = controls.get(anchorId)
        if (creation?.phase !== 'failed' || !control?.thread) return
        const thread = control.thread
        finish(anchorId)
        control.resolve(thread)
      },

      async retry(anchorId) {
        const creation = get().creations[anchorId]
        const control = controls.get(anchorId)
        if (creation?.phase !== 'failed' || !creation.threadId || !control?.thread) return
        const operationId = deps.newOperationId()
        const thread = control.thread
        patch(anchorId, (current) => ({
          phase: 'retrying',
          operationId,
          output: '',
          setup: null,
          error: null,
          steps: { ...current.steps, setupEnvironment: 'started' }
        }))
        try {
          const result = await deps.sendRequest('worktree/runSetup', { threadId: creation.threadId, operationId }, null) as
            { setup?: WorktreeSetupOutcome }
          settleSetup(anchorId, result.setup ?? { status: 'failed', outputTail: '' }, thread)
        } catch (error) {
          patch(anchorId, (current) => ({
            phase: 'failed',
            error: errorMessage(error),
            steps: { ...current.steps, setupEnvironment: 'failed' }
          }))
        }
      }
    }
  })
}

export const useWorktreeCreationStore = createWorktreeCreationStore({
  sendRequest: (method, params, timeoutMs) => window.api.appServer.sendRequestRaw(method, params, timeoutMs),
  onNotification: (listener) => window.api.appServer.onNotificationRaw((notification) => {
    listener(notification.method, notification.params)
  }),
  newOperationId: () => `worktree-create-${crypto.randomUUID()}`
})
