import { expect, it } from 'vitest'
import { normalizeRemoteFileTransferProgress } from '../utils/remoteToolHostDisplay'

it('accepts only complete remote transfer progress snapshots', () => {
  const input = { kind: 'remoteFileTransfer', stage: 'preparing', direction: 'download', localPath: 'local/tool.bin', remotePath: 'remote/tool.bin', hostId: 'fixture-host', hostDisplayName: 'Fixture PC', transferredBytes: 0, completedFiles: 0, completedBytes: 0 }
  expect(normalizeRemoteFileTransferProgress(input)).toEqual(input)
  expect(normalizeRemoteFileTransferProgress({ ...input, kind: 'other' })).toBeNull()
  expect(normalizeRemoteFileTransferProgress({ ...input, completedBytes: -1 })).toBeNull()
})
