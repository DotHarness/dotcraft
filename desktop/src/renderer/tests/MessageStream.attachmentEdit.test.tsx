import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { LocaleProvider } from '../contexts/LocaleContext'
import { MessageStream } from '../components/conversation/MessageStream'
import { useConnectionStore } from '../stores/connectionStore'
import { useConversationStore } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'
import type { InputPart } from '../types/conversation'
import { installDesktopApiMock } from './desktopApiMock'

const send = vi.fn()
const imageUrl = 'data:image/png;base64,aW1hZ2U='
const original: InputPart[] = [
  { type: 'text', text: 'Original' },
  { type: 'localImage', path: 'X:/workspace/.craft/attachments/images/chart.png', fileName: 'chart.png' },
  { type: 'fileRef', path: 'X:/workspace/.craft/attachments/report.pdf', displayPath: 'report.pdf' }
]
function openMessage(input = original) {
  const time = '2026-01-01T00:00:00Z'
  useConversationStore.setState({
    turns: [{
      id: 'turn-1', threadId: 'thread-1', status: 'completed', startedAt: time,
      items: [{ id: 'user-1', type: 'userMessage', status: 'completed', createdAt: time, nativeInputParts: input, text: 'Original' }]
    }],
    turnStatus: 'idle', activeTurnId: null
  })
  render(<LocaleProvider><MessageStream /></LocaleProvider>)
  fireEvent.click(screen.getByRole('button', { name: 'Edit message' }))
}

function submit(text = 'Edited') {
  fireEvent.change(screen.getByRole('textbox', { name: 'Edit message text' }), { target: { value: text } })
  fireEvent.click(screen.getByRole('button', { name: 'Send', exact: true }))
}

beforeEach(() => {
  vi.clearAllMocks()
  useConnectionStore.getState().reset()
  useThreadStore.getState().reset()
  useConversationStore.getState().reset()
  useConversationStore.getState().setWorkspacePath('X:/workspace')
  useThreadStore.setState({ activeThreadId: 'thread-1' })
  send.mockImplementation(async (method: string) => {
    if (method === 'thread/rollback' || method === 'thread/read') return { thread: { id: 'thread-1', turns: [] } }
    if (method === 'thread/turns/list') return { data: [] }
    if (method === 'turn/start') return { turn: { id: 'new-turn' } }
    return {}
  })
  installDesktopApiMock({
    settings: { get: async () => ({ locale: 'en' }) },
    appServer: { sendRequest: send, onNotificationRaw: () => () => undefined },
    workspace: { readImageAsDataUrl: async () => ({ dataUrl: imageUrl }) }
  })
})

afterEach(() => {
  vi.restoreAllMocks()
  act(() => useConnectionStore.getState().reset())
})

describe('inline edits with retained attachments', () => {
  it('resends original attachments with edited text after rolling back', async () => {
    openMessage()
    expect(screen.getByRole('textbox', { name: 'Edit message text' })).toHaveValue('Original')
    submit()
    await waitFor(() => expect(send).toHaveBeenCalledWith('turn/start', expect.anything(), undefined, undefined))
    const methods = send.mock.calls.map(([method]) => method)
    expect(methods.indexOf('thread/rollback')).toBeLessThan(methods.indexOf('turn/start'))
    expect(send.mock.calls.find(([method]) => method === 'turn/start')![1].input)
      .toEqual([{ type: 'text', text: 'Edited' }, ...original.slice(1)])
  })

  it('accepts empty text while retaining an image-only message and cancels without RPC', async () => {
    openMessage([{ type: 'image', url: imageUrl }])
    expect(screen.getByRole('textbox', { name: 'Edit message text' })).toHaveValue('')
    fireEvent.keyDown(screen.getByRole('textbox', { name: 'Edit message text' }), { key: 'Escape' })
    expect(send).not.toHaveBeenCalled()
    fireEvent.click(screen.getByRole('button', { name: 'Edit message' }))
    fireEvent.click(screen.getByRole('button', { name: 'Send', exact: true }))
    await waitFor(() => expect(send.mock.calls.some(([method]) => method === 'turn/start')).toBe(true))
    expect(send.mock.calls.find(([method]) => method === 'turn/start')![1].input)
      .toEqual([{ type: 'image', url: imageUrl }])
  })

  it('keeps original history and the edited draft when rollback fails', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => {})
    send.mockRejectedValueOnce(new Error('Rollback unavailable'))
    openMessage()
    submit('Keep my draft')
    await waitFor(() => expect(screen.getByRole('button', { name: 'Send', exact: true })).toBeEnabled())
    expect(screen.getByRole('textbox', { name: 'Edit message text' })).toHaveValue('Keep my draft')
    expect(useConversationStore.getState().turns[0].id).toBe('turn-1')
    expect(send.mock.calls.map(([method]) => method)).toEqual(['thread/rollback'])
  })

  it('retains attachments after send failure and retries without rolling back twice', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => {})
    const defaultSend = send.getMockImplementation()!
    let attempts = 0
    send.mockImplementation(async (method: string, ...args: unknown[]) => {
      if (method === 'turn/start' && ++attempts === 1) throw new Error('Offline')
      return defaultSend(method, ...args)
    })
    openMessage()
    submit('Retry with attachments')
    await waitFor(() => expect(attempts).toBe(1))
    expect(screen.getByRole('textbox', { name: 'Edit message text' })).toHaveValue('Retry with attachments')
    fireEvent.click(screen.getByRole('button', { name: 'Send', exact: true }))
    await waitFor(() => expect(attempts).toBe(2))
    expect(send.mock.calls.filter(([method]) => method === 'thread/rollback')).toHaveLength(1)
    const starts = send.mock.calls.filter(([method]) => method === 'turn/start')
    expect(starts[0][1].input).toEqual(starts[1][1].input)
  })

  it('finishes on the original connection without modifying a workspace opened during rollback', async () => {
    useConnectionStore.setState({ connectionKey: 'origin', connectionId: 'origin-lifetime', status: 'connected' })
    const scoped = vi.spyOn(window.api.appServer, 'sendConnectionRequest')
    const defaultSend = send.getMockImplementation()!
    let release!: (value: unknown) => void
    send.mockImplementation((method: string, ...args: unknown[]) =>
      method === 'thread/rollback' ? new Promise((resolve) => { release = resolve }) : defaultSend(method, ...args))
    openMessage()
    submit()
    await waitFor(() => expect(send.mock.calls.some(([method]) => method === 'thread/rollback')).toBe(true))
    const otherTurn = { ...useConversationStore.getState().turns[0], id: 'other-turn', threadId: 'other-thread' }
    act(() => {
      useConnectionStore.setState({ connectionKey: 'other', connectionId: 'other-lifetime' })
      useConversationStore.setState({ workspacePath: 'Y:/other', turns: [otherTurn] })
      useThreadStore.setState({ activeThreadId: 'other-thread' })
    })
    await act(async () => { release({ thread: { id: 'thread-1' } }) })
    await waitFor(() => expect(send.mock.calls.some(([method]) => method === 'turn/start')).toBe(true))
    const start = scoped.mock.calls.find((call) => call[1] === 'turn/start')!
    expect(start[0]).toBe('origin')
    expect(start[4]).toMatchObject({ connectionId: 'origin-lifetime' })
    expect(useConversationStore.getState().turns).toEqual([otherTurn])
    expect(send.mock.calls.some(([method]) => method === 'thread/read')).toBe(false)
  })
})
