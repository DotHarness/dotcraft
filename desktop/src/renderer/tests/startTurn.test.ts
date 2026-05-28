// @vitest-environment jsdom

import { beforeEach, describe, expect, it, vi } from 'vitest'
import { startTurnWithOptimisticUI } from '../utils/startTurn'
import { useConversationStore } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'

const sendRequest = vi.fn()

describe('startTurnWithOptimisticUI thread naming', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    useConversationStore.getState().reset()
    useThreadStore.getState().reset()
    useThreadStore.setState({
      threadList: [
        {
          id: 'thread-1',
          displayName: null,
          status: 'active',
          originChannel: 'dotcraft-desktop',
          createdAt: new Date().toISOString(),
          lastActiveAt: new Date().toISOString()
        }
      ]
    })

    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        appServer: {
          sendRequest
        }
      }
    })

    sendRequest.mockResolvedValue({ turn: { id: 'turn-1' } })
  })

  it('keeps the image fallback for image-only messages', async () => {
    await startTurnWithOptimisticUI({
      threadId: 'thread-1',
      workspacePath: 'F:\\dotcraft',
      text: '',
      images: [
        {
          tempPath: 'C:\\temp\\image.png',
          dataUrl: 'data:image/png;base64,abc',
          fileName: 'image.png',
          mimeType: 'image/png'
        }
      ],
      fallbackThreadName: 'Image message',
      fileFallbackThreadName: 'File reference message',
      attachmentFallbackThreadName: 'Attachment message'
    })

    expect(useThreadStore.getState().threadList[0]?.displayName).toBe('Image message')
  })

  it('uses a file-oriented fallback for file-only messages', async () => {
    await startTurnWithOptimisticUI({
      threadId: 'thread-1',
      workspacePath: 'F:\\dotcraft',
      text: '',
      files: [{ path: 'C:\\temp\\notes.txt', fileName: 'notes.txt' }],
      fallbackThreadName: 'Image message',
      fileFallbackThreadName: 'File reference message',
      attachmentFallbackThreadName: 'Attachment message'
    })

    expect(useThreadStore.getState().threadList[0]?.displayName).toBe('File reference message')
  })

  it('uses the attachment fallback for mixed attachment messages', async () => {
    await startTurnWithOptimisticUI({
      threadId: 'thread-1',
      workspacePath: 'F:\\dotcraft',
      text: '',
      images: [
        {
          tempPath: 'C:\\temp\\image.png',
          dataUrl: 'data:image/png;base64,abc',
          fileName: 'image.png',
          mimeType: 'image/png'
        }
      ],
      files: [{ path: 'C:\\temp\\notes.txt', fileName: 'notes.txt' }],
      fallbackThreadName: 'Image message',
      fileFallbackThreadName: 'File reference message',
      attachmentFallbackThreadName: 'Attachment message'
    })

    expect(useThreadStore.getState().threadList[0]?.displayName).toBe('Attachment message')
  })

  it('still prefers visible text when it exists', async () => {
    await startTurnWithOptimisticUI({
      threadId: 'thread-1',
      workspacePath: 'F:\\dotcraft',
      text: 'Review this change set',
      files: [{ path: 'C:\\temp\\notes.txt', fileName: 'notes.txt' }],
      fallbackThreadName: 'Image message',
      fileFallbackThreadName: 'File reference message',
      attachmentFallbackThreadName: 'Attachment message'
    })

    expect(useThreadStore.getState().threadList[0]?.displayName).toBe('Review this change set')
  })
})
