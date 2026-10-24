import { readAppServerErrorFields } from '../../shared/appServerError'

export interface WorktreeCleanupFailure {
  exitCode?: number
  outputTail: string
}

export function worktreeCleanupFailureOf(error: unknown): WorktreeCleanupFailure | null {
  const data = readAppServerErrorFields(error).data
  if (data?.code !== 'cleanupFailed') return null
  const params = data.params ?? {}
  return {
    ...(typeof params.exitCode === 'number' ? { exitCode: params.exitCode } : {}),
    outputTail: typeof params.outputTail === 'string' ? params.outputTail : ''
  }
}
