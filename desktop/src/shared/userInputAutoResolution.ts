export type UserInputAutoResolutionPhase =
  | 'waitingForInactivity'
  | 'scheduled'

export interface UserInputAutoResolutionState {
  threadId: string
  requestId: string
  phase: UserInputAutoResolutionPhase
  deadlineAt: number | null
}
