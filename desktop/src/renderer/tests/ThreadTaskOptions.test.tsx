import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, expect, it, vi } from 'vitest'
import { ThreadTaskOptions, type ThreadTaskOptionsHandle } from '../components/conversation/threadTasks/ThreadTaskOptions'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useConnectionStore } from '../stores/connectionStore'
import { useGitStore } from '../stores/gitStore'
import { useThreadStore } from '../stores/threadStore'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import { installDesktopApiMock } from './desktopApiMock'

beforeEach(() => {
  useConnectionStore.getState().reset()
  useThreadStore.getState().reset()
  useWorkspaceProjectsStore.getState().reset()
  useGitStore.getState().reset()
})

it('reads task worktree options from the current primary instead of the state workspace', async () => {
  const anchor = 'X:/state'
  const primary = 'X:/source'
  const project = {
    kind: 'local' as const, path: anchor, name: 'sample',
    state: 'foreground' as const, running: true, loaded: true, threadCount: 0, threads: [], pinned: false
  }
  const listWorktreeEnvironments = vi.fn(async (path: string) => ({
    environments: [{ id: 'setup', name: 'Setup', path: `${path}/.craft/environments/setup.json`, isDefault: true }],
    selectedEnvironmentId: 'setup',
    unavailable: false
  }))
  installDesktopApiMock({
    initialLocale: 'en',
    git: {
      listBranches: async (path) => ({
        current: path === primary ? 'source-main' : 'main', detachedHead: null,
        branches: [{ name: path === primary ? 'source-main' : 'main', current: true }]
      })
    },
    projectActions: { listWorktreeEnvironments, watch: () => () => {} }
  })
  useConnectionStore.setState({ capabilities: { gitWorktrees: true } })
  useWorkspaceProjectsStore.setState({ projects: [project] })
  const optionsRef: { current: ThreadTaskOptionsHandle | null } = { current: null }
  render(
    <LocaleProvider loadSettings={false}>
      <ThreadTaskOptions workspacePath={anchor} optionsRef={optionsRef} />
    </LocaleProvider>
  )

  fireEvent.click(screen.getByRole('button', { name: 'Options' }))
  const local = await screen.findByRole('button', { name: 'Work locally' })
  await waitFor(() => expect(local).toBeEnabled())
  fireEvent.click(local)
  fireEvent.click(screen.getByRole('button', { name: 'New worktree' }))
  await waitFor(() => expect(listWorktreeEnvironments).toHaveBeenCalledWith(anchor))

  act(() => useWorkspaceProjectsStore.setState({
    projects: [{ ...project, primaryFolder: primary, secondaryFolders: [anchor] }]
  }))
  await screen.findByRole('button', { name: 'source-main', exact: true })
  expect(await optionsRef.current!.read()).toEqual({
    workLocation: 'worktree',
    approvalPolicy: 'prompt',
    baseRef: 'source-main',
    environmentPath: `${primary}/.craft/environments/setup.json`
  })
})
