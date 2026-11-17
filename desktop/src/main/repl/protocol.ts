export interface ReplContext {
  threadId: string
  evaluationId: string
  generation: string
  workspacePath: string
  dotcraft: Record<string, unknown>
  browserSession: Record<string, unknown>
}
export interface ReplResult { resultText?: string; error?: string; logs: string[] }
export type ReplRequest =
  | { type: 'evaluate'; context: ReplContext; code: string }
  | { type: 'hostResult'; context: ReplContext; id: string; value?: unknown; error?: string }
export type ReplResponse =
  | { type: 'ready' }
  | { type: 'output'; context: ReplContext; text: string }
  | { type: 'result'; context: ReplContext; result: ReplResult }
  | { type: 'host'; context: ReplContext; id: string; method: string; value: unknown }
export interface ReplTransport {
  send(message: ReplResponse): void
  listen(handler: (message: ReplRequest) => void): void
}
