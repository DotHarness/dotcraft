import { expect, it, vi } from 'vitest'
import { assertBrowserRestoreScope } from '../browserRestoreScope'
import type { WindowIpcContext } from '../ipcBridgeContracts'

vi.mock('../ipcBridgePaths', () => ({
  assertWorkspaceOrChatFolder: (requested: string, current: string) => {
    if (requested !== current) throw new Error('Workspace mismatch')
  }
}))

function context(): Parameters<typeof assertBrowserRestoreScope>[0] {
  return {
    workspacePath: '/project-b', owner: { webContents: { id: 1 } }, callbacks: {
      getSettings: () => ({}),
      getWorkspaceProjects: () => ({ foregroundWorkspacePath: '/project-b', foregroundProjectId: '/project-b', secondaryLimit: 8,
        projects: [{ path: '/project-a', projectId: '/project-a', kind: 'local' },
          { path: '/remote', projectId: 'remote:one', kind: 'remote' }] })
    }
  } as unknown as Pick<WindowIpcContext, 'workspacePath' | 'callbacks' | 'owner'>
}

it('admits late metadata restore for its known local project after another project became foreground', () => {
  expect(() => assertBrowserRestoreScope(context(), { workspacePath: '/project-a', projectKey: '/project-a' })).not.toThrow()
})
it('does not admit forged project roots, unknown projects or background remote resources', () => {
  for (const scope of [
    { workspacePath: '/secret', projectKey: '/project-a' },
    { workspacePath: '/project-a', projectKey: '/unknown' },
    { workspacePath: '/remote', projectKey: 'remote:one' },
    { workspacePath: '/secret', projectKey: '/project-b' }
  ]) expect(() => assertBrowserRestoreScope(context(), scope)).toThrow()
})
