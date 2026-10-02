import type { DesktopPluginHost, DesktopPluginSurfaceComponent } from '@dotcraft/plugin'
import { act, render, screen } from '@testing-library/react'
import { useEffect, useRef, useState, type JSX } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import {
  ConversationAsideProvider,
  ConversationAsides,
  useConversationColumnShift
} from '../components/conversation/conversationAside/ConversationAside'
import {
  clearDesktopPluginRegistry,
  registerDesktopPluginSurface
} from '../plugins/desktopPluginRegistry'

const host = { plugin: { id: 'summary', version: '1.0.0', displayName: 'Summary' } } as DesktopPluginHost
const thread = { workspacePath: null, threadId: 'thread-1', busy: false }

let setPinned: (pinned: boolean) => void = () => {}

const PinningPanel: DesktopPluginSurfaceComponent<'conversation.aside.trailing'> = ({ context }) => {
  const [pinned, setState] = useState(false)
  setPinned = setState
  const { pin } = context
  useEffect(() => (pinned ? pin() : undefined), [pinned, pin])
  return <span data-testid="trailing-layout">{context.layout}</span>
}

function Stream(): JSX.Element {
  const scrollRef = useRef<HTMLDivElement>(null)
  return (
    <div style={{ position: 'relative' }}>
      <div ref={scrollRef} data-testid="stream" />
      <ConversationAsides scrollRef={scrollRef} />
    </div>
  )
}

function ColumnShift(): JSX.Element {
  return <output data-testid="column-shift">{useConversationColumnShift()}</output>
}

beforeEach(() => {
  clearDesktopPluginRegistry()
  vi.spyOn(HTMLElement.prototype, 'offsetWidth', 'get').mockImplementation(function (this: HTMLElement) {
    if (this.dataset.testid === 'stream') return 1168
    if (this.classList.contains('dc-conversation-asides__probe')) return 768
    return 0
  })
})

afterEach(() => {
  act(() => clearDesktopPluginRegistry())
  vi.restoreAllMocks()
})

describe('conversation asides', () => {
  it('moves the conversation column while a trailing pin is live in the shift tier', () => {
    registerDesktopPluginSurface('summary', host, 'conversation.aside.trailing', 'add', PinningPanel)
    render(
      <ConversationAsideProvider thread={thread} style={{}}>
        <Stream />
        <ColumnShift />
      </ConversationAsideProvider>
    )

    expect(screen.getByTestId('trailing-layout')).toHaveTextContent('shift')
    expect(screen.getByTestId('column-shift')).toHaveTextContent('0')

    act(() => setPinned(true))
    expect(screen.getByTestId('column-shift')).toHaveTextContent('153')

    act(() => setPinned(false))
    expect(screen.getByTestId('column-shift')).toHaveTextContent('0')
  })

  it('keeps the column in place without a thread to seat contributions beside', () => {
    registerDesktopPluginSurface('summary', host, 'conversation.aside.trailing', 'add', PinningPanel)
    render(
      <ConversationAsideProvider thread={null} style={{}}>
        <Stream />
        <ColumnShift />
      </ConversationAsideProvider>
    )

    expect(screen.queryByTestId('trailing-layout')).toBeNull()
    expect(screen.getByTestId('column-shift')).toHaveTextContent('0')
  })
})
