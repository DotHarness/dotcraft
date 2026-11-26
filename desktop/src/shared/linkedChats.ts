export type LinkedChatOutcome = 'completed' | 'failed' | 'stopped'
export type LinkedChatStatus = 'idle' | 'running' | 'waitingForInput' | LinkedChatOutcome
export type LinkedChatWait = 'approval' | 'input' | 'planConfirmation'

export interface LinkedChatSummary {
  threadId: string
  projectId: string
  projectName: string
  workspacePath: string
  title: string
  status: LinkedChatStatus
  waitingFor?: LinkedChatWait
  followed?: true
  createdAt: string
}
