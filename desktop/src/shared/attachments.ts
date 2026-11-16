import type { AppServerConnectionSource } from './appServerBoundary'

export const MAX_ATTACHMENT_BYTES = 10 * 1024 * 1024

export interface AttachmentReadParams {
  path: string
  source?: AppServerConnectionSource
  workspacePath?: string
}
