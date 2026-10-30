import { beforeEach, describe, expect, it } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import { translate } from '../../shared/locales'
import { LocaleProvider } from '../contexts/LocaleContext'
import { MessageOriginLine } from '../components/conversation/MessageOriginMarkers'
import { SideChatMainMessageRow } from '../components/conversation/SideChatMainMessageRow'
import { useSideChatStore, type SideChat } from '../stores/sideChatStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import type { ConversationItem } from '../types/conversation'
import { installDesktopApiMock } from './desktopApiMock'

const chat: SideChat = {
  key: 'side-key',
  parentThreadId: 'thread-main',
  threadId: 'thread-side',
  tabId: 'tab-side',
  ordinal: 1,
  status: 'ready',
  turns: [],
  live: null,
  unread: false,
  interruptingTurnId: null,
  pendingApprovals: [],
  pendingUserInput: null
}

function renderMainMessageRow(): void {
  const item = {
    id: 'tool-1',
    type: 'toolCall',
    status: 'completed',
    toolCallId: 'call-1',
    toolName: 'SendMessage',
    arguments: { target: 'main', message: 'Use port 8080' },
    result: '{"status":"started"}',
    success: true,
    createdAt: '2026-10-30T10:00:00.000Z'
  } as ConversationItem
  render(
    <LocaleProvider>
      <SideChatMainMessageRow item={item} chat={chat} args={item.arguments} running={false} success locale="en" />
    </LocaleProvider>
  )
}

beforeEach(() => {
  installDesktopApiMock({ settings: { get: async () => ({ locale: 'en' }) } })
  useThreadStore.getState().reset()
  useSideChatStore.setState({ chats: new Map(), focusRequest: null })
})

describe('side chat messages to the main thread', () => {
  it('opens the sending side chat from the origin marker while its tab is open', () => {
    useSideChatStore.setState({ chats: new Map([[chat.key, chat]]) })
    render(<LocaleProvider><MessageOriginLine kind="sideChat" refId="thread-side" /></LocaleProvider>)

    fireEvent.click(screen.getByRole('button'))

    expect(useSideChatStore.getState().focusRequest?.key).toBe(chat.key)
    expect(useUIStore.getState().activeDetailTab).toMatchObject({ kind: 'viewer', id: chat.tabId })
  })

  it('shows a static origin marker once the side chat is gone', () => {
    render(<LocaleProvider><MessageOriginLine kind="sideChat" refId="thread-side" /></LocaleProvider>)

    expect(screen.queryByRole('button')).toBeNull()
  })

  it('leaves side chat full view when showing the main thread it is already beside', () => {
    useThreadStore.setState({ activeThreadId: 'thread-main' })
    useUIStore.setState({
      activeMainView: 'conversation',
      detailPanelVisible: true,
      activeDetailTab: { kind: 'viewer', id: chat.tabId },
      detailPanelFullView: true
    })
    renderMainMessageRow()

    fireEvent.click(screen.getByRole('button', { name: translate('en', 'sideChat.toolMain.openAria') }))

    expect(useUIStore.getState().detailPanelFullView).toBe(false)
  })

  it('shows the main thread from the side chat tool row', () => {
    renderMainMessageRow()

    fireEvent.click(screen.getByRole('button', { name: translate('en', 'sideChat.toolMain.openAria') }))

    expect(useThreadStore.getState().activeThreadId).toBe('thread-main')
    expect(useUIStore.getState().activeMainView).toBe('conversation')
  })
})
