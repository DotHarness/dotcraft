import { MAX_ATTACHMENT_BYTES } from '../../shared/attachments'
import type { AppServerConnectionSource, TypedAppServerRequestApi } from '../../shared/appServerBoundary'
import type { ComposerContextRecord } from '../../shared/composerContext'
import type { ComposerFileAttachment, ImageAttachment } from '../types/conversation'
import { useConnectionStore } from '../stores/connectionStore'
import { useConversationStore } from '../stores/conversationStore'
import { getThreadConnection } from '../stores/threadConnectionStore'
import { useThreadStore } from '../stores/threadStore'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import { translate, type AppLocale } from '../../shared/locales'
import { describeAppServerError } from './appServerErrorText'

export interface ComposerAttachmentTarget {
  source?: AppServerConnectionSource
  request: TypedAppServerRequestApi['sendRequest']
  remote: boolean
}

export function captureComposerAttachmentTarget(threadId?: string): ComposerAttachmentTarget {
  const foreground = useConnectionStore.getState()
  const retained = threadId ? getThreadConnection(threadId) : undefined
  const connection = retained ?? foreground
  const source = connection.connectionKey && connection.connectionId
    ? { connectionKey: connection.connectionKey, connectionId: connection.connectionId } : undefined
  const projects = useWorkspaceProjectsStore.getState()
  const project = source && [...projects.projects, ...(projects.chat ? [projects.chat] : [])]
    .find((candidate) => candidate.connectionKey === source.connectionKey)
  const remote = project ? project.kind === 'remote' : useConversationStore.getState().remoteWorkspaceActive
  const request: ComposerAttachmentTarget['request'] = source
    ? (method, params, timeout, options) => window.api.appServer.sendConnectionRequest(
        source.connectionKey, method, params, timeout, { ...options, connectionId: source.connectionId })
    : window.api.appServer.sendRequest
  return { source, request, remote }
}

export class ComposerAttachmentTooLargeError extends Error {
  constructor(readonly maxFileBytes: number, readonly image: boolean) {
    super('Attachment exceeds the file size limit.')
  }
}

export function describeComposerAttachmentError(locale: AppLocale, error: unknown): string {
  return error instanceof ComposerAttachmentTooLargeError
    ? translate(locale, error.image ? 'input.imageTooLarge' : 'input.attachmentTooLarge', { mb: error.maxFileBytes / 1024 / 1024 })
    : describeAppServerError(locale, error)
}

interface AttachmentDraft {
  threadId?: string
  workspacePath: string
  files?: ComposerFileAttachment[]
  images?: ImageAttachment[]
  contexts?: ComposerContextRecord[]
  target?: ComposerAttachmentTarget
}

const uploadedPaths = new WeakMap<object, Map<string, Promise<string>>>()

function hostJoin(root: string, ...segments: string[]): string {
  const separator = root.includes('\\') ? '\\' : '/'
  return [root.replace(/[/\\]+$/u, ''), ...segments].join(separator)
}

function safeName(name: string): string {
  return name.split(/[/\\]/u).pop()?.replace(/[<>:"|?*\x00-\x1f]/gu, '_') || 'attachment'
}

export async function prepareComposerAttachments({
  threadId, workspacePath, files = [], images = [], contexts = [],
  target = captureComposerAttachmentTarget(threadId)
}: AttachmentDraft): Promise<{ files: ComposerFileAttachment[]; images: ImageAttachment[]; contexts: ComposerContextRecord[] }> {
  if (!target.remote) return { files, images, contexts }
  const state = useThreadStore.getState()
  const thread = state.activeThread?.id === threadId ? state.activeThread : state.threadList.find((entry) => entry.id === threadId)
  const root = thread?.workspacePath || workspacePath
  const scope = JSON.stringify([target.source?.connectionKey, target.source?.connectionId, root])
  const upload = async (attachment: object, path: string, fileName: string, image: boolean): Promise<string> => {
    let cache = uploadedPaths.get(attachment)
    if (!cache) { cache = new Map(); uploadedPaths.set(attachment, cache) }
    const existing = cache.get(scope)
    if (existing) return existing
    const pending = (async () => {
      if (!root) throw new Error('The thread workspace is unavailable.')
      const { sizeBytes } = await window.api.workspace.attachments.statLocalFile({ path })
      if (sizeBytes > MAX_ATTACHMENT_BYTES) throw new ComposerAttachmentTooLargeError(MAX_ATTACHMENT_BYTES, image)
      const { dataBase64 } = await window.api.workspace.attachments.readLocalFile({ path })
      const id = crypto.randomUUID()
      const name = safeName(fileName)
      const extension = /\.[a-z0-9]+$/iu.exec(name)?.[0]?.toLowerCase() || '.png'
      const directory = image
        ? hostJoin(root, '.craft', 'attachments', 'images')
        : hostJoin(root, '.craft', 'attachments', id)
      const destination = hostJoin(directory, image ? id + extension : name)
      await target.request('fs/createDirectory', { path: directory, recursive: true })
      await target.request('fs/writeFile', { path: destination, dataBase64 })
      return destination
    })()
    cache.set(scope, pending)
    try { return await pending } catch (error) { cache.delete(scope); throw error }
  }
  const preparedFiles: ComposerFileAttachment[] = []
  for (const file of files) preparedFiles.push(file.source === 'client'
    ? { ...file, path: await upload(file, file.path, file.fileName, false), source: 'host' } : file)
  const preparedImages: ImageAttachment[] = []
  for (const image of images) preparedImages.push(image.source === 'client'
    ? { ...image, tempPath: await upload(image, image.tempPath, image.fileName, true), source: 'host' } : image)
  const preparedContexts: ComposerContextRecord[] = []
  for (const context of contexts) {
    if (context.kind !== 'pageReference' || !context.image) { preparedContexts.push(context); continue }
    const image = context.image
    preparedContexts.push(image.source === 'client' ? { ...context, image: {
      ...image, tempPath: await upload(image, image.tempPath, image.fileName, true), source: 'host'
    } } : context)
  }
  return { files: preparedFiles, images: preparedImages, contexts: preparedContexts }
}
