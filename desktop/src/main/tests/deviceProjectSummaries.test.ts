import { describe, expect, it, vi } from 'vitest'
import type { DesktopWindowRuntime } from '../windows/windowRuntime'
import type { WorkspaceConnectionEntry } from '../windows/windowTypes'

vi.mock('electron', () => ({ app: { getPath: () => '', getLocale: () => 'en-US' } }))
import { getWorkspaceProjectsPayload } from '../windows/windowProjects'

function fixture() {
  const saved = ['one', 'two'].map((computerId) => ({
    computer: { computerId, name: computerId },
    project: { projectId: 'project', path: '/same/path', displayName: 'Workspace', kind: 'project' }
  }))
  const connections = new Map<string, WorkspaceConnectionEntry>()
  const ctx = {
    settings: {}, currentWorkspacePath: '/local', workspaceConnections: connections,
    app: { localProjects: { list: () => [] }, pairedComputers: { savedProjects: () => saved } },
    buildDeviceRemoteProject: (computer: { computerId: string }, project: { path: string; displayName: string }) => ({
      projectId: `remote:device:${computer.computerId}:project`, displayPath: project.path,
      identityWorkspacePath: project.path, name: project.displayName,
      remote: { source: 'device', hostId: computer.computerId, remoteProjectId: 'project' }
    }),
    getPinnedThreadIdsForProject: () => [], isProjectPinned: () => false,
    buildDefaultChatSummary: () => undefined, localProjectId: (path: string) => path
  } as unknown as DesktopWindowRuntime
  return { ctx, connections }
}

describe('paired project summaries', () => {
  it('keeps background thread summaries isolated by computer identity, not remote path', () => {
    const { ctx, connections } = fixture()
    const threads = [{ id: 'background-thread', unreadCount: 1 }]
    const key = 'remote:device:one:project'
    connections.set(key, { key, connected: true, threads } as WorkspaceConnectionEntry)

    const { projects } = getWorkspaceProjectsPayload(ctx)
    expect(projects[0]).toMatchObject({ projectId: key, state: 'secondary', loaded: true, threadCount: 1, threads })
    expect(projects[1]).toMatchObject({ projectId: 'remote:device:two:project', state: 'cold', loaded: false, threads: [] })
  })

  it('keeps cached threads during background recovery and reports failures without losing the cache', () => {
    const { ctx, connections } = fixture()
    const key = 'remote:device:one:project'
    const entry = {
      key, connected: false, connecting: true, threads: [{ id: 'cached-thread' }],
      initializeResult: { serverInfo: { name: 'fixture', version: '1' }, capabilities: {} }
    } as WorkspaceConnectionEntry
    connections.set(key, entry)
    expect(getWorkspaceProjectsPayload(ctx).projects[0]).toMatchObject({
      state: 'connecting', loaded: true, threads: entry.threads
    })

    entry.connecting = false
    entry.errorMessage = 'Access rejected'
    expect(getWorkspaceProjectsPayload(ctx).projects[0]).toMatchObject({
      state: 'error', running: false, loaded: true, errorMessage: 'Access rejected', threads: entry.threads
    })
  })
})
