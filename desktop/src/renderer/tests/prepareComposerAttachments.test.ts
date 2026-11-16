import { MAX_ATTACHMENT_BYTES } from '../../shared/attachments'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { captureComposerAttachmentTarget, ComposerAttachmentTooLargeError, prepareComposerAttachments } from '../utils/prepareComposerAttachments'
import { startTurnWithOptimisticUI } from '../utils/startTurn'
import { mergeRestoredComposerDraft } from '../utils/composerSubmission'
import { buildComposerInputParts } from '../utils/composeInputParts'
import { installDesktopApiMock } from './desktopApiMock'
import { useConnectionStore } from '../stores/connectionStore'
import { useConversationStore } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'
import { registerThreadConnection, releaseThreadConnection } from '../stores/threadConnectionStore'
import type { ComposerFileAttachment, ImageAttachment } from '../types/conversation'
import type { ComposerContextRecord } from '../../shared/composerContext'

const request = vi.fn()
const readLocalFile = vi.fn()
const statLocalFile = vi.fn()
const source = { connectionKey: 'remote-A', connectionId: 'A-1' }
const clientFile = (): ComposerFileAttachment => ({ path: 'C:\\picked\\notes.txt', fileName: 'notes.txt', source: 'client' })
const clientImage = (): ImageAttachment => ({ tempPath: 'C:\\temp\\image.png', fileName: 'image.png', source: 'client', dataUrl: 'data:image/png;base64,AQID', mimeType: 'image/png' })

beforeEach(() => {
  vi.stubGlobal('window', {})
  request.mockReset().mockResolvedValue({})
  readLocalFile.mockReset().mockResolvedValue({ dataBase64: 'AQID' })
  statLocalFile.mockReset().mockResolvedValue({ sizeBytes: 3 })
  installDesktopApiMock({
    appServer: { sendRequest: request, sendConnectionRequest: request },
    workspace: { attachments: { readLocalFile, statLocalFile } }
  })
  useThreadStore.getState().reset()
  useConversationStore.getState().reset()
  useConnectionStore.getState().reset()
  releaseThreadConnection('attachment-thread')
})

describe('composer attachment preparation', () => {
  it.each(['/canonical', 'D:\\canonical'])('uploads original files and images under canonical root %s, not cwd', async (workspacePath) => {
    useThreadStore.setState({ threadList: [{ id: 'attachment-thread', workspacePath } as never] })
    const image = clientImage()
    const prepared = await prepareComposerAttachments({
      threadId: 'attachment-thread', workspacePath: '/wrong-worktree', files: [clientFile()], images: [image],
      target: { source, request, remote: true }
    })
    const separator = workspacePath.includes('\\') ? '\\' : '/'
    const prefix = workspacePath + separator + '.craft' + separator + 'attachments' + separator
    expect(prepared.files[0].path.startsWith(prefix)).toBe(true)
    expect(prepared.images[0].tempPath.startsWith(prefix + 'images' + separator)).toBe(true)
    expect(prepared.images[0].dataUrl).toBe(image.dataUrl)
    expect(prepared.files[0].source).toBe('host')
    expect(request.mock.calls.map(([method]) => method)).toEqual(['fs/createDirectory', 'fs/writeFile', 'fs/createDirectory', 'fs/writeFile'])
    expect(request.mock.calls.filter(([method]) => method === 'fs/writeFile').map(([, params]) => params.dataBase64)).toEqual(['AQID', 'AQID'])
    expect(statLocalFile).toHaveBeenCalledWith({ path: image.tempPath })
    expect(readLocalFile).toHaveBeenCalledWith({ path: image.tempPath })
  })

  it('keeps host history references and same-host client files without reading or copying bytes', async () => {
    const hostFile = { path: '/host/notes.txt', fileName: 'notes.txt' }
    const hostImage = { ...clientImage(), source: 'host' as const, tempPath: '/host/image.png' }
    const remote = await prepareComposerAttachments({ workspacePath: '/host', files: [hostFile], images: [hostImage], target: { source, request, remote: true } })
    expect(remote.files[0]).toBe(hostFile)
    expect(remote.images[0]).toBe(hostImage)
    const file = clientFile()
    const local = await prepareComposerAttachments({ workspacePath: 'C:\\work', files: [file], target: { request, remote: false } })
    expect(local.files[0]).toBe(file)
    expect(readLocalFile).not.toHaveBeenCalled()
    expect(request).not.toHaveBeenCalled()
  })

  it('uploads page-reference screenshots but never reuploads restored host context images', async () => {
    const context: ComposerContextRecord = { id: 'page', kind: 'pageReference', url: 'https://example.com', title: 'Example', selectionKind: 'region', text: '', comment: '', image: clientImage() }
    const args = { workspacePath: '/host', contexts: [context], target: { source, request, remote: true } }
    const prepared = await prepareComposerAttachments(args)
    const wire = buildComposerInputParts({ text: 'review', ...prepared }).inputParts
    const page = wire.find((part) => part.type === 'contextRef')
    expect(page).toMatchObject({ context: { image: { tempPath: expect.stringMatching(/^\/host\/\.craft\/attachments\/images\//u) } } })
    expect(JSON.stringify(wire)).not.toContain('data:image')
    expect(JSON.stringify(wire)).not.toContain('"source"')
    await prepareComposerAttachments({ ...args, contexts: prepared.contexts })
    expect(readLocalFile).toHaveBeenCalledTimes(1)
  })

  it('retains completed uploads on retry when a later attachment failed', async () => {
    const files = [clientFile(), { ...clientFile(), path: 'C:\\picked\\second.txt' }]
    readLocalFile.mockResolvedValueOnce({ dataBase64: 'AQID' }).mockRejectedValueOnce(new Error('unreadable'))
    const args = { workspacePath: '/host', files, target: { source, request, remote: true } }
    await expect(prepareComposerAttachments(args)).rejects.toThrow('unreadable')
    expect(files[0].source).toBe('client')
    const restored = mergeRestoredComposerDraft({ text: 'retry', segments: [], files, images: [] }, { text: '', segments: [], files: [], images: [] })
    const retried = await prepareComposerAttachments({ ...args, files: restored.files })
    expect(retried.files).toHaveLength(2)
    expect(readLocalFile).toHaveBeenCalledTimes(3)
    expect(request.mock.calls.filter(([method]) => method === 'fs/writeFile')).toHaveLength(2)
  })

  it('rejects files over 10 MiB before reading and sends the complete file at the exact cap', async () => {
    statLocalFile.mockResolvedValueOnce({ sizeBytes: MAX_ATTACHMENT_BYTES + 1 })
    await expect(prepareComposerAttachments({ workspacePath: '/host', files: [clientFile()], target: { source, request, remote: true } })).rejects.toBeInstanceOf(ComposerAttachmentTooLargeError)
    expect(request).not.toHaveBeenCalled()
    expect(readLocalFile).not.toHaveBeenCalled()
    const dataBase64 = 'AAAA'.repeat(Math.floor(MAX_ATTACHMENT_BYTES / 3)) + 'AA=='
    statLocalFile.mockResolvedValueOnce({ sizeBytes: MAX_ATTACHMENT_BYTES })
    readLocalFile.mockResolvedValueOnce({ dataBase64 })
    const prepared = await prepareComposerAttachments({ workspacePath: '/host', files: [clientFile()], target: { source, request, remote: true } })
    expect(request).toHaveBeenCalledWith('fs/writeFile', { path: prepared.files[0].path, dataBase64 })
    expect(request.mock.calls.filter(([method]) => method === 'fs/writeFile')).toHaveLength(1)
  })

  it('captures the thread connection before async reads and retains its connection id after navigation', async () => {
    registerThreadConnection('attachment-thread', { ...source, threadId: 'attachment-thread', workspaceKey: 'remote-A', capabilities: {} })
    useConversationStore.setState({ remoteWorkspaceActive: true })
    const captured = captureComposerAttachmentTarget('attachment-thread')
    let finish!: (value: { dataBase64: string }) => void
    readLocalFile.mockImplementationOnce(() => new Promise((resolve) => { finish = resolve }))
    const pending = prepareComposerAttachments({ threadId: 'attachment-thread', workspacePath: '/host', files: [clientFile()], target: captured })
    await vi.waitFor(() => expect(readLocalFile).toHaveBeenCalled())
    useConnectionStore.setState({ connectionKey: 'remote-B', connectionId: 'B-2' })
    finish({ dataBase64: 'AQID' })
    await pending
    expect(request).toHaveBeenCalledWith('remote-A', 'fs/writeFile', expect.objectContaining({ dataBase64: 'AQID' }), undefined, { connectionId: 'A-1' })
  })
  it('does not upload a same-host file after navigation changes the foreground to a remote project', async () => {
    const file = clientFile()
    const target = { source, request, remote: false }
    useConversationStore.setState({ remoteWorkspaceActive: true })
    await startTurnWithOptimisticUI({
      threadId: 'attachment-thread', workspacePath: 'C:/workspace', text: 'local reference', files: [file],
      fallbackThreadName: 'Message', renameThreadFromText: false, onScreen: false,
      request: target.request, attachmentTarget: target
    })
    expect(statLocalFile).not.toHaveBeenCalled()
    expect(readLocalFile).not.toHaveBeenCalled()
    expect(request).toHaveBeenCalledWith('turn/start', expect.objectContaining({
      input: expect.arrayContaining([{ type: 'fileRef', path: file.path, displayPath: file.path }])
    }))
  })

})
