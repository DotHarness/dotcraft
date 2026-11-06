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

  it('lists the workspace folder first, then the Project folders, or the folder alone outside a Project', () => {
    expect(viewerRootsFor('X:/work/app')).toEqual(['X:/work/app', 'X:/work/shared', 'X:/work/assets'])
    expect(viewerRootsFor('D:/solo')).toEqual(['D:/solo'])
    expect(viewerRootsFor('')).toEqual([])
  })

  it('uses the explicit project identity for a worktree without adding the original checkout', () => {
    useWorkspaceProjectsStore.setState({
      projects: [
        project('X:/work/other', ['X:/work/app', 'X:/work/worktree']),
        project('X:/work/app', ['X:/work/private'])
      ]
    })

    expect(viewerRootsFor('X:/work/worktree', 'X:/work/app', 'X:/work/app'))
      .toEqual(['X:/work/worktree', 'X:/work/private'])

    useWorkspaceProjectsStore.setState({ projects: [{
      ...project('X:/work/app', ['X:/work/app', 'X:/work/new-shared']), primaryFolder: 'X:/work/lib'
    }] })
    expect(viewerRootsFor('X:/work/app')).toEqual(['X:/work/app', 'X:/work/lib', 'X:/work/new-shared'])
    expect(viewerRootsFor('X:/work/worktree', 'X:/work/app', 'X:/work/app'))
      .toEqual(['X:/work/worktree', 'X:/work/lib', 'X:/work/new-shared'])

    useWorkspaceProjectsStore.setState({ projects: [{
      ...project('X:/work/app', ['X:/work/new-shared']), primaryFolder: 'X:/work/lib'
    }] })
    expect(viewerRootsFor('X:/work/worktree', 'X:/work/app', 'X:/work/lib'))
      .toEqual(['X:/work/worktree', 'X:/work/new-shared'])
  })

  it('picks the longest containing root case-insensitively and derives the relative path', () => {
    const roots = ['X:/work', 'X:/work/shared']
    expect(containingRoot('x:\\work\\shared\\src\\a.ts', roots)).toBe('X:/work/shared')
    expect(containingRoot('X:/work/other/a.ts', roots)).toBe('X:/work')
    expect(containingRoot('X:/workx/a.ts', roots)).toBeNull()
    expect(relativeToRoot('X:\\work\\shared\\src\\a.ts', 'X:/work/shared/')).toBe('src/a.ts')
  })
})
