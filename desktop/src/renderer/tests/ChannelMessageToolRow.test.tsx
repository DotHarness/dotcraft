import { beforeEach, describe, expect, it } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ToolCallCard } from '../components/conversation/ToolCallCard'
import { useConversationStore } from '../stores/conversationStore'
import type { ConversationItem } from '../types/conversation'
import { coreToolFields } from './testToolPresentation'
import { installDesktopApiMock } from './desktopApiMock'

function sendCall(
  args: Record<string, unknown>,
  structuredResult: Record<string, unknown> | undefined,
  extra: Partial<ConversationItem> = {}
): ConversationItem {
  return {
    id: 'send-1',
    type: 'toolCall',
    status: 'completed',
    toolName: 'SendChannelMessage',
    ...coreToolFields('SendChannelMessage'),
    toolCallId: 'call-send-1',
    arguments: args,
    result: '',
    structuredResult,
    success: true,
    createdAt: '2026-10-18T10:00:00.000Z',
    ...extra
  }
}

const destination = { channelName: 'telegram', displayName: 'Telegram', address: 'chat:1', label: 'Ops' }

function renderRow(item: ConversationItem, turnRunning = false) {
  return render(<LocaleProvider loadSettings={false}><ToolCallCard threadId="thread" turnId="turn" item={item} turnRunning={turnRunning} /></LocaleProvider>)
}

describe('SendChannelMessage tool row', () => {
  beforeEach(() => {
    useConversationStore.getState().reset()
    installDesktopApiMock({ settings: { get: async () => ({ locale: 'en' }) } })
  })

  it('is a static row when nothing was sent to show', () => {
    renderRow(sendCall({}, { ...destination, parts: [] }))
    expect(screen.getByTestId('tool-row')).toHaveAttribute('data-expandable', 'false')
  })

  it('expands to the sent text and attachment names from the result', () => {
    renderRow(sendCall(
      { text: 'draft', attachments: [{ path: 'out/report.pdf' }] },
      { ...destination, text: 'Build is green.', parts: [{ kind: 'text' }, { kind: 'file', name: 'report.pdf' }] }
    ))
    const row = screen.getByRole('button', { expanded: false })
    expect(screen.queryByText('Build is green.')).toBeNull()
    fireEvent.click(row)
    expect(screen.getByRole('button', { expanded: true })).toBe(row)
    expect(screen.getByText('Build is green.')).toBeInTheDocument()
    expect(screen.getByText('report.pdf')).toBeInTheDocument()
  })

  it('falls back to the arguments while the call runs', () => {
    renderRow(sendCall({ text: 'On my way.' }, undefined, { status: 'started', success: undefined, result: undefined }), true)
    fireEvent.click(screen.getByRole('button', { expanded: false }))
    expect(screen.getByText('On my way.')).toBeInTheDocument()
  })

  it('keeps a failed call without a result on its own row with the error', () => {
    renderRow(sendCall({ text: 'Hi' }, undefined, {
      success: false,
      errorCode: 'invalidArguments',
      result: 'Nothing to send: text and attachments are both empty.'
    }))
    fireEvent.click(screen.getByRole('button', { expanded: false }))
    expect(screen.getByText('Nothing to send: text and attachments are both empty.')).toBeInTheDocument()
  })
})
