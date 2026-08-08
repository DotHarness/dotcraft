export type OratorioProviderKind = 'local' | 'remote'

export interface OratorioServiceContext {
  provider: OratorioProviderKind
  workspacePath: string | null
  connected: boolean
  revision: number
}

export interface OratorioRequest {
  method: 'GET' | 'POST' | 'PUT' | 'PATCH'
  path: string
  body?: Record<string, unknown>
}

export interface OratorioResponse<T = unknown> {
  status: number
  data: T
}

export interface OratorioServiceEvent {
  type: 'context-changed' | 'data-changed' | 'board-event' | 'handoff-requested'
  revision: number
  event?: OratorioBoardEvent
  handoff?: OratorioHandoffRequest
}

export interface OratorioHandoffRequest {
  requestId: string
  operation: 'connect' | 'bind'
  appId: string
  workspacePath: string
  summary: string
}

export interface OratorioBoardEvent {
  type: string
  taskId?: string
  shortId?: string
  runId?: string
  taskStatus?: string
  microStatus?: string
  boardSortOrder?: number
  ts?: string
  payload?: { type?: string; status?: string; text?: string }
}

export interface OratorioApi {
  getContext(): Promise<OratorioServiceContext>
  request<T = unknown>(request: OratorioRequest): Promise<OratorioResponse<T>>
  retry(): Promise<OratorioServiceContext>
  getPendingHandoff(): Promise<OratorioHandoffRequest | null>
  resolveHandoff(requestId: string, approved: boolean): Promise<void>
  focusRun(runId: string | null): Promise<void>
  onEvent(callback: (event: OratorioServiceEvent) => void): () => void
}
