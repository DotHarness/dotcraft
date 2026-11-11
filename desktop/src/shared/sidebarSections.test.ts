import { describe, expect, it } from 'vitest'
import { forgetProjectInSidebarSections, normalizeSidebarSections } from './sidebarSections'

describe('normalizeSidebarSections', () => {
  it('drops invalid sections and keeps each item in its first section only', () => {
    const sections = normalizeSidebarSections([
      { id: 'work', name: '  Work  ', items: [
        { kind: 'project', projectKey: 'C:/Repos/App/' },
        { kind: 'thread', projectKey: 'C:/Repos/App', threadId: 'thread-1' },
        { kind: 'thread', projectKey: 'C:/Repos/App' },
        { kind: 'folder', projectKey: 'C:/Repos/App' }
      ] },
      { id: 'work', name: 'Duplicate id', items: [] },
      { id: 'blank', name: '   ', items: [] },
      { id: 'later', name: 'Later', collapsed: true, items: [{ kind: 'project', projectKey: 'c:/repos/app' }] }
    ])

    expect(sections).toEqual([
      { id: 'work', name: 'Work', items: [
        { kind: 'project', projectKey: 'c:/repos/app' },
        { kind: 'thread', projectKey: 'c:/repos/app', threadId: 'thread-1' }
      ] },
      { id: 'later', name: 'Later', collapsed: true, items: [] }
    ])
  })

  it('removes a forgotten project and its chats from every section', () => {
    const sections = forgetProjectInSidebarSections([
      { id: 'work', name: 'Work', items: [
        { kind: 'project', projectKey: 'c:/repos/app' },
        { kind: 'thread', projectKey: 'c:/repos/app', threadId: 'thread-1' },
        { kind: 'project', projectKey: 'c:/repos/site' }
      ] }
    ], 'C:/Repos/App')

    expect(sections?.[0].items).toEqual([{ kind: 'project', projectKey: 'c:/repos/site' }])
  })
})
