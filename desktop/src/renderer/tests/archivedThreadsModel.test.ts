import { describe, expect, it } from 'vitest'
import type { WorkspaceProjectSummary } from '../../shared/workspaceProjects'
import type { ThreadSummary } from '../types/thread'
import {
  archivedThreadSources,
  groupArchivedThreads,
  isStartableSource,
  sourceFilter
} from '../components/settings/archivedThreads/archivedThreadsModel'

function project(id: string, patch: Partial<WorkspaceProjectSummary> = {}): WorkspaceProjectSummary {
  return {
    projectId: id, kind: 'local', path: `/${id}`, name: id, state: 'secondary', running: true,
    loaded: true, threadCount: 0, threads: [], pinned: false, ...patch
  }
}

function thread(id: string, patch: Partial<ThreadSummary> = {}): ThreadSummary {
  return {
    id, displayName: id, status: 'archived', originChannel: 'dotcraft-desktop',
    createdAt: '2026-11-01T00:00:00Z', lastActiveAt: '2026-11-01T00:00:00Z', ...patch
  }
}

describe('archived threads model', () => {
  const sources = archivedThreadSources({
    projects: [
      project('alpha', { state: 'foreground' }),
      project('beta', { state: 'cold', running: false, loaded: false }),
      project('remote-cold', { kind: 'remote', state: 'cold', running: false, loaded: false }),
      project('remote-live', { kind: 'remote' })
    ],
    chat: project('chats', { kind: 'chat' })
  })
  const [alpha, beta, remote, chats] = sources

  it('lists local projects, live remote projects, and the Chat workspace', () => {
    expect(sources.map((source) => [source.key, source.kind, source.live])).toEqual([
      ['alpha', 'local', true],
      ['beta', 'local', false],
      ['remote-live', 'remote', true],
      ['chats', 'chat', true]
    ])
    expect(sources.filter(isStartableSource)).toEqual([beta])
    expect(alpha.foreground).toBe(true)
  })

  it('groups automation runs from every workspace and applies the project filter', () => {
    const threads = new Map([
      ['alpha', [thread('a1'), thread('a-run', { originChannel: 'automations' })]],
      ['remote-live', [thread('r1')]],
      ['chats', [thread('c-run', { originChannel: 'Automations' })]]
    ])
    const all = groupArchivedThreads(sources, threads, { query: '', project: 'all', sort: 'updated' })
    expect(all.map((group) => [group.id, group.entries.map((entry) => entry.thread.id)])).toEqual([
      [sourceFilter(alpha), ['a1']],
      [sourceFilter(remote), ['r1']],
      ['automations', ['a-run', 'c-run']]
    ])
    expect(all[2].entries.map((entry) => entry.source.key)).toEqual(['alpha', 'chats'])

    const onlyChats = groupArchivedThreads(sources, threads, { query: '', project: sourceFilter(chats), sort: 'updated' })
    expect(onlyChats).toEqual([])
  })

  it('matches titles case-insensitively and sorts within a group', () => {
    const threads = new Map([['alpha', [
      thread('old', { displayName: 'Fix Login', createdAt: '2026-11-03T00:00:00Z', lastActiveAt: '2026-11-01T00:00:00Z' }),
      thread('new', { displayName: 'Add login tests', createdAt: '2026-11-02T00:00:00Z', lastActiveAt: '2026-11-05T00:00:00Z' }),
      thread('other', { displayName: 'Refactor' })
    ]]])
    const ids = (sort: 'updated' | 'created' | 'title') =>
      groupArchivedThreads(sources, threads, { query: 'LOGIN', project: 'all', sort })[0].entries.map((entry) => entry.thread.id)
    expect(ids('updated')).toEqual(['new', 'old'])
    expect(ids('created')).toEqual(['old', 'new'])
    expect(ids('title')).toEqual(['new', 'old'])
  })
})
