import { useCallback } from 'react'
import { useConversationStore } from '../stores/conversationStore'
import type { FileDiff } from '../types/toolCall'
import { reconstructNewContent, reconstructOriginalContent } from '../utils/diffReconstruct'

export interface FileChangeActions {
  revertFileDiff(diff: FileDiff): Promise<void>
  reapplyFileDiff(diff: FileDiff): Promise<void>
  revertFileDiffs(diffs: FileDiff[]): Promise<void>
}

export function useFileChangeActions(workspacePath: string): FileChangeActions {
  const markReverted = useConversationStore((s) => s.revertFile)
  const markWritten = useConversationStore((s) => s.reapplyFile)

  const revertFileDiff = useCallback(async (diff: FileDiff): Promise<void> => {
    const absPath = toAbsPath(diff.filePath, workspacePath)
    if (diff.isNewFile) {
      await window.api.file.deleteFile(absPath)
    } else {
      await window.api.file.writeFile(absPath, reconstructOriginalContent(diff))
    }
    markReverted(diff.filePath)
  }, [markReverted, workspacePath])

  const reapplyFileDiff = useCallback(async (diff: FileDiff): Promise<void> => {
    const absPath = toAbsPath(diff.filePath, workspacePath)
    await window.api.file.writeFile(absPath, reconstructNewContent(diff))
    markWritten(diff.filePath)
  }, [markWritten, workspacePath])

  const revertFileDiffs = useCallback(async (diffs: FileDiff[]): Promise<void> => {
    for (const diff of diffs) {
      await revertFileDiff(diff)
    }
  }, [revertFileDiff])

  return { revertFileDiff, reapplyFileDiff, revertFileDiffs }
}

export function toAbsPath(filePath: string, workspacePath: string): string {
  if (filePath.startsWith('/') || /^[A-Za-z]:[\\/]/.test(filePath)) return filePath
  const ws = workspacePath.replace(/\\/g, '/').replace(/\/$/, '')
  const rel = filePath.replace(/\\/g, '/').replace(/^\/+/, '')
  return `${ws}/${rel}`.replace(/\/+/g, '/')
}
