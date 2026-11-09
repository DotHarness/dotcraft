// @vitest-environment jsdom

import { beforeEach, describe, expect, it, vi } from 'vitest'
import { installDesktopApiMock } from './desktopApiMock'
import { compactThreadContext } from '../utils/manualCompaction'
import { useConversationStore } from '../stores/conversationStore'
import { useToastStore } from '../stores/toastStore'

vi.mock('../utils/threadHistory', () => ({
  readThreadHistoryHead: vi.fn(async () => ({ thread: null }))
}))

const sendRequest = vi.fn()
const t = (key: string) => key

describe('manual compaction notifications', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    useToastStore.setState({ toasts: [] })
    useConversationStore.setState({ maintenanceKind: null })
    installDesktopApiMock({ appServer: { sendRequest } })
  })

  it.each(['micro', 'partial', 'skipped'])('keeps %s outcomes quiet', async (outcome) => {
    let resolve!: (value: unknown) => void
    sendRequest.mockReturnValue(new Promise((done) => { resolve = done }))
    const pending = compactThreadContext('thread-1', t)
    expect(useToastStore.getState().toasts).toEqual([])

    resolve({ outcome })
    expect(await pending).toBe(true)
    expect(useToastStore.getState().toasts).toEqual([])
  })

  it('keeps active maintenance quiet after a client timeout', async () => {
    useConversationStore.setState({ maintenanceKind: 'compacting' })
    sendRequest.mockRejectedValue(new Error('Request timed out'))
    expect(await compactThreadContext('thread-1', t)).toBe(true)
    expect(useConversationStore.getState().maintenanceKind).toBe('compacting')
    expect(useToastStore.getState().toasts).toEqual([])
  })

  it.each(['outcome', 'request', 'timeout'])('reports %s failures', async (failure) => {
    if (failure === 'outcome') sendRequest.mockResolvedValue({ outcome: 'failed', message: 'rejected' })
    else sendRequest.mockRejectedValue(new Error(failure === 'timeout' ? 'Request timed out' : 'rejected'))

    await compactThreadContext('thread-1', t)

    expect(useToastStore.getState().toasts).toEqual([expect.objectContaining({ type: 'error' })])
  })
})
