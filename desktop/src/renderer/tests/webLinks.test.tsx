// @vitest-environment jsdom
import { setRendererTestState } from './panelTestState'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen } from '@testing-library/react'
import { MarkdownRenderer } from '../components/conversation/MarkdownRenderer'
import { droppedWebLink } from '../components/conversation/composerLinkDrop'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useConversationStore } from '../stores/conversationStore'
import { useLinkPreferencesStore } from '../stores/linkPreferencesStore'
import { useThreadStore } from '../stores/threadStore'
import { useDetailPanelStore } from '../stores/detailPanelStore'
import { installDesktopApiMock } from './desktopApiMock'

const openExternal = vi.fn()
const clipboardWriteText = vi.fn()

beforeAll(() => {
  installDesktopApiMock({
    settings: { get: () => Promise.resolve({ locale: 'en' }), set: vi.fn() },
    shell: { openExternal },
    forge: {
      getWorkspaceRepo: () => Promise.resolve(null),
      getReferenceSummary: () => Promise.resolve({ status: 'error', error: 'unavailable' })
    }
  })
  Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText: clipboardWriteText } })
})

function renderMarkdown(content: string): void {
  render(<LocaleProvider><MarkdownRenderer content={content} /></LocaleProvider>)
}

function browserTabs(): Array<{ kind: string }> {
  return useDetailPanelStore.getState().getThreadState('thread-1').tabs
}

function link(name: string): HTMLElement {
  return screen.getByText(name).closest('a')!
}

describe('web links', () => {
  beforeEach(() => {
    openExternal.mockReset().mockResolvedValue(undefined)
    clipboardWriteText.mockReset().mockResolvedValue(undefined)
    useLinkPreferencesStore.setState({ preferences: { web: 'inAppBrowser', local: 'inAppBrowser', favicons: true } })
    useConversationStore.getState().reset()
    useConversationStore.setState({ workspacePath: 'F:/workspace' })
    useThreadStore.setState({ activeThreadId: 'thread-1' })
    useDetailPanelStore.setState({ byThread: new Map(), currentThreadId: 'thread-1', currentWorkspacePath: 'F:/workspace' })
    setRendererTestState({ activeDetailTab: { kind: 'system', id: 'changes' }, detailPanelPreferredVisible: false, detailPanelVisible: false })
  })

  it('opens web and local links where their preferences point', () => {
    useLinkPreferencesStore.setState({ preferences: { web: 'systemBrowser', local: 'inAppBrowser', favicons: true } })
    renderMarkdown('[site](https://example.com/docs) and [dev](http://localhost:5230/?page=about)')

    fireEvent.click(link('site'), { detail: 1 })
    expect(openExternal).toHaveBeenCalledWith('https://example.com/docs')
    expect(browserTabs()).toHaveLength(0)

    fireEvent.click(link('dev'), { detail: 1 })
    expect(openExternal).toHaveBeenCalledTimes(1)
    expect(browserTabs()).toHaveLength(1)
  })

  it('lets modifiers and middle-click override the preference', () => {
    renderMarkdown('[site](https://example.com/docs)')

    fireEvent.click(link('site'), { detail: 1, shiftKey: true })
    expect(openExternal).toHaveBeenCalledWith('https://example.com/docs')
    expect(browserTabs()).toHaveLength(0)

    fireEvent.click(link('site'), { detail: 1 })
    fireEvent.click(link('site'), { detail: 1, ctrlKey: true })
    fireEvent(link('site'), new MouseEvent('auxclick', { bubbles: true, cancelable: true, button: 1, detail: 1 }))
    expect(browserTabs()).toHaveLength(3)
  })

  it('falls back to the system browser without an active thread', () => {
    useThreadStore.setState({ activeThreadId: null })
    renderMarkdown('[site](https://example.com/docs)')
    fireEvent.click(link('site'), { detail: 1 })
    expect(openExternal).toHaveBeenCalledWith('https://example.com/docs')
  })

  it('offers destinations and copying from a web link context menu', () => {
    renderMarkdown('[site](https://example.com/docs)')

    fireEvent.contextMenu(link('site'), { clientX: 20, clientY: 20 })
    fireEvent.click(screen.getByRole('menuitem', { name: 'Default browser' }))
    expect(openExternal).toHaveBeenCalledWith('https://example.com/docs')
    expect(screen.queryByRole('menu')).toBeNull()

    fireEvent.contextMenu(link('site'), { clientX: 20, clientY: 20 })
    fireEvent.click(screen.getByRole('menuitem', { name: 'New browser tab' }))
    expect(browserTabs()).toHaveLength(1)

    fireEvent.contextMenu(link('site'), { clientX: 20, clientY: 20 })
    fireEvent.click(screen.getByRole('menuitem', { name: 'Copy link' }))
    expect(clipboardWriteText).toHaveBeenCalledWith('https://example.com/docs')
  })

  it('copies the short reference of a forge link and the address of a mail link', async () => {
    renderMarkdown('[PR](https://github.com/acme/tool/pull/12) and [mail](mailto:team@example.com)')
    await act(() => Promise.resolve())

    fireEvent.contextMenu(screen.getByText('PR').closest('a')!, { clientX: 20, clientY: 20 })
    fireEvent.click(screen.getByRole('menuitem', { name: 'Copy reference' }))
    expect(clipboardWriteText).toHaveBeenCalledWith('acme/tool#12')

    fireEvent.contextMenu(link('mail'), { clientX: 20, clientY: 20 })
    expect(screen.queryByRole('menuitem', { name: 'New browser tab' })).toBeNull()
    fireEvent.click(screen.getByRole('menuitem', { name: 'Copy address' }))
    expect(clipboardWriteText).toHaveBeenCalledWith('team@example.com')
  })

  it('requests site icons only for public hosts and only while enabled', () => {
    const iconSources = (): string[] => Array.from(document.querySelectorAll('a img')).map((img) => img.getAttribute('src') ?? '')
    renderMarkdown('[site](https://example.com/docs) [dev](http://localhost:5230/) [lan](http://192.168.1.4/) [mail](mailto:team@example.com)')
    expect(iconSources()).toHaveLength(1)
    expect(iconSources()[0]).toContain(encodeURIComponent('https://example.com'))

    act(() => { useLinkPreferencesStore.setState({ preferences: { web: 'inAppBrowser', local: 'inAppBrowser', favicons: false } }) })
    expect(iconSources()).toHaveLength(0)
  })

  it('reads a dragged web link but not dropped files', () => {
    const transfer = (types: string[], uriList: string) => ({ types, getData: (type: string) => (type === 'text/uri-list' ? uriList : '') })
    expect(droppedWebLink(transfer(['text/uri-list'], '# comment\r\nhttps://example.com/a'))).toBe('https://example.com/a')
    expect(droppedWebLink(transfer(['Files', 'text/uri-list'], 'https://example.com/a'))).toBeNull()
    expect(droppedWebLink(transfer(['text/uri-list'], 'file:///C:/secret'))).toBeNull()
  })
})
