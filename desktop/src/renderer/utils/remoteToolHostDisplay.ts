import type { RemoteFileTransferProgress } from '../types/conversation'

export function normalizeRemoteFileTransferProgress(value: unknown): RemoteFileTransferProgress | null {
  const record = asRecord(value)
  const stage = text(record?.stage)
  const direction = transferDirection(text(record?.direction))
  const localPath = text(record?.localPath)
  const remotePath = text(record?.remotePath)
  const hostId = text(record?.hostId)
  const hostDisplayName = text(record?.hostDisplayName)
  const transferredBytes = nonNegativeNumber(record?.transferredBytes)
  const completedFiles = nonNegativeNumber(record?.completedFiles)
  const completedBytes = nonNegativeNumber(record?.completedBytes)
  if (record?.kind !== 'remoteFileTransfer'
      || (stage !== 'preparing' && stage !== 'transferring')
      || !direction || !localPath || !remotePath || !hostId || !hostDisplayName
      || transferredBytes == null || completedFiles == null || completedBytes == null) return null
  const totalBytes = nonNegativeNumber(record.totalBytes)
  const totalFiles = nonNegativeNumber(record.totalFiles)
  const currentFile = text(record.currentFile)
  return {
    kind: 'remoteFileTransfer', stage, direction, localPath, remotePath, hostId, hostDisplayName,
    transferredBytes, completedFiles, completedBytes,
    ...(totalBytes != null ? { totalBytes } : {}),
    ...(totalFiles != null ? { totalFiles } : {}),
    ...(currentFile ? { currentFile } : {})
  }
}


function transferDirection(value: string | undefined): 'upload' | 'download' | undefined {
  return value === 'upload' || value === 'download' ? value : undefined
}

function nonNegativeNumber(value: unknown): number | undefined {
  return typeof value === 'number' && Number.isFinite(value) && value >= 0 ? value : undefined
}

function asRecord(value: unknown): Record<string, unknown> | null {
  return value != null && typeof value === 'object' && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : null
}

function text(value: unknown): string | undefined {
  return typeof value === 'string' && value.trim() !== '' ? value : undefined
}
