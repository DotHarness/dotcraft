import { describe, expect, it } from 'vitest'
import { getRemoteFileTransferDisplay, parseRemoteToolHostCatalog } from './display'

describe('satellite tool results', () => {
  it('maps the remote catalog and rejects malformed tool results', () => {
    const catalog = parseRemoteToolHostCatalog(JSON.stringify({
      hosts: [{
        hostId: 'fixture-host', displayName: 'Fixture PC', online: true,
        workspaces: [{ workspaceId: 'fixture-folder', displayName: 'workspace', available: false, busyOwner: 'other' }]
      }, { displayName: 'Missing identity' }],
      connectedRoute: { hostId: 'fixture-host', workspaceId: 'fixture-folder' }
    }))
    expect(catalog?.hosts).toHaveLength(1)
    expect(catalog?.hosts[0].workspaces[0]).toMatchObject({ available: false, busyOwner: 'other' })
    expect(catalog?.connectedRoute).toEqual({ hostId: 'fixture-host', workspaceId: 'fixture-folder' })
    expect(parseRemoteToolHostCatalog('not json')).toBeNull()
  })

  it('projects live progress and preserves partial completion after a transfer fails', () => {
    const arguments_ = { direction: 'upload', localPath: 'local/tool.bin', remotePath: 'remote/tool.bin' }
    const names = { host: () => 'Fixture PC', workspace: () => undefined }
    const live = getRemoteFileTransferDisplay({
      arguments: arguments_,
      transferProgress: {
        kind: 'remoteFileTransfer', stage: 'transferring', ...arguments_,
        direction: 'upload', hostId: 'fixture-host', hostDisplayName: 'Fixture PC',
        transferredBytes: 512, completedFiles: 1, completedBytes: 256, totalBytes: 1024, totalFiles: 2
      }
    }, 'running', 'en', names)
    expect(live).toMatchObject({ stage: 'transferring', transferredBytes: 512, completedFiles: 1, completedBytes: 256, totalBytes: 1024 })

    const failed = getRemoteFileTransferDisplay({
      result: JSON.stringify({
        ...arguments_, success: false, completedFiles: 1, completedBytes: 256, errorCode: 'remote_lease_lost'
      })
    }, 'completed', 'en', names)
    expect(failed).toMatchObject({ stage: 'failed', completedFiles: 1, completedBytes: 256 })
  })
})
