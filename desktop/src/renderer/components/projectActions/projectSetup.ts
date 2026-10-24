import {
  projectSetupTerminalTabId,
  type EnvironmentDocument,
  type WriteEnvironmentResult
} from '../../../shared/projectActions'
import { showToast } from '../../stores/toastStore'
import { useUIStore } from '../../stores/uiStore'
import { useViewerTabStore } from '../../stores/viewerTabStore'
import { sameEnvironmentPath, threadWorktreeEnvironment } from '../conversation/summary/worktreeEnvironment'
import { selectProjectEnvironment } from './environmentSelection'

type Translate = (key: string, vars?: Record<string, string | number>) => string

export type SetUpProjectResult = { ok: true } | { ok: false; reason: 'conflict' | 'failed' }

async function runProjectSetup({ workspacePath, threadId, environmentId, t }: {
  workspacePath: string
  threadId: string
  environmentId: string
  t: Translate
}): Promise<void> {
  const worktreePath = threadWorktreeEnvironment(threadId)?.worktreePath
  const run = await window.api.projectActions.prepareSetup({
    workspacePath,
    environmentId,
    ...(worktreePath ? { worktreePath } : {})
  })
  if (!run) return
  const tabId = useViewerTabStore.getState().runInTerminal({
    threadId,
    tabId: projectSetupTerminalTabId(threadId),
    cwd: run.cwd,
    title: t('projectActions.setup.terminalTitle'),
    command: run.command,
    env: run.env
  })
  useUIStore.getState().setActiveViewerTab(tabId)
}

export async function setUpProject({ workspacePath, threadId, document, t }: {
  workspacePath: string
  threadId: string
  document: EnvironmentDocument
  t: Translate
}): Promise<SetUpProjectResult> {
  let written: WriteEnvironmentResult
  try {
    const { newEnvironmentPath } = await window.api.projectActions.listProjectEnvironments(workspacePath)
    written = await window.api.projectActions.writeEnvironment({
      projectPath: workspacePath,
      path: newEnvironmentPath,
      revision: null,
      document
    })
  } catch {
    written = { ok: false, reason: 'failed' }
  }
  if (!written.ok) return written

  const path = written.path
  try {
    const { environments } = await window.api.projectActions.list(workspacePath)
    const environment = environments.find((candidate) => sameEnvironmentPath(candidate.path, path))
    if (!environment) throw new Error(`Environment not found: ${path}`)
    await selectProjectEnvironment({ workspacePath, threadId, environment })
    await runProjectSetup({ workspacePath, threadId, environmentId: environment.id, t })
  } catch (error) {
    showToast({
      message: t('projectActions.setup.runFailed', { error: error instanceof Error ? error.message : String(error) }),
      type: 'error'
    })
  }
  return { ok: true }
}
