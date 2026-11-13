import type { FileViewerTab } from '../../shared/viewer/types'

export async function prepareFileViewer(
  absolutePath: string,
  workspacePath: string
): Promise<Pick<FileViewerTab, 'absolutePath' | 'contentClass' | 'sizeBytes'>> {
  const authorized = await window.api.workspace.viewer.authorizeFile({ absolutePath, workspacePath })
  const classified = await window.api.workspace.viewer.classify({ absolutePath: authorized.absolutePath })
  return {
    absolutePath: authorized.absolutePath,
    contentClass: classified.contentClass,
    sizeBytes: classified.sizeBytes
  }
}
