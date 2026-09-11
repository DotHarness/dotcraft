import { describe, expect, it } from 'vitest'
import type { ConversationItem } from '../types/conversation'
import {
  formatRemoteToolHostLabel,
  getRemoteFileTransferDisplay,
  normalizeRemoteFileTransferProgress,
  parseRemoteToolHostCatalog,
  readRemoteToolHostOperation,
  type RemoteToolHostNames
} from '../utils/remoteToolHostDisplay'

const NAMES: RemoteToolHostNames = {
  host: (hostId) => (hostId ?? 'sat_studio') === 'sat_studio' ? 'Studio PC' : undefined,
  workspace: (workspaceId) => (workspaceId === 'ws_shaders' ? 'shaders' : undefined)
}

/** Nothing is known about the machine, as when a Disconnect runs on a forgotten route. */
const NO_NAMES: RemoteToolHostNames = { host: () => undefined, workspace: () => undefined }

function item(
  operation: string,
  extra: Partial<ConversationItem> = {}
): ConversationItem {
  return {
    id: 'rth-1',
    type: 'toolCall',
    status: 'completed',
    toolName: `RemoteToolHost.${operation}`,
    source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: operation },
    presentation: { presentationId: 'core.remote-tool-host', options: { operation } },
    createdAt: '2026-09-10T00:00:00Z',
    ...extra
  }
}

const CATALOG = JSON.stringify({
  hosts: [
    {
      hostId: 'sat_studio',
      displayName: 'Studio PC',
      online: true,
      workspaces: [
        { workspaceId: 'ws_shaders', displayName: 'shaders', available: true },
        { workspaceId: 'ws_art', displayName: 'art', available: false, busyOwner: 'other' }
      ]
    },
    {
      hostId: 'sat_qa',
      displayName: 'QA Laptop',
      online: false,
      workspaces: [{ workspaceId: 'ws_qa', displayName: 'qa', available: true }]
    }
  ],
  connectedRoute: {
    hostId: 'sat_studio',
    workspaceId: 'ws_shaders',
    leaseId: 'lease-1',
    hostInstanceId: 'instance-1'
  }
})

const CONNECTED = JSON.stringify({
  route: { threadId: 't1', hostId: 'sat_studio', workspaceId: 'ws_shaders', status: 'connected' },
  environment: {
    hostName: 'Studio PC',
    operatingSystem: 'Windows 11 Pro',
    userName: 'mei',
    workspacePath: 'D:\\art\\shaders'
  },
  matchedTools: ['Shell', 'ReadFile', 'WriteFile'],
  unavailableTools: [],
  unavailableReasons: [],
  alreadyConnected: false
})

describe('readRemoteToolHostOperation', () => {
  it.each(['list', 'connect', 'disconnect', 'transfer'])('accepts the %s operation', (operation) => {
    expect(readRemoteToolHostOperation(item(operation))).toBe(operation)
  })

  it('rejects an operation the family does not own', () => {
    expect(readRemoteToolHostOperation(item('reroute'))).toBeNull()
    expect(readRemoteToolHostOperation({ presentation: undefined })).toBeNull()
  })
})

describe('parseRemoteToolHostCatalog', () => {
  it('reads hosts, workspaces, and the connected route', () => {
    const catalog = parseRemoteToolHostCatalog(CATALOG)

    expect(catalog?.connectedRoute).toEqual({ hostId: 'sat_studio', workspaceId: 'ws_shaders' })
    expect(catalog?.hosts).toHaveLength(2)
    expect(catalog?.hosts[1]).toMatchObject({ displayName: 'QA Laptop', online: false })
    expect(catalog?.hosts[0].workspaces[1]).toMatchObject({
      workspaceId: 'ws_art',
      available: false,
      busyOwner: 'other'
    })
  })

  it('drops entries without an id and keeps the rest', () => {
    const catalog = parseRemoteToolHostCatalog(
      JSON.stringify({ hosts: [{ displayName: 'Nameless' }, { hostId: 'sat_only' }] })
    )

    expect(catalog?.hosts).toHaveLength(1)
    expect(catalog?.hosts[0]).toMatchObject({ hostId: 'sat_only', displayName: 'sat_only', online: false })
    expect(catalog?.connectedRoute).toBeUndefined()
  })

  it.each([undefined, '', 'not json', '{"hosts":"none"}'])('returns null for %s', (result) => {
    expect(parseRemoteToolHostCatalog(result)).toBeNull()
  })
})

describe('formatRemoteToolHostLabel', () => {
  it('reports a partial transfer failure even when the tool returned a result', () => {
    expect(formatRemoteToolHostLabel(item('transfer', {
      arguments: { direction: 'upload', localPath: 'C:/tools/checker', remotePath: 'D:/tools/checker' },
      result: JSON.stringify({
        success: false,
        direction: 'upload',
        localPath: 'C:/tools/checker',
        remotePath: 'D:/tools/checker',
        hostDisplayName: 'Studio PC',
        completedFiles: 2,
        completedBytes: 1024,
        errorCode: 'remote_lease_lost'
      })
    }), 'completed', 'en', NAMES)).toBe(
      'Could not upload checker to Studio PC · The connection to that folder was lost.'
    )
  })

  it('uses live transfer progress and final result fields for the row label', () => {
    const live = item('transfer', {
      arguments: { direction: 'upload', localPath: 'C:/tools/checker', remotePath: 'D:/tools/checker' },
      transferProgress: {
        kind: 'remoteFileTransfer',
        stage: 'transferring',
        direction: 'upload',
        localPath: 'C:/tools/checker',
        remotePath: 'D:/tools/checker',
        hostId: 'sat_studio',
        hostDisplayName: 'Studio PC',
        transferredBytes: 512,
        completedFiles: 0,
        completedBytes: 0,
        totalBytes: 1024,
        totalFiles: 1,
        currentFile: 'checker.exe'
      }
    })
    expect(formatRemoteToolHostLabel(live, 'running', 'en', NAMES))
      .toBe('Uploading checker to Studio PC')

    const completed = item('transfer', {
      result: JSON.stringify({
        success: true,
        direction: 'download',
        localPath: 'C:/tools/checker',
        remotePath: 'D:/tools/checker',
        hostDisplayName: 'Studio PC',
        completedFiles: 1,
        completedBytes: 1024,
        totalFiles: 1,
        totalBytes: 1024,
        transferredBytes: 1024
      })
    })
    expect(formatRemoteToolHostLabel(completed, 'completed', 'en', NO_NAMES))
      .toBe('Downloaded checker from Studio PC · 1 file, 1 KiB')
  })

  it('accepts only remote file transfer progress snapshots', () => {
    const progress = normalizeRemoteFileTransferProgress({
      kind: 'remoteFileTransfer',
      stage: 'preparing',
      direction: 'download',
      localPath: 'C:/tools/checker',
      remotePath: 'D:/tools/checker',
      hostId: 'sat_studio',
      hostDisplayName: 'Studio PC',
      transferredBytes: 0,
      completedFiles: 0,
      completedBytes: 0
    })

    expect(progress?.stage).toBe('preparing')
    expect(getRemoteFileTransferDisplay(item('transfer', {
      arguments: { direction: 'download', localPath: 'C:/tools/checker', remotePath: 'D:/tools/checker' },
      transferProgress: progress ?? undefined
    }), 'running', 'en', NAMES)?.host).toBe('Studio PC')
    expect(normalizeRemoteFileTransferProgress({ ...progress, kind: 'other' })).toBeNull()
  })

  it('says what a listing is doing and what it found', () => {
    expect(formatRemoteToolHostLabel(item('list'), 'running', 'en', NAMES))
      .toBe('Listing remote machines')
    expect(formatRemoteToolHostLabel(item('list', { result: CATALOG }), 'completed', 'en', NAMES))
      .toBe('Listed remote machines · 1 online')
    expect(formatRemoteToolHostLabel(item('list'), 'failed', 'en', NAMES))
      .toBe('Could not list remote machines')
  })

  it('names the machine a connect is joining, then the folder it joined', () => {
    expect(formatRemoteToolHostLabel(
      item('connect', { arguments: { hostId: 'sat_studio', workspaceId: 'ws_shaders' } }),
      'running',
      'en',
      NAMES
    )).toBe('Connecting to Studio PC')

    expect(formatRemoteToolHostLabel(item('connect', { result: CONNECTED }), 'completed', 'en', NAMES))
      .toBe('Joined shaders on Studio PC · 3 tools')
  })

  it('counts a single matched tool in the singular', () => {
    const single = JSON.stringify({ ...JSON.parse(CONNECTED), matchedTools: ['Shell'] })

    expect(formatRemoteToolHostLabel(item('connect', { result: single }), 'completed', 'en', NAMES))
      .toBe('Joined shaders on Studio PC · 1 tool')
  })

  it('explains a refused connect with the localized error code', () => {
    expect(formatRemoteToolHostLabel(
      item('connect', {
        arguments: { hostId: 'sat_studio' },
        errorCode: 'remote_workspace_busy',
        errorMessage: 'This folder is already in use.'
      }),
      'failed',
      'en',
      NAMES
    )).toBe('Could not join Studio PC · Another agent is using that folder right now.')
  })

  it('falls back to the server sentence for an error code Desktop does not know', () => {
    expect(formatRemoteToolHostLabel(
      item('connect', {
        arguments: { hostId: 'sat_studio' },
        errorCode: 'remote_wormhole_collapsed',
        errorMessage: 'The wormhole collapsed.'
      }),
      'failed',
      'en',
      NAMES
    )).toBe('Could not join Studio PC · The wormhole collapsed.')
  })

  it('names the machine a disconnect is leaving, and says when there was none', () => {
    expect(formatRemoteToolHostLabel(item('disconnect'), 'running', 'en', NAMES))
      .toBe('Leaving Studio PC')

    expect(formatRemoteToolHostLabel(
      item('disconnect', {
        result: JSON.stringify({
          disconnected: true,
          previousRoute: { threadId: 't1', hostId: 'sat_studio', workspaceId: 'ws_shaders' }
        })
      }),
      'completed',
      'en',
      NAMES
    )).toBe('Left Studio PC')

    expect(formatRemoteToolHostLabel(
      item('disconnect', { result: JSON.stringify({ disconnected: false }) }),
      'completed',
      'en',
      NAMES
    )).toBe('No remote machine to leave')
  })

  it('keeps a sentence when no machine can be named', () => {
    expect(formatRemoteToolHostLabel(item('disconnect'), 'running', 'en', NO_NAMES))
      .toBe('Leaving the remote machine')
  })

  it('translates the header', () => {
    expect(formatRemoteToolHostLabel(item('connect', { result: CONNECTED }), 'completed', 'zh-Hans', NAMES))
      .toBe('已加入 Studio PC 上的 shaders · 3 个工具')
  })

  it('returns null when the result cannot carry the sentence', () => {
    expect(formatRemoteToolHostLabel(item('list', { result: 'not json' }), 'completed', 'en', NAMES))
      .toBeNull()
    expect(formatRemoteToolHostLabel(item('connect', { result: '{}' }), 'completed', 'en', NAMES))
      .toBeNull()
    expect(formatRemoteToolHostLabel(item('disconnect', { result: '{}' }), 'completed', 'en', NAMES))
      .toBeNull()
    expect(formatRemoteToolHostLabel(item('reroute'), 'running', 'en', NAMES)).toBeNull()
  })
})
