export const PROJECT_CREATION_CONFIRM_REQUEST_CHANNEL = 'workspace:project-creation-confirm-request'
export const PROJECT_CREATION_CONFIRM_DISMISSED_CHANNEL = 'workspace:project-creation-confirm-dismissed'
export const PROJECT_CREATION_CONFIRM_RESPOND_CHANNEL = 'workspace:project-creation-confirm-respond'

export interface ProjectCreationConfirmRequest {
  requestId: string
  name: string
  folders: string[]
  createsNewFolder: boolean
}

export type ProjectCreationConfirmOutcome = 'confirmed' | 'canceled' | 'abandoned'
