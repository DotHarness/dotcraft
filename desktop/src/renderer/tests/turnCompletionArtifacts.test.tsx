import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { translate } from '../../shared/locales'
import { LocaleProvider } from '../contexts/LocaleContext'
import { TurnArtifacts } from '../components/conversation/TurnArtifacts'
import { TurnCompletionSummary } from '../components/conversation/TurnCompletionSummary'
import { ConfirmDialogHost } from '../components/ui/ConfirmDialog'
import { useConversationStore } from '../stores/conversationStore'
import { useToastStore } from '../stores/toastStore'
import { useViewerTabStore } from '../stores/viewerTabStore'
import { useUIStore } from '../stores/uiStore'
import type { FileDiff } from '../types/toolCall'
import type { TurnFileChange } from '../types/turnDiff'
import { installDesktopApiMock } from './desktopApiMock'

const settingsGet = vi.fn()
const settingsSet = vi.fn()
const listEditors = vi.fn()
const launchEditor = vi.fn()
const classify = vi.fn()
const toViewerUrl = vi.fn()
const browserCreate = vi.fn()
const applyPatch = vi.fn()

function makeDiff(filePath: string, overrides: Partial<FileDiff> = {}): FileDiff {
  return {
    filePath,
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

function seedTurn(...filePaths: string[]): void {
  const files: TurnFileChange[] = filePaths.map((filePath) => ({
    key: `turn-1::${filePath}`,
    turnId: 'turn-1',
    diff: makeDiff(filePath),
    patchText: `diff --git a/${filePath} b/${filePath}`,
    truncated: false
  }))
  useConversationStore.setState({ turnDiffs: new Map([['turn-1', { turnId: 'turn-1', source: 'history', files }]]) })
}

function renderWithLocale(ui: JSX.Element): void {
  render(<LocaleProvider>{ui}</LocaleProvider>)
}

function renderSummary(): void {
  renderWithLocale(<><ConfirmDialogHost /><TurnCompletionSummary turnId="turn-1" /></>)
}

const statuses = (): string[] =>
  useConversationStore.getState().turnDiffs.get('turn-1')?.files.map((row) => row.diff.status) ?? []

const appliedPatches = (): Array<[string, { reverse: boolean }]> =>
  applyPatch.mock.calls.map(([, patchText, options]) => [patchText, options])

function resetStores(): void {
  useToastStore.setState({ toasts: [] })
  useConversationStore.getState().reset()
  useConversationStore.setState({
    workspacePath: 'F:/workspace'
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
    applyPatch.mockResolvedValue({ ok: true })
    installDesktopApiMock({
      settings: { get: settingsGet, set: settingsSet },
      git: { applyPatch },
      shell: { listEditors, launchLocalPathInEditor: launchEditor },
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

  it('does not render inline visualization HTML as a regular artifact', () => {
    seedTurn('.craft/visualizations/thread-test/chart.html', 'site/index.html')

    renderWithLocale(<TurnArtifacts turnId="turn-1" />)

    expect(screen.queryByText('chart.html')).not.toBeInTheDocument()
    expect(screen.getByText('index.html')).toBeInTheDocument()
  })

  it('opens Markdown artifact card bodies in the internal file viewer', async () => {
    seedTurn('README.md')

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
    seedTurn('site/index.html')

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

  it('undoes the turn through git newest first without confirming, then re-applies it', async () => {
    seedTurn('src/a.ts', 'src/b.ts')

    renderSummary()
    fireEvent.click(await screen.findByRole('button', { name: translate('en', 'turnChanges.undo') }))

    const reapply = await screen.findByRole('button', { name: translate('en', 'turnChanges.reapply') })
    await waitFor(() => expect(reapply).toBeEnabled())
    expect(appliedPatches()).toEqual([
      ['diff --git a/src/b.ts b/src/b.ts', { reverse: true }],
      ['diff --git a/src/a.ts b/src/a.ts', { reverse: true }]
    ])
    expect(applyPatch).toHaveBeenCalledWith('F:/workspace', expect.any(String), { reverse: true })
    expect(statuses()).toEqual(['reverted', 'reverted'])
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(useToastStore.getState().toasts.map((toast) => toast.type)).toEqual(['success'])

    applyPatch.mockClear()
    fireEvent.click(reapply)

    const undo = await screen.findByRole('button', { name: translate('en', 'turnChanges.undo') })
    await waitFor(() => expect(undo).toBeEnabled())
    expect(appliedPatches()).toEqual([
      ['diff --git a/src/a.ts b/src/a.ts', { reverse: false }],
      ['diff --git a/src/b.ts b/src/b.ts', { reverse: false }]
    ])
    expect(statuses()).toEqual(['written', 'written'])
  })

  it('explains that Undo needs a Git repository and leaves the files as they are', async () => {
    applyPatch.mockResolvedValue({ ok: false, code: 'not-git-repo', message: 'not a git repository' })
    seedTurn('src/a.ts', 'src/b.ts')

    renderSummary()
    fireEvent.click(await screen.findByRole('button', { name: translate('en', 'turnChanges.undo') }))

    expect(await screen.findByRole('dialog')).toBeInTheDocument()
    expect(applyPatch).toHaveBeenCalledTimes(1)
    expect(statuses()).toEqual(['written', 'written'])
    expect(screen.getByRole('button', { name: translate('en', 'turnChanges.undo') })).toBeEnabled()
  })

  it('lists at most three files and reveals the rest on request', async () => {
    seedTurn('src/a.ts', 'src/b.ts', 'src/c.ts', 'src/d.ts', 'src/e.ts')

    renderSummary()

    expect(screen.getAllByRole('listitem')).toHaveLength(3)
    const toggle = screen.getByRole('button', { name: translate('en', 'turnChanges.showMoreFiles.other', { count: 2 }) })
    expect(toggle).toHaveAttribute('aria-expanded', 'false')

    fireEvent.click(toggle)

    expect(screen.getAllByRole('listitem')).toHaveLength(5)
    expect(toggle).toHaveAttribute('aria-expanded', 'true')
  })

  it('opens the Changes panel at the first file from Review', () => {
    seedTurn('src/a.ts', 'src/b.ts')

    renderSummary()
    fireEvent.click(screen.getByRole('button', { name: translate('en', 'turnChanges.review') }))

    expect(useUIStore.getState().detailPanelPreferredVisible).toBe(true)
    expect(useUIStore.getState().selectedChangeKey).toBe('turn-1::src/a.ts')
  })
})
