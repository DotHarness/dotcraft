import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useConnectionStore } from '../stores/connectionStore'
import { useConversationStore } from '../stores/conversationStore'
import { useDetailPanelStore } from '../stores/detailPanelStore'
import { useSideChatStore, type SideChatPrompt } from '../stores/sideChatStore'
import { getThreadConnection } from '../stores/threadConnectionStore'
import { useThreadStore } from '../stores/threadStore'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'

let notify: ((payload: { method: string; params: unknown }) => void) | undefined
let send: ReturnType<typeof vi.fn>
let statLocalFile: ReturnType<typeof vi.fn>
let readLocalFile: ReturnType<typeof vi.fn>

function sideChat() {
  const chat = [...useSideChatStore.getState().chats.values()][0]
  if (!chat) throw new Error('Missing side chat')
  return chat
}

function calls(method: string) {
  return send.mock.calls.filter(([, name]) => name === method)
}

beforeEach(() => {
  useSideChatStore.getState().reset()
  useThreadStore.getState().reset()
  useWorkspaceProjectsStore.getState().reset()
  useDetailPanelStore.setState({ byThread: new Map(), currentThreadId: 'parent' })
  useConnectionStore.setState({ status: 'connected', connectionKey: 'host-a', connectionId: 'connection-a' })
  useConversationStore.setState({ workspacePath: '/project', remoteWorkspaceActive: true })
  useThreadStore.setState({ threadList: [{
    id: 'parent', workspacePath: '/project', effectiveWorkspacePath: '/worktree',
    displayName: null, status: 'active', originChannel: 'dotcraft-desktop',
    createdAt: '2026-11-16T00:00:00Z', lastActiveAt: '2026-11-16T00:00:00Z'
  }] })
  statLocalFile = vi.fn(async () => ({ sizeBytes: 3 }))
  readLocalFile = vi.fn(async () => ({ dataBase64: 'AQID' }))
  send = vi.fn(async (_key: string, method: string, _params: unknown, _timeout: unknown,
    options?: { onResponse?: (result: unknown) => void }) => {
    const result = method === 'thread/fork'
      ? { thread: { id: 'side-1', workspacePath: '/canonical-project' } }
      : method === 'turn/start' ? { turn: { id: 'turn-1' } } : {}
    options?.onResponse?.(result)
    return result
  })
  vi.stubGlobal('window', {
    setTimeout, clearTimeout,
    api: {
      appServer: {
        sendConnectionRequest: send,
        sendRequest: vi.fn(async () => { throw new Error('Unexpected foreground request') }),
        getConnectionKey: async () => 'host-a',
        sendServerResponse: vi.fn(async () => {}),
        onNotification: (callback: typeof notify) => { notify = callback; return () => {} }
      },
      workspace: { attachments: { statLocalFile, readLocalFile } }
    }
  })
})

afterEach(() => {
  useSideChatStore.getState().reset()
  useDetailPanelStore.setState({ byThread: new Map(), currentThreadId: null })
  vi.unstubAllGlobals()
})

describe('side chat attachments', () => {
  it('uploads the initial prompt before echo or start using its captured connection and canonical workspace', async () => {
    const uploaded = Promise.withResolvers<void>()
    const release = Promise.withResolvers<void>()
    const normalSend = send.getMockImplementation()!
    send.mockImplementation(async (...args) => {
      if (args[1] === 'fs/writeFile') { uploaded.resolve(); await release.promise }
      return normalSend(...args)
    })
    const opening = useSideChatStore.getState().open('parent', {
      label: () => 'Side chat',
      prompt: { text: 'Inspect this', files: [{ path: 'C:/local/report.txt', fileName: 'report.txt', source: 'client' }] }
    })
    await uploaded.promise
    expect(calls('turn/start')).toHaveLength(0)
    expect(sideChat().turns).toEqual([])
    useConnectionStore.setState({ connectionKey: 'host-b', connectionId: 'connection-b' })
    release.resolve()
    const key = await opening

    const written = calls('fs/writeFile')[0][2] as { path: string }
    expect(written.path).toMatch(/^\/canonical-project\/\.craft\/attachments\/[^/]+\/report\.txt$/)
    expect(calls('turn/start')[0][2]).toMatchObject({
      threadId: 'side-1', input: expect.arrayContaining([{ type: 'fileRef', path: written.path, displayPath: written.path }])
    })
    expect(statLocalFile).toHaveBeenCalledWith({ path: 'C:/local/report.txt' })
    expect(readLocalFile).toHaveBeenCalledWith({ path: 'C:/local/report.txt' })
    expect(calls('fs/writeFile')[0][2]).toEqual({ path: written.path, dataBase64: 'AQID' })
    for (const [connectionKey, , , , options] of send.mock.calls) {
      expect(connectionKey).toBe('host-a')
      expect(options).toMatchObject({ connectionId: 'connection-a', attachmentOwner: `sidechat:${key}` })
    }
    expect(getThreadConnection('side-1')).toMatchObject({ connectionKey: 'host-a', connectionId: 'connection-a' })
  })

  it('prepares continued image and screenshot inputs before steering and retains host references', async () => {
    const key = await useSideChatStore.getState().open('parent', { label: () => 'Side chat' })
    notify?.({ method: 'turn/started', params: { turn: { id: 'running', threadId: 'side-1', status: 'running' } } })
    await useSideChatStore.getState().send(key!, {
      text: 'Compare these',
      files: [{ path: '/canonical-project/existing.txt', fileName: 'existing.txt', source: 'host' }],
      images: [{ tempPath: 'C:/local/image.png', fileName: 'image.png', mimeType: 'image/png', dataUrl: 'data:image/png;base64,AQID', source: 'client' }],
      contexts: [{
        id: 'page', kind: 'pageReference', url: 'https://example.test', title: 'Page',
        selectionKind: 'region', text: '', comment: '',
        image: { tempPath: 'C:/local/region.png', fileName: 'region.png', mimeType: 'image/png', dataUrl: 'data:image/png;base64,AQID', source: 'client' }
      }]
    })

    expect(statLocalFile.mock.calls).toEqual([
      [{ path: 'C:/local/image.png' }],
      [{ path: 'C:/local/region.png' }]
    ])
    expect(readLocalFile.mock.calls).toEqual([
      [{ path: 'C:/local/image.png' }],
      [{ path: 'C:/local/region.png' }]
    ])
    expect(calls('turn/start')).toHaveLength(0)
    const writes = calls('fs/writeFile').map(([, , params]) => (params as { path: string }).path)
    expect(writes).toHaveLength(2)
    const steer = calls('turn/steer')[0][2] as { input: unknown[] }
    expect(steer).toMatchObject({ threadId: 'side-1', expectedTurnId: 'running' })
    expect(steer.input).toEqual(expect.arrayContaining([
      { type: 'fileRef', path: '/canonical-project/existing.txt', displayPath: '/canonical-project/existing.txt' },
      { type: 'localImage', path: writes[0], fileName: 'image.png', mimeType: 'image/png' },
      expect.objectContaining({ type: 'contextRef', context: expect.objectContaining({ image: {
        tempPath: writes[1], fileName: 'region.png', mimeType: 'image/png'
      } }) })
    ]))
  })

  it('keeps a failed prompt retryable and reuses files that already uploaded successfully', async () => {
    const normalSend = send.getMockImplementation()!
    let rejectImage = true
    send.mockImplementation(async (...args) => {
      if (args[1] === 'fs/writeFile' && (args[2] as { path: string }).path.endsWith('.png') && rejectImage) {
        rejectImage = false
        throw new Error('Upload failed')
      }
      return normalSend(...args)
    })
    const prompt: SideChatPrompt = {
      text: 'Review both',
      files: [{ path: 'C:/local/report.txt', fileName: 'report.txt', source: 'client' }],
      images: [{ tempPath: 'C:/local/image.png', fileName: 'image.png', mimeType: 'image/png', dataUrl: '', source: 'client' }]
    }
    await expect(useSideChatStore.getState().open('parent', { label: () => 'Side chat', prompt })).rejects.toThrow('Upload failed')
    expect(calls('turn/start')).toHaveLength(0)
    expect(sideChat().turns).toEqual([])
    expect(prompt.files?.[0]).toMatchObject({ path: 'C:/local/report.txt', source: 'client' })
    await useSideChatStore.getState().send(sideChat().key, prompt)

    expect(statLocalFile.mock.calls.filter(([params]) => params.path === 'C:/local/report.txt')).toHaveLength(1)
    expect(readLocalFile.mock.calls.filter(([params]) => params.path === 'C:/local/report.txt')).toHaveLength(1)
    expect(calls('turn/start')).toHaveLength(1)
  })

  it('keeps existing host image parts unchanged without uploading', async () => {
    const key = await useSideChatStore.getState().open('parent', { label: () => 'Side chat' })
    await useSideChatStore.getState().send(key!, {
      text: '', images: [{ tempPath: '/canonical-project/.craft/attachments/images/kept.png', fileName: 'kept.png', mimeType: 'image/png', dataUrl: '' }]
    })
    expect(statLocalFile).not.toHaveBeenCalled()
    expect(readLocalFile).not.toHaveBeenCalled()
    expect(calls('turn/start')[0][2]).toMatchObject({ input: [{
      type: 'localImage', path: '/canonical-project/.craft/attachments/images/kept.png', fileName: 'kept.png', mimeType: 'image/png'
    }] })
    useSideChatStore.getState().discard(key!)
    expect(getThreadConnection('side-1')).toBeUndefined()
  })
})
