import { translate } from '../shared/locales'
import { sameWorkspaceProjectKey } from '../shared/workspaceProjectKey'
import type { ReadPresentationParams } from '../shared/viewer/presentation'
import type { ReadSpreadsheetParams } from '../shared/viewer/spreadsheet'
import type { SaveCopyAsParams, WriteTextParams } from '../shared/viewer/types'
import { saveFileCopyAs } from './fileSaveAs'
import { mainLocale, type WindowIpcContext } from './ipcBridgeContracts'
import { assertPathWithinViewerRoots, assertWorkspaceOrChatFolder } from './ipcBridgePaths'
import {
  createPastedTextAttachment,
  readPastedTextAttachment,
  restorePastedTextAttachment
} from './pastedTextAttachments'
import { readPresentation } from './presentation/readPresentation'
import { readSpreadsheet } from './spreadsheet/readSpreadsheet'
import { authorizeViewerFile, buildViewerUrl, getViewerWorkspaceRoot, resolveViewerFileForAccess } from './viewerFileProtocol'
import { classifyFile, listDirectory, listViewerFiles, readTextFile, writeTextFile } from './viewerIpc'
import { viewerTextWatchManager } from './viewerTextWatch'
import { listWorkspaceFiles, readImageAsDataUrl, saveImageDataUrlToTemp } from './workspaceComposerIpc'

export function registerWorkspaceFileHandlers(context: WindowIpcContext): void {
  const { handleSafe, workspacePath, callbacks, owner } = context
  handleSafe('workspace:create-pasted-text', async (_event, params: { text: string; workspacePath?: string }) => {
    const root = params.workspacePath || workspacePath
    if (!root) throw new Error(translate(mainLocale(callbacks), 'ipc.noWorkspaceOpen'))
    return createPastedTextAttachment(root, params.text)
  })
  handleSafe('workspace:read-pasted-text', async (_event, params: { path: string }) => {
    return { text: await readPastedTextAttachment(params.path) }
  })
  handleSafe('workspace:restore-pasted-text', async (_event, params: { path: string }) => {
    return { text: await restorePastedTextAttachment(params.path) }
  })

  handleSafe('workspace:save-image-to-temp', async (_event, params: { dataUrl: string; fileName?: string }) => {
    const ws = workspacePath
    if (!ws) {
      throw new Error(translate(mainLocale(callbacks), 'ipc.noWorkspaceOpen'))
    }
    const loc = mainLocale(callbacks)
    const pathAbs = await saveImageDataUrlToTemp(ws, params.dataUrl, params.fileName, loc)
    return { path: pathAbs }
  })

  handleSafe('workspace:read-image-as-data-url', async (_event, params: { path: string }) => {
    const ws = workspacePath
    if (!ws) {
      throw new Error(translate(mainLocale(callbacks), 'ipc.noWorkspaceOpen'))
    }
    const loc = mainLocale(callbacks)
    const dataUrl = await readImageAsDataUrl(ws, params.path, loc)
    return { dataUrl }
  })

  handleSafe(
    'workspace:search-files',
    async (_event, params: { query: string; workspacePath: string; limit?: number }) => {
      if (!workspacePath.trim()) {
        return { files: [], indexStatus: 'empty', indexedCount: 0, stale: false }
      }
      const root = assertWorkspaceOrChatFolder(
        params.workspacePath,
        workspacePath,
        mainLocale(callbacks),
        owner.webContents.id
      )
      const limit = Math.min(20, Math.max(1, params.limit ?? 10))
      return listWorkspaceFiles(root, params.query, limit, owner.webContents.id, { includeDirectories: true })
    }
  )

  handleSafe(
    'workspace:viewer:list-files',
    async (_event, params: { workspacePath: string; query: string; limit: number }) => {
      const root = assertWorkspaceOrChatFolder(
        params.workspacePath,
        workspacePath,
        mainLocale(callbacks),
        owner.webContents.id
      )
      const limit = Math.min(500, Math.max(1, params.limit ?? 100))
      return listViewerFiles(root, params.query, limit, owner.webContents.id)
    }
  )

  handleSafe('workspace:viewer:list-dir', async (_event, params: { dirPath?: string }) => {
    if (!workspacePath) {
      throw new Error(translate(mainLocale(callbacks), 'ipc.noWorkspaceOpen'))
    }
    const target = params.dirPath && params.dirPath.trim() ? params.dirPath : workspacePath
    const { resolved, root } = assertPathWithinViewerRoots(
      target,
      workspacePath,
      mainLocale(callbacks),
      owner.webContents.id
    )
    return listDirectory(resolved, root)
  })

  handleSafe('workspace:viewer:classify', async (_event, params: { absolutePath: string }) => {
    if (!workspacePath) {
      throw new Error(translate(mainLocale(callbacks), 'ipc.noWorkspaceOpen'))
    }
    return classifyFile(params.absolutePath, workspacePath)
  })

  handleSafe('workspace:viewer:read-text', async (_event, params: { absolutePath: string; limitBytes?: number }) => {
    if (!workspacePath) {
      throw new Error(translate(mainLocale(callbacks), 'ipc.noWorkspaceOpen'))
    }
    const absolutePath = await resolveViewerFileForAccess(params.absolutePath, workspacePath, owner.webContents.id)
    return readTextFile(absolutePath, workspacePath, params.limitBytes)
  })

  handleSafe('workspace:viewer:read-spreadsheet', async (_event, params: ReadSpreadsheetParams) => {
    if (!workspacePath) {
      throw new Error(translate(mainLocale(callbacks), 'ipc.noWorkspaceOpen'))
    }
    const absolutePath = await resolveViewerFileForAccess(params.absolutePath, workspacePath, owner.webContents.id)
    return readSpreadsheet(absolutePath, params.sheetIndex)
  })

  handleSafe('workspace:viewer:read-presentation', async (_event, params: ReadPresentationParams) => {
    if (!workspacePath) {
      throw new Error(translate(mainLocale(callbacks), 'ipc.noWorkspaceOpen'))
    }
    const absolutePath = await resolveViewerFileForAccess(params.absolutePath, workspacePath, owner.webContents.id)
    return readPresentation(absolutePath)
  })

  handleSafe('workspace:viewer:write-text', async (_event, params: WriteTextParams) => {
    if (!workspacePath) {
      throw new Error(translate(mainLocale(callbacks), 'ipc.noWorkspaceOpen'))
    }
    const absolutePath = await resolveViewerFileForAccess(params.absolutePath, workspacePath, owner.webContents.id)
    return writeTextFile({ ...params, absolutePath }, workspacePath, (target) =>
      resolveViewerFileForAccess(target, workspacePath, owner.webContents.id)
    )
  })

  handleSafe(
    'workspace:viewer:watch-text',
    async (event, params: { absolutePath: string }): Promise<{ subscriptionId: string }> => {
      if (!workspacePath) {
        throw new Error(translate(mainLocale(callbacks), 'ipc.noWorkspaceOpen'))
      }
      const absolutePath = await resolveViewerFileForAccess(params.absolutePath, workspacePath, owner.webContents.id)
      return { subscriptionId: viewerTextWatchManager.subscribe(event.sender, absolutePath) }
    }
  )

  handleSafe('workspace:viewer:unwatch-text', async (event, params: { subscriptionId: string }): Promise<void> => {
    viewerTextWatchManager.unsubscribe(params.subscriptionId, event.sender.id)
  })

  handleSafe(
    'workspace:viewer:authorize-file',
    async (_event, params: { absolutePath: string; workspacePath: string }): Promise<{ absolutePath: string }> => {
      if (!workspacePath) {
        throw new Error(translate(mainLocale(callbacks), 'ipc.noWorkspaceOpen'))
      }
      if (!sameWorkspaceProjectKey(params.workspacePath, workspacePath) ||
          !sameWorkspaceProjectKey(params.workspacePath, getViewerWorkspaceRoot(owner.webContents.id))) {
        throw new Error('Viewer workspace changed.')
      }
      const absolutePath = await authorizeViewerFile(params.absolutePath, owner.webContents.id)
      return { absolutePath }
    }
  )

  handleSafe(
    'workspace:viewer:to-viewer-url',
    async (_event, params: { absolutePath: string }): Promise<{ url: string }> => {
      if (!workspacePath) {
        throw new Error(translate(mainLocale(callbacks), 'ipc.noWorkspaceOpen'))
      }
      const { resolved } = assertPathWithinViewerRoots(
        params.absolutePath,
        workspacePath,
        mainLocale(callbacks),
        owner.webContents.id
      )
      return { url: buildViewerUrl(resolved) }
    }
  )

  handleSafe('workspace:viewer:save-copy-as', async (event, params: SaveCopyAsParams): Promise<{ saved: boolean }> => {
    if (!workspacePath) {
      throw new Error(translate(mainLocale(callbacks), 'ipc.noWorkspaceOpen'))
    }
    return saveFileCopyAs(
      event.sender,
      await resolveViewerFileForAccess(params.absolutePath, workspacePath, owner.webContents.id)
    )
  })
}
