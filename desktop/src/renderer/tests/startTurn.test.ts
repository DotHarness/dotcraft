import { beforeEach, describe, expect, it, vi } from 'vitest'
import { startTurnWithOptimisticUI } from '../utils/startTurn'
import { useConversationStore } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'
import { installDesktopApiMock } from './desktopApiMock'

const sendRequest = vi.fn()
const image = { tempPath: 'C:\\temp\\image.png', dataUrl: 'data:image/png;base64,abc', fileName: 'image.png', mimeType: 'image/png' }
const file = { path: 'C:\\temp\\notes.txt', fileName: 'notes.txt' }

describe('startTurnWithOptimisticUI thread naming', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.stubGlobal('window', {})
    installDesktopApiMock({ appServer: { sendRequest } })
    sendRequest.mockResolvedValue({ turn: { id: 'turn-1' } })
  })

  it.each([
    ['Image message', '', { images: [image] }],
    ['File reference message', '', { files: [file] }],
    ['Attachment message', '', { images: [image], files: [file] }],
    ['Review this change set', 'Review this change set', { files: [file] }]
  ])('names the thread %j', async (expected, text, attachments) => {
    useConversationStore.getState().reset()
    useThreadStore.getState().reset()
    useThreadStore.setState({
      threadList: [{
        id: 'thread-1',
        displayName: null,
        status: 'active',
        originChannel: 'dotcraft-desktop',
        createdAt: new Date().toISOString(),
        lastActiveAt: new Date().toISOString()
      }]
    })

    await startTurnWithOptimisticUI({
      threadId: 'thread-1',
      workspacePath: 'X:\\fixtures\\workspace',
      text,
      ...attachments,
      fallbackThreadName: 'Image message',
      fileFallbackThreadName: 'File reference message',
      attachmentFallbackThreadName: 'Attachment message'
    })

    expect(useThreadStore.getState().threadList[0]?.displayName).toBe(expected)
  })

  it('names an untitled thread from the user text before the turn starts', async () => {
    useConversationStore.getState().reset()
    useThreadStore.getState().reset()
    useThreadStore.setState({
      threadList: [{
        id: 'thread-1',
        displayName: null,
        status: 'active',
        originChannel: 'dotcraft-desktop',
        createdAt: new Date().toISOString(),
        lastActiveAt: new Date().toISOString()
      }]
    })

    await startTurnWithOptimisticUI({
      threadId: 'thread-1',
      workspacePath: 'X:\\fixtures\\workspace',
      text: 'Tighten the retry loop',
      contexts: [{ id: 'c1', kind: 'diffAnnotation', path: 'a.ts', side: 'right', startLine: 1, endLine: 1, comment: 'here' }],
      fallbackThreadName: 'Image message'
    })

    const methods = sendRequest.mock.calls.map(([method]) => method)
    expect(methods.indexOf('thread/rename')).toBeLessThan(methods.indexOf('turn/start'))
    expect(sendRequest).toHaveBeenCalledWith('thread/rename', { threadId: 'thread-1', displayName: 'Tighten the retry loop' })
    expect(useThreadStore.getState().threadList[0]?.displayName).toBe('Tighten the retry loop')
  })
})
