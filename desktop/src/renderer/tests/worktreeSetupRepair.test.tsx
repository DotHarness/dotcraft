import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import type { WorktreeCreation } from '../stores/worktreeCreationStore'
import { startWorktreeSetupRepair } from '../utils/worktreeSetupRepair'
import { installDesktopApiMock } from './desktopApiMock'

const WORKSPACE = 'C:\\repo'
const ENVIRONMENT_PATH = 'C:\\repo\\.craft\\environments\\environment.json'

function failedCreation(): WorktreeCreation {
  return {
    anchorId: 'welcome-1',
    workspacePath: WORKSPACE,
    environment: { id: '.craft/environments/environment.json', name: 'repo', path: ENVIRONMENT_PATH },
    operationId: 'op-1',
    phase: 'failed',
    steps: { createWorktree: 'completed', copyFiles: 'completed', setupEnvironment: 'failed' },
    output: 'npm ERR! missing script: bootstrap',
    threadId: 'original-thread',
    setup: { status: 'failed', exitCode: 17, outputTail: 'npm ERR! missing script: bootstrap' },
    error: null
  }
}

beforeEach(() => {
  useThreadStore.setState({ activeThreadId: 'original-thread', threadList: [] })
  useUIStore.getState().setPendingWelcomeTurn(null)
})

describe('startWorktreeSetupRepair', () => {
  it('starts a new worktree thread without an environment and opens it with the failure as its first message', async () => {
    const sendRequestRaw = vi.fn().mockResolvedValue({
      thread: { id: 'repair-thread', displayName: 'Repair', workspacePath: WORKSPACE }
    })
    installDesktopApiMock({ appServer: { sendRequestRaw } })

    await startWorktreeSetupRepair(failedCreation(), 'Repair')

    expect(sendRequestRaw).toHaveBeenCalledTimes(1)
    const [method, params] = sendRequestRaw.mock.calls[0] as [string, Record<string, unknown>]
    expect(method).toBe('worktree/createAndStart')
    expect(params).not.toHaveProperty('environmentPath')
    expect(params).toMatchObject({ displayName: 'Repair', identity: { workspacePath: WORKSPACE } })

    const pending = useUIStore.getState().pendingWelcomeTurn
    expect(pending?.threadId).toBe('repair-thread')
    expect(pending?.text).toContain(ENVIRONMENT_PATH)
    expect(pending?.text).toContain('17')
    expect(pending?.text).toContain('npm ERR! missing script: bootstrap')
    expect(useThreadStore.getState().activeThreadId).toBe('repair-thread')
  })
})
