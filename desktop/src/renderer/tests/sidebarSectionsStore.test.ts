// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { SidebarSectionItem } from '../../shared/sidebarSections'
import { useSidebarSectionsStore } from '../stores/sidebarSectionsStore'
import { installDesktopApiMock } from './desktopApiMock'

const project: SidebarSectionItem = { kind: 'project', projectKey: 'c:/repos/app' }
const chat: SidebarSectionItem = { kind: 'thread', projectKey: 'c:/chats', threadId: 'thread-1' }

describe('sidebar sections store', () => {
  const set = vi.fn(async () => undefined)

  beforeEach(() => {
    set.mockClear()
    installDesktopApiMock({ settings: { set } })
    useSidebarSectionsStore.getState().hydrate({})
  })

  it('moves an item out of its previous section and persists the result', () => {
    const store = useSidebarSectionsStore.getState()
    const work = store.createSection('Work', { items: [project, chat] })
    const later = store.createSection('Later')

    useSidebarSectionsStore.getState().moveItems(later, [project])

    const sections = useSidebarSectionsStore.getState().sections
    expect(sections.map((section) => [section.id, section.items])).toEqual([
      [work, [chat]],
      [later, [project]]
    ])
    expect(set).toHaveBeenLastCalledWith({ sidebarSections: sections })
  })
})
