import { worktreeCleanupFailureOf, type WorktreeCleanupFailure } from '../../../../utils/worktreeCleanupFailure'

export interface WorktreeRemoval {
  path: string
  discardChanges: boolean
  skipCleanup?: boolean
}

export type WorktreeRemovalOutcome =
  | { kind: 'removed' }
  | { kind: 'cleanupFailed'; removal: WorktreeRemoval; message: string; failure: WorktreeCleanupFailure }
  | { kind: 'failed'; message: string }

type Request = (method: 'worktree/remove', params: WorktreeRemoval) => Promise<unknown>

export async function removeWorktree(request: Request, removal: WorktreeRemoval): Promise<WorktreeRemovalOutcome> {
  try {
    await request('worktree/remove', removal)
    return { kind: 'removed' }
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error)
    const failure = worktreeCleanupFailureOf(error)
    return failure && !removal.skipCleanup
      ? { kind: 'cleanupFailed', removal, message, failure }
      : { kind: 'failed', message }
  }
}

export function removeAnyway(request: Request, outcome: Extract<WorktreeRemovalOutcome, { kind: 'cleanupFailed' }>): Promise<WorktreeRemovalOutcome> {
  return removeWorktree(request, { ...outcome.removal, skipCleanup: true })
}
