import { setThreadWorktreeEnvironment, threadWorktreeEnvironment } from '../conversation/summary/worktreeEnvironment'

export async function selectProjectEnvironment({ workspacePath, threadId, environment }: {
  workspacePath: string
  threadId: string
  environment: { id: string; path: string }
}): Promise<void> {
  await window.api.projectActions.select({ workspacePath, environmentId: environment.id })
  await window.api.projectActions.selectWorktreeEnvironment({ workspacePath, environmentId: environment.id })
  if (threadWorktreeEnvironment(threadId)) {
    await setThreadWorktreeEnvironment({ threadId, environmentPath: environment.path })
  }
}
