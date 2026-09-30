// @vitest-environment jsdom
import { describe, it, expect, beforeEach, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { createElement, Fragment } from 'react'
import { useUIStore } from '../stores/uiStore'
import { useThreadStore } from '../stores/threadStore'
import { useViewerTabStore } from '../stores/viewerTabStore'
import { useFileEditorStore } from '../stores/fileEditorStore'
import { LocaleProvider } from '../contexts/LocaleContext'
import { DetailPanel } from '../components/layout/DetailPanel'
import { ConfirmDialogHost } from '../components/ui/ConfirmDialog'
import { installDesktopApiMock } from './desktopApiMock'

vi.mock('../components/detail/ViewerTab', () => ({
  ViewerTab: () => null
}))

const THREAD = 'thread-1'
const ui = () => useUIStore.getState()
const viewerTabs = () => useViewerTabStore.getState().getThreadState(THREAD).tabs

let destroyBrowser: ReturnType<typeof vi.fn>
let disposeTerminal: ReturnType<typeof vi.fn>
let fileTabId: string

beforeEach(() => {
  destroyBrowser = vi.fn(async () => undefined)
  disposeTerminal = vi.fn(async () => undefined)
  installDesktopApiMock({
    settings: { get: async () => ({ locale: 'en' }) },
    platform: 'win32',
    workspace: {
      viewer: {
        browser: { destroy: destroyBrowser },
        terminal: { dispose: disposeTerminal }
      }
    }
  })
  useThreadStore.setState({ activeThreadId: THREAD })
  useViewerTabStore.setState({
    byThread: new Map(),
    currentThreadId: null,
    welcomeScopeId: null,
    currentWorkspacePath: null
  })
  useFileEditorStore.setState({ sessions: new Map() })
  const viewer = useViewerTabStore.getState()
  viewer.onThreadSwitched(THREAD)
  viewer.openBrowser({ threadId: THREAD, initialUrl: 'http://localhost:5173' })
  viewer.openTerminal({ threadId: THREAD, cwd: '/workspace' })
  fileTabId = viewer.openFile({
    threadId: THREAD,
    absolutePath: '/workspace/notes.md',
    relativePath: 'notes.md',
    contentClass: 'text'
  })
  useUIStore.setState({
    openSystemTabs: ['changes', 'plan'],
    activeDetailTab: { kind: 'system', id: 'changes' },
    detailPanelVisible: true,
    detailPanelPreferredVisible: true
  })
})

function renderPanel(): void {
  render(createElement(
    LocaleProvider,
    null,
    createElement(Fragment, null, createElement(DetailPanel, { workspacePath: '/workspace' }), createElement(ConfirmDialogHost))
  ))
}

async function openMenu(tabName: string): Promise<void> {
  fireEvent.contextMenu(await screen.findByRole('tab', { name: tabName }))
  await screen.findByRole('menu')
}

describe('detail panel tab context menu', () => {
  it('disables bulk actions that would close nothing', async () => {
    renderPanel()

    await openMenu('notes.md')
    expect(screen.getByRole('menuitem', { name: 'Close' })).toHaveProperty('disabled', false)
    expect(screen.getByRole('menuitem', { name: 'Close other tabs' })).toHaveProperty('disabled', false)
    expect(screen.getByRole('menuitem', { name: 'Close tabs to the right' })).toHaveProperty('disabled', true)
  })

  it('closes system and viewer tabs other than the target and activates it', async () => {
    renderPanel()

    await openMenu('Checks')
    fireEvent.click(screen.getByRole('menuitem', { name: 'Close other tabs' }))

    await waitFor(() => expect(ui().openSystemTabs).toEqual(['plan']))
    expect(viewerTabs()).toEqual([])
    expect(ui().activeDetailTab).toEqual({ kind: 'system', id: 'plan' })
    expect(ui().detailPanelVisible).toBe(true)
    expect(destroyBrowser).toHaveBeenCalledTimes(1)
    expect(disposeTerminal).toHaveBeenCalledTimes(1)

    await openMenu('Checks')
    expect(screen.getByRole('menuitem', { name: 'Close other tabs' })).toHaveProperty('disabled', true)
    expect(screen.getByRole('menuitem', { name: 'Close tabs to the right' })).toHaveProperty('disabled', true)
  })

  it('closes only the tabs to the right of the target', async () => {
    renderPanel()

    await openMenu('localhost:5173')
    fireEvent.click(screen.getByRole('menuitem', { name: 'Close tabs to the right' }))

    await waitFor(() => expect(viewerTabs().map((tab) => tab.kind)).toEqual(['browser']))
    expect(ui().openSystemTabs).toEqual(['changes', 'plan'])
    expect(ui().activeDetailTab).toEqual({ kind: 'system', id: 'changes' })
    expect(destroyBrowser).not.toHaveBeenCalled()
    expect(disposeTerminal).toHaveBeenCalledTimes(1)
  })

  it('keeps an unsaved file the user chooses to keep while closing the rest', async () => {
    useFileEditorStore.setState({ sessions: new Map([[fileTabId, {} as never]]) })
    renderPanel()

    await openMenu('Changes')
    fireEvent.click(screen.getByRole('menuitem', { name: 'Close other tabs' }))

    fireEvent.click(await screen.findByRole('button', { name: 'Continue viewing' }))

    await waitFor(() => expect(ui().openSystemTabs).toEqual(['changes']))
    expect(viewerTabs().map((tab) => tab.id)).toEqual([fileTabId])
    expect(ui().activeDetailTab).toEqual({ kind: 'system', id: 'changes' })
  })
})
