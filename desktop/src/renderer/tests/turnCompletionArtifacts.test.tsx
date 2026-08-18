import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { TurnArtifacts } from '../components/conversation/TurnArtifacts'
import { TurnCompletionSummary } from '../components/conversation/TurnCompletionSummary'
import { useConversationStore } from '../stores/conversationStore'
import { useViewerTabStore } from '../stores/viewerTabStore'
import { useUIStore } from '../stores/uiStore'
import type { FileDiff } from '../types/toolCall'
import { installDesktopApiMock } from './desktopApiMock'

const settingsGet = vi.fn()
const settingsSet = vi.fn()
const listEditors = vi.fn()
const launchEditor = vi.fn()
const classify = vi.fn()
const toViewerUrl = vi.fn()
const browserCreate = vi.fn()
const writeFile = vi.fn()
const deleteFile = vi.fn()

function makeDiff(filePath: string, overrides: Partial<FileDiff> = {}): FileDiff {
  return {
    filePath,
    turnId: 'turn-1',
    turnIds: ['turn-1'],
    additions: 1,
    deletions: 1,
    status: 'written',
    isNewFile: false,
    originalContent: 'old\n',
    currentContent: 'new\n',
    diffHunks: [
      {
        oldStart: 1,
        oldLines: 1,
        newStart: 1,
        newLines: 1,
        lines: [
          { type: 'remove', content: 'old' },
          { type: 'add', content: 'new' }
        ]
      }
    ],
    ...overrides
  }
}

function renderWithLocale(ui: JSX.Element): void {
  render(<LocaleProvider>{ui}</LocaleProvider>)
}

function resetStores(): void {
  useConversationStore.getState().reset()
  useConversationStore.setState({
    workspacePath: 'F:/workspace',
    changedFiles: new Map()
  })
  useViewerTabStore.setState({
    byThread: new Map(),
    currentThreadId: 'thread-1',
    currentWorkspacePath: 'F:/workspace'
  })
  useUIStore.setState({
    activeDetailTab: { kind: 'system', id: 'changes' },
    detailPanelPreferredVisible: false,
    detailPanelVisible: false
  })
}

describe('turn completion artifacts', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    resetStores()
    settingsGet.mockResolvedValue({ locale: 'en', lastOpenEditorId: 'explorer' })
    settingsSet.mockResolvedValue(undefined)
    listEditors.mockResolvedValue([{ id: 'explorer', labelKey: 'editors.explorer', iconKey: 'explorer' }])
    launchEditor.mockResolvedValue(undefined)
    classify.mockResolvedValue({ contentClass: 'text', mime: 'text/markdown', sizeBytes: 16 })
    toViewerUrl.mockResolvedValue({ url: 'dotcraft-viewer://workspace/F%3A/workspace/site/index.html' })
    browserCreate.mockResolvedValue({
      tabId: 'browser-tab',
      currentUrl: 'dotcraft-viewer://workspace/F%3A/workspace/site/index.html',
      title: 'index.html',
      canGoBack: false,
      canGoForward: false,
      loading: false
    })
    writeFile.mockResolvedValue(undefined)
    deleteFile.mockResolvedValue(undefined)
    installDesktopApiMock({
      settings: { get: settingsGet, set: settingsSet },
      shell: { listEditors, launchEditor },
      file: { writeFile, deleteFile },
      workspace: {
        viewer: {
          classify,
          toViewerUrl,
          browser: { create: browserCreate }
        }
      }
    })
    ;(window as Window & { __confirmDialog?: unknown }).__confirmDialog = undefined
  })

  it('renders Markdown and HTML artifact cards', async () => {
    useConversationStore.setState({
      changedFiles: new Map([
        ['README.md', makeDiff('README.md')],
        ['site/index.html', makeDiff('site/index.html')]
      ])
    })

    renderWithLocale(<TurnArtifacts turnId="turn-1" />)

    expect(screen.getByText('README.md')).toBeInTheDocument()
    expect(screen.getByText('Document · MD')).toBeInTheDocument()
    expect(screen.getByText('index.html')).toBeInTheDocument()
    expect(screen.getByText('Web page · HTML')).toBeInTheDocument()
    await waitFor(() => expect(screen.getByRole('button', { name: 'Choose how to open file' })).toBeEnabled())
  })

  it('does not render inline visualization HTML as a regular artifact', () => {
    useConversationStore.setState({
      changedFiles: new Map([
        [
          'visualization',
          makeDiff('.craft/visualizations/thread-test/chart.html')
        ],
        ['site/index.html', makeDiff('site/index.html')]
      ])
    })

    renderWithLocale(<TurnArtifacts turnId="turn-1" />)

    expect(screen.queryByText('chart.html')).not.toBeInTheDocument()
    expect(screen.getByText('index.html')).toBeInTheDocument()
  })

  it('opens Markdown artifact card bodies in the internal file viewer', async () => {
    useConversationStore.setState({
      changedFiles: new Map([['README.md', makeDiff('README.md')]])
    })

    renderWithLocale(<TurnArtifacts turnId="turn-1" />)
    fireEvent.click(screen.getByRole('button', { name: 'Open README.md in DotCraft viewer' }))

    await waitFor(() => {
      expect(classify).toHaveBeenCalledWith({ absolutePath: 'F:/workspace/README.md' })
    })
    const activeTab = useUIStore.getState().activeDetailTab
    expect(activeTab.kind).toBe('viewer')
    if (activeTab.kind === 'viewer') {
      const tab = useViewerTabStore.getState().getThreadState('thread-1').tabs.find((entry) => entry.id === activeTab.id)
      expect(tab).toMatchObject({
        kind: 'file',
        absolutePath: 'F:/workspace/README.md',
        relativePath: 'README.md',
        contentClass: 'text',
        sizeBytes: 16
      })
    }
    expect(useUIStore.getState().detailPanelVisible).toBe(true)
    expect(launchEditor).not.toHaveBeenCalled()
  })

  it('opens HTML artifacts in the internal browser', async () => {
    useConversationStore.setState({
      changedFiles: new Map([['site/index.html', makeDiff('site/index.html')]])
    })

    renderWithLocale(<TurnArtifacts turnId="turn-1" />)
    fireEvent.click(screen.getByRole('button', { name: 'Preview index.html in DotCraft browser' }))

    await waitFor(() => {
      expect(toViewerUrl).toHaveBeenCalledWith({ absolutePath: 'F:/workspace/site/index.html' })
      expect(browserCreate).toHaveBeenCalledWith(expect.objectContaining({
        workspacePath: 'F:/workspace',
        initialUrl: 'dotcraft-viewer://workspace/F%3A/workspace/site/index.html'
      }))
    })
  })

  it('expands turn file diffs inline and can undo written files', async () => {
    useConversationStore.setState({
      changedFiles: new Map([['src/App.tsx', makeDiff('src/App.tsx')]])
    })
    ;(window as Window & { __confirmDialog?: (opts: unknown) => Promise<boolean> }).__confirmDialog = vi.fn()
      .mockResolvedValue(true)

    renderWithLocale(<TurnCompletionSummary turnId="turn-1" />)

    fireEvent.click(screen.getAllByRole('button', { name: /src\/App\.tsx/ })[0]!)
    expect(screen.queryByTestId('file-result-header')).toBeNull()
    expect(screen.queryByText('@@ -1,1 +1,1 @@')).toBeNull()
    expect(screen.getByText('old')).toBeInTheDocument()
    expect(screen.getByText('new')).toBeInTheDocument()

    const undoButton = screen.getByRole('button', { name: 'Undo' })
    expect(undoButton).toHaveAttribute('data-variant', 'ghost')
    expect(undoButton).toHaveAttribute('data-size', 'sm')
    fireEvent.click(undoButton)
    await waitFor(() => {
      expect(writeFile).toHaveBeenCalledWith('F:/workspace/src/App.tsx', 'old\n')
    })
    expect(useConversationStore.getState().changedFiles.get('src/App.tsx')?.status).toBe('reverted')
  })
})
