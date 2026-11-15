import { act, fireEvent, render, screen, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { MarkdownRenderer } from '../components/conversation/MarkdownRenderer'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useConversationStore } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'
import { useLinkPreferencesStore } from '../stores/linkPreferencesStore'
import { useTransientOverlayStore } from '../stores/transientOverlayStore'
import { installDesktopApiMock } from './desktopApiMock'

const openExternal = vi.fn().mockResolvedValue(undefined)
const table = '| Item | Details |\n| --- | --- |\n| One | [Document](https://example.com/document) |'

beforeEach(() => {
  installDesktopApiMock({ shell: { openExternal } })
  openExternal.mockClear()
  useConversationStore.setState({ workspacePath: '/workspace', turns: [] })
  useThreadStore.setState({ activeThreadId: 'thread' })
  useLinkPreferencesStore.setState({ preferences: { web: 'systemBrowser', local: 'systemBrowser', favicons: false } })
})

function message(content: string): JSX.Element {
  return <LocaleProvider loadSettings={false}><MarkdownRenderer content={content} enableMermaid={false} /></LocaleProvider>
}

describe('expanded Markdown tables', () => {
  it('opens the selected table, keeps focus inside, and restores focus and native views on close', () => {
    render(message('| Other |\n| --- |\n| Unrelated |\n\n' + table))
    const opener = screen.getAllByRole('button', { name: 'Expand table' })[1]
    act(() => opener.focus())
    fireEvent.click(opener)
    const dialog = screen.getByRole('dialog')
    expect(within(dialog).getByRole('link').getAttribute('href')).toBe('https://example.com/document')
    expect(useTransientOverlayStore.getState().nativeViewBlockerCount).toBe(1)
    const close = within(dialog).getByRole('button', { name: 'Close' })
    expect(close).toHaveFocus()
    fireEvent.keyDown(close, { key: 'Tab', shiftKey: true })
    expect(close).not.toHaveFocus()
    expect(dialog).toContainElement(document.activeElement as HTMLElement)
    fireEvent.keyDown(document.activeElement!, { key: 'Tab' })
    expect(close).toHaveFocus()
    fireEvent.keyDown(close, { key: 'Escape' })
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(opener).toHaveFocus()
    expect(useTransientOverlayStore.getState().nativeViewBlockerCount).toBe(0)
  })

  it('keeps expanded content in sync as a reply adds rows', () => {
    const view = render(message(table))
    fireEvent.click(screen.getByRole('button', { name: 'Expand table' }))
    expect(within(screen.getByRole('dialog')).getAllByRole('row')).toHaveLength(2)
    view.rerender(message(table + '\n| Two | More details |'))
    expect(within(screen.getByRole('dialog')).getAllByRole('row')).toHaveLength(3)
  })

  it('preserves ordinary link opening and the explicit Close action', async () => {
    render(message(table))
    fireEvent.click(screen.getByRole('button', { name: 'Expand table' }))
    const dialog = screen.getByRole('dialog')
    await act(async () => { fireEvent.click(within(dialog).getByRole('link'), { detail: 1 }) })
    expect(openExternal).toHaveBeenCalledWith('https://example.com/document')
    fireEvent.click(within(dialog).getByRole('button', { name: 'Close' }))
    expect(screen.queryByRole('dialog')).toBeNull()
  })
})
