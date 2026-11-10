import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { RawServerRequestPayload, ServerRequestSettledPayload } from '../../shared/appServerBoundary'
import { McpElicitationHost } from '../components/mcp/McpElicitationHost'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useConnectionStore } from '../stores/connectionStore'
import { useThreadStore } from '../stores/threadStore'
import { applyThreadServerRequest, useThreadElicitationStore } from '../stores/threadServerRequests'
import { installDesktopApiMock } from './desktopApiMock'

function input(id: number, threadId = 'A', schema?: unknown): RawServerRequestPayload {
  return {
    bridgeId: `bridge-${id}`, wireRequestId: id, connectionId: 'connection', connectionKey: 'workspace',
    method: 'mcpServer/elicitation/request',
    params: {
      threadId, turnId: `turn-${threadId}`, serverName: 'tickets', mode: 'form',
      requestedSchema: schema ?? {
        type: 'object', properties: { project: { type: 'string', title: 'Project', default: `initial-${id}` } }, required: ['project']
      }
    }
  }
}

let settled!: (payload: ServerRequestSettledPayload) => void
let respond: ReturnType<typeof vi.fn>
beforeEach(() => {
  respond = vi.fn().mockResolvedValue(undefined)
  installDesktopApiMock({
    initialLocale: 'en',
    appServer: {
      sendServerResponse: respond,
      onServerRequestSettled: callback => { settled = callback; return () => undefined }
    }
  })
  useThreadElicitationStore.setState({ requests: [] })
  useThreadStore.setState({ activeThreadId: 'A' })
  useConnectionStore.setState({ connectionId: 'connection' })
})

function mount(): void {
  render(<LocaleProvider loadSettings={false}><McpElicitationHost /></LocaleProvider>)
}

describe('MCP input presentation', () => {
  it('keeps thread inputs queued and answers the original bridge after switching threads', async () => {
    applyThreadServerRequest(input(1, 'A'))
    applyThreadServerRequest(input(2, 'B'))
    mount()
    expect(screen.getByRole('textbox', { name: 'Project' })).toHaveValue('initial-1')
    fireEvent.change(screen.getByRole('textbox', { name: 'Project' }), { target: { value: 'A-project' } })
    act(() => useThreadStore.setState({ activeThreadId: 'B' }))
    expect(screen.getByRole('textbox', { name: 'Project' })).toHaveValue('initial-2')
    fireEvent.click(screen.getByRole('button', { name: 'Continue' }))
    await waitFor(() => expect(respond).toHaveBeenCalledWith('bridge-2', { action: 'accept', content: { project: 'initial-2' } }))
    await waitFor(() => expect(useThreadElicitationStore.getState().requests.map(request => request.bridgeId)).toEqual(['bridge-1']))
    act(() => useThreadStore.setState({ activeThreadId: 'A' }))
    expect(screen.getByRole('textbox', { name: 'Project' })).toHaveValue('initial-1')
    fireEvent.click(screen.getByRole('button', { name: 'Decline' }))
    await waitFor(() => expect(respond).toHaveBeenLastCalledWith('bridge-1', { action: 'decline' }))
  })

  it('withdraws exactly one input and reinitializes the next form with its own schema', () => {
    applyThreadServerRequest(input(1))
    applyThreadServerRequest(input(2, 'A', {
      type: 'object', properties: { count: { type: 'integer', title: 'Count', default: 7 } }, required: ['count']
    }))
    applyThreadServerRequest(input(3, 'B'))
    mount()
    fireEvent.change(screen.getByRole('textbox', { name: 'Project' }), { target: { value: 'stale' } })
    act(() => settled({ bridgeId: 'bridge-1', connectionId: 'connection', wireRequestId: 1 }))
    expect(screen.getByRole('spinbutton', { name: 'Count' })).toHaveValue(7)
    expect(useThreadElicitationStore.getState().requests.map(request => request.bridgeId)).toEqual(['bridge-2', 'bridge-3'])
    expect(respond).not.toHaveBeenCalled()
    act(() => settled({ bridgeId: 'bridge-2', connectionId: 'connection', wireRequestId: 2 }))
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(useThreadElicitationStore.getState().requests.map(request => request.bridgeId)).toEqual(['bridge-3'])
  })

  it('does not copy input values to the next queued request with the same fields', async () => {
    applyThreadServerRequest(input(1))
    applyThreadServerRequest(input(2))
    mount()
    fireEvent.change(screen.getByRole('textbox', { name: 'Project' }), { target: { value: 'first-answer' } })
    fireEvent.click(screen.getByRole('button', { name: 'Continue' }))
    await waitFor(() => expect(screen.getByRole('textbox', { name: 'Project' })).toHaveValue('initial-2'))
    expect(respond).toHaveBeenCalledWith('bridge-1', { action: 'accept', content: { project: 'first-answer' } })
  })

  it('waits for main-process settlement and prevents duplicate submissions', async () => {
    let complete!: () => void
    respond.mockReturnValue(new Promise<void>(resolve => { complete = resolve }))
    applyThreadServerRequest(input(1))
    mount()
    const accept = screen.getByRole('button', { name: 'Continue' })
    fireEvent.click(accept)
    fireEvent.click(accept)
    fireEvent.keyDown(document, { key: 'Escape' })
    expect(respond).toHaveBeenCalledTimes(1)
    expect(useThreadElicitationStore.getState().requests).toHaveLength(1)
    await act(async () => complete())
    expect(useThreadElicitationStore.getState().requests).toHaveLength(0)
  })

  it('rejects unowned inputs instead of inferring the active thread', () => {
    const unowned = input(1)
    unowned.params = { mode: 'form', serverName: 'tickets' }
    expect(applyThreadServerRequest(unowned)).toBe(true)
    expect(useThreadElicitationStore.getState().requests).toHaveLength(0)
    expect(respond).toHaveBeenCalledWith('bridge-1', { action: 'decline' })
  })
})
