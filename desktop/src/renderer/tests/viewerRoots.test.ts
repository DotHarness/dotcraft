import { beforeEach, describe, expect, it } from 'vitest'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import { containingRoot, relativeToRoot, rootLabel, viewerRootsFor } from '../utils/viewerRoots'
import type { WorkspaceProjectSummary } from '../../shared/workspaceProjects'

function project(path: string, secondaryFolders?: string[]): WorkspaceProjectSummary {
  return {
    kind: 'local',
    path,
    name: rootLabel(path),
    state: 'foreground',
    running: true,
    loaded: true,
    threadCount: 0,
    threads: [],
    pinned: false,
    ...(secondaryFolders ? { secondaryFolders } : {})
  }
}

describe('viewerRoots', () => {
  beforeEach(() => {
    useWorkspaceProjectsStore.setState({
      projects: [project('X:/work/app', ['X:/work/shared', 'X:/work/assets'])]
    })
  })

  it('lists the workspace folder first, then the Project folders', () => {
    expect(viewerRootsFor('X:/work/app')).toEqual(['X:/work/app', 'X:/work/shared', 'X:/work/assets'])
  })

  it('returns a single root for folders outside any multi-folder Project', () => {
    expect(viewerRootsFor('D:/solo')).toEqual(['D:/solo'])
    expect(viewerRootsFor('')).toEqual([])
  })

  it('picks the longest containing root case-insensitively', () => {
    const roots = ['X:/work', 'X:/work/shared']
    expect(containingRoot('x:\\work\\shared\\src\\a.ts', roots)).toBe('X:/work/shared')
    expect(containingRoot('X:/work/other/a.ts', roots)).toBe('X:/work')
    expect(containingRoot('X:/workx/a.ts', roots)).toBeNull()
  })

  it('derives the path relative to a root', () => {
    expect(relativeToRoot('X:\\work\\shared\\src\\a.ts', 'X:/work/shared/')).toBe('src/a.ts')
  })
})
