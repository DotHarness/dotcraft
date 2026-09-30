import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { WorkspaceExplorer } from '../components/detail/WorkspaceExplorer'
import { useConversationStore } from '../stores/conversationStore'
import { useViewerTabStore } from '../stores/viewerTabStore'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import { installDesktopApiMock } from './desktopApiMock'

const listDir = vi.fn()

function entry(dir: string, name: string, isDir = false) {
  return { name, relativePath: name, absolutePath: `${dir}/${name}`, isDir }
}

function setProjects(secondaryFolders?: string[]): void {
  useWorkspaceProjectsStore.setState({
    projects: [{
      kind: 'local',
      path: 'X:/work/app',
      name: 'app',
      state: 'foreground',
      running: true,
      loaded: true,
      threadCount: 0,
      threads: [],
      pinned: false,
      ...(secondaryFolders ? { secondaryFolders } : {})
    }]
  })
}

function renderExplorer(): void {
  render(<LocaleProvider><WorkspaceExplorer /></LocaleProvider>)
}

describe('WorkspaceExplorer roots', () => {
  beforeEach(() => {
    listDir.mockReset()
    listDir.mockImplementation(async ({ dirPath }: { dirPath: string }) => ({
      dirPath,
      entries: [entry(dirPath, dirPath.endsWith('shared') ? 'src' : 'specs', true)]
    }))
    installDesktopApiMock({
      settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }), set: vi.fn() },
      workspace: { viewer: { listDir } }
    })
    useConversationStore.setState({ workspacePath: 'X:/work/app' })
    useViewerTabStore.setState({ byThread: new Map(), currentThreadId: 'thread-1' })
  })

  it('hides the root picker for single-folder projects', async () => {
    setProjects()
    renderExplorer()
    expect(await screen.findByText('specs')).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Choose file tree root' })).toBeNull()
  })

  it('switches the tree to another Project folder', async () => {
    setProjects(['X:/work/shared'])
    renderExplorer()
    expect(await screen.findByText('specs')).toBeTruthy()

    fireEvent.click(screen.getByRole('button', { name: 'Choose file tree root' }))
    fireEvent.click(await screen.findByRole('menuitemradio', { name: /shared/ }))

    expect(await screen.findByText('src')).toBeTruthy()
    expect(listDir).toHaveBeenLastCalledWith({ dirPath: 'X:/work/shared' })
  })

  it('follows the active file into its folder', async () => {
    setProjects(['X:/work/shared'])
    useViewerTabStore.getState().openFile({
      threadId: 'thread-1',
      absolutePath: 'X:/work/shared/src/a.ts',
      relativePath: 'src/a.ts',
      contentClass: 'text'
    })
    renderExplorer()
    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Choose file tree root' }).textContent).toContain('shared')
    })
    expect(await screen.findByText('src')).toBeTruthy()
  })
})
