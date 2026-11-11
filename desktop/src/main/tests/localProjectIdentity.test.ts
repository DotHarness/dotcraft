import { mkdtempSync, rmSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { normalizeWorkspaceProjectKey } from '../../shared/workspaceProjectKey'
import type { DesktopWindowRuntime } from '../windows/windowRuntime'

const electron = vi.hoisted(() => ({ getPath: vi.fn() }))
vi.mock('electron', () => ({ app: { getPath: electron.getPath, getLocale: () => 'en-US' } }))

import { loadSettings, saveLocalProjectDetails, saveSettings, type AppSettings } from '../settings'
import { getWorkspaceProjectsPayload } from '../windows/windowProjects'

let tempRoot: string

afterEach(() => {
  if (tempRoot) rmSync(tempRoot, { recursive: true, force: true })
})

describe('local Project identity', () => {
  it('keeps old chats attached after primary promotion, reload, and another source edit', () => {
    tempRoot = mkdtempSync(join(tmpdir(), 'dotcraft-project-identity-'))
    electron.getPath.mockReturnValue(tempRoot)
    const anchor = join(tempRoot, 'app')
    const promoted = join(tempRoot, 'lib')
    const shared = join(tempRoot, 'shared')
    const replacement = join(tempRoot, 'new-shared')
    const key = normalizeWorkspaceProjectKey(anchor)
    const thread = { id: 'existing-thread', workspacePath: anchor }
    const settings: AppSettings = { pinnedProjectIds: [key], projectOrder: [key] }
    saveLocalProjectDetails(settings, { primaryFolder: anchor, secondaryFolders: [promoted, shared] })
    expect(saveLocalProjectDetails(settings, {
      projectPath: anchor, primaryFolder: promoted, secondaryFolders: [anchor, shared]
    })).toBe(anchor)
    saveSettings(settings)

    const restored = loadSettings()
    saveLocalProjectDetails(restored, {
      projectPath: anchor, primaryFolder: promoted, secondaryFolders: [anchor, replacement]
    })
    saveSettings(restored)
    const reloaded = loadSettings()
    const context = {
      settings: reloaded,
      startupProjectStates: new Map(),
      workspaceConnections: new Map(),
      normalizeWorkspaceConnectionKey: normalizeWorkspaceProjectKey,
      currentWorkspacePath: anchor,
      app: { localProjects: { list: () => [{ path: anchor }] }, pairedComputers: { savedProjects: () => [] } },
      getWorkspaceConnection: () => ({ client: { connectionId: null }, connected: true, threads: [thread] }),
      isWorkspaceForeground: () => true,
      localProjectId: normalizeWorkspaceProjectKey,
      getPinnedThreadIdsForWorkspace: () => [],
      isProjectPinned: () => true,
      buildDefaultChatSummary: () => undefined
    } as unknown as DesktopWindowRuntime
    const payload = getWorkspaceProjectsPayload(context)

    expect(payload.projects).toHaveLength(1)
    expect(payload.projects[0]).toMatchObject({
      projectId: key, path: anchor, identityWorkspacePath: anchor, primaryFolder: promoted,
      secondaryFolders: [anchor, replacement], threads: [thread], state: 'foreground'
    })
    expect(Object.keys(reloaded.localProjectDetails!)).toEqual([key])
    expect(reloaded.pinnedProjectIds).toEqual([key])
    expect(reloaded.projectOrder).toEqual([key])
  })
})
