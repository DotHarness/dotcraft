import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { EnvironmentDocument } from '../../shared/projectActions'
import { setUpProject } from '../components/projectActions/projectSetup'
import { useThreadStore } from '../stores/threadStore'
import { useViewerTabStore } from '../stores/viewerTabStore'
import { installDesktopApiMock } from './desktopApiMock'

const WORKSPACE = 'C:\\repo'
const WORKTREE = 'C:\\repo\\.craft\\worktrees\\feature'
const PATH = 'C:\\repo\\.craft\\environments\\environment.json'
const ID = '.craft/environments/environment.json'
const ENV = { DOTCRAFT_SOURCE_TREE_PATH: WORKTREE, DOTCRAFT_WORKTREE_PATH: WORKTREE }

const document: EnvironmentDocument = {
  name: 'repo',
  setup: { default: 'npm install' },
  cleanup: { default: '' },
  actions: []
}

function install(writeResult: unknown) {
  const api = {
    writeEnvironment: vi.fn().mockResolvedValue(writeResult),
    select: vi.fn().mockResolvedValue(undefined),
    selectWorktreeEnvironment: vi.fn().mockResolvedValue(undefined),
    prepareSetup: vi.fn().mockResolvedValue({ cwd: WORKTREE, command: 'npm install', env: ENV }),
    sendRequest: vi.fn().mockResolvedValue({ worktree: { path: WORKTREE, environmentPath: PATH } })
  }
  installDesktopApiMock({
    projectActions: {
      listProjectEnvironments: async () => ({ projectPath: WORKSPACE, environments: [], newEnvironmentPath: PATH }),
      list: async () => ({
        environments: [{ id: ID, name: 'repo', path: PATH, isDefault: true, actions: [], revision: 'r1' }],
        selectedEnvironmentId: ID
      }),
      writeEnvironment: api.writeEnvironment,
      select: api.select,
      selectWorktreeEnvironment: api.selectWorktreeEnvironment,
      prepareSetup: api.prepareSetup
    },
    appServer: { sendRequest: api.sendRequest }
  })
  return api
}

beforeEach(() => {
  useThreadStore.setState({
    activeThread: { id: 'thread-1', worktree: { path: WORKTREE, environmentPath: null } } as never,
    threadList: []
  })
  useViewerTabStore.setState({ byThread: new Map() })
})

describe('setUpProject', () => {
  it('creates the environment, selects it everywhere, and runs its setup in the thread worktree', async () => {
    const api = install({ ok: true, path: PATH, revision: 'r1' })

    await expect(setUpProject({ workspacePath: WORKSPACE, threadId: 'thread-1', document, t: (key) => key }))
      .resolves.toEqual({ ok: true })

    expect(api.writeEnvironment).toHaveBeenCalledWith({ projectPath: WORKSPACE, path: PATH, revision: null, document })
    expect(api.select).toHaveBeenCalledWith({ workspacePath: WORKSPACE, environmentId: ID })
    expect(api.selectWorktreeEnvironment).toHaveBeenCalledWith({ workspacePath: WORKSPACE, environmentId: ID })
    expect(api.sendRequest).toHaveBeenCalledWith('worktree/setEnvironment', { path: WORKTREE, environmentPath: PATH })
    expect(api.prepareSetup).toHaveBeenCalledWith({ workspacePath: WORKSPACE, environmentId: ID, worktreePath: WORKTREE })
    const terminal = useViewerTabStore.getState().getThreadState('thread-1').tabs.find((tab) => tab.kind === 'terminal')
    expect(terminal).toMatchObject({ cwd: WORKTREE, pendingRun: { command: 'npm install', env: ENV } })
  })

  it('selects and runs nothing when the file appeared on disk', async () => {
    const api = install({ ok: false, reason: 'conflict' })

    await expect(setUpProject({ workspacePath: WORKSPACE, threadId: 'thread-1', document, t: (key) => key }))
      .resolves.toEqual({ ok: false, reason: 'conflict' })

    expect(api.select).not.toHaveBeenCalled()
    expect(api.selectWorktreeEnvironment).not.toHaveBeenCalled()
    expect(api.prepareSetup).not.toHaveBeenCalled()
    expect(useViewerTabStore.getState().getThreadState('thread-1').tabs).toHaveLength(0)
  })
})
