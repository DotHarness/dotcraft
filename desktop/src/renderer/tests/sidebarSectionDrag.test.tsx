import { act, fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { SidebarSectionItem } from '../../shared/sidebarSections'
import { LocaleProvider } from '../contexts/LocaleContext'
import { CustomSidebarSection } from '../components/sidebar/sections/CustomSidebarSection'
import { SidebarSectionDropTarget } from '../components/sidebar/sections/SidebarSectionDropTarget'
import { clearSidebarSectionDrag, useSidebarSectionDrag } from '../components/sidebar/sections/sidebarSectionDrag'
import { ThreadListRow } from '../components/sidebar/ThreadListRow'
import { useSidebarSectionsStore } from '../stores/sidebarSectionsStore'
import { useUIStore } from '../stores/uiStore'
import { THREAD_DRAG_MIME } from '../utils/threadReferences'
import { installDesktopApiMock } from './desktopApiMock'

const project: SidebarSectionItem = { kind: 'project', projectKey: 'c:/repos/app' }
const chat: SidebarSectionItem = { kind: 'thread', projectKey: 'c:/chats', threadId: 'chat-1' }
const set = vi.fn(async () => undefined)

function transfer(): DataTransfer {
  const data = new Map<string, string>()
  return {
    effectAllowed: 'uninitialized',
    dropEffect: 'none',
    setData: (type: string, value: string) => { data.set(type, value) },
    getData: (type: string) => data.get(type) ?? ''
  } as DataTransfer
}

function Source({ item = project, beforeMove }: { item?: SidebarSectionItem; beforeMove?: () => Promise<void> }) {
  const { sourceProps } = useSidebarSectionDrag(item, beforeMove)
  return <div data-testid="source" {...sourceProps} onDragStart={(event) => {
    if (item.kind === 'thread') {
      event.dataTransfer.setData(THREAD_DRAG_MIME, item.threadId)
      event.dataTransfer.effectAllowed = 'link'
    }
    sourceProps.onDragStart?.(event)
  }}>Source</div>
}

function Sections() {
  const sections = useSidebarSectionsStore((state) => state.sections)
  return <>{sections.map((section) => <div key={section.id} data-testid={section.id}>
    <CustomSidebarSection section={section} empty={section.items.length === 0}>{null}</CustomSidebarSection>
  </div>)}</>
}

function target(sectionId: string): Element {
  return screen.getByTestId(sectionId).firstElementChild!
}

async function drop(element: Element, dataTransfer: DataTransfer) {
  await act(async () => {
    fireEvent.dragOver(element, { dataTransfer })
    fireEvent.drop(element, { dataTransfer })
  })
}

beforeEach(() => {
  set.mockClear()
  installDesktopApiMock({ settings: { set } })
  useSidebarSectionsStore.getState().hydrate({})
  clearSidebarSectionDrag()
})

describe('sidebar section dragging', () => {
  it('moves a project once, appends it, expands the destination and persists placement', async () => {
    const store = useSidebarSectionsStore.getState()
    const from = store.createSection('From', { items: [project] })
    const to = store.createSection('To', { items: [chat] })
    store.setSectionCollapsed(to, true)
    render(<LocaleProvider loadSettings={false}><Source /><Sections /></LocaleProvider>)
    const dataTransfer = transfer()

    fireEvent.dragStart(screen.getByTestId('source'), { dataTransfer })
    await drop(target(to), dataTransfer)

    const sections = useSidebarSectionsStore.getState().sections
    expect(sections.find((section) => section.id === from)?.items).toEqual([])
    expect(sections.find((section) => section.id === to)).toMatchObject({ items: [chat, project] })
    expect(sections.find((section) => section.id === to)?.collapsed).not.toBe(true)
    expect(set).toHaveBeenLastCalledWith({ sidebarSections: sections })
  })

  it('waits for unpinning before moving the item', async () => {
    let resolve!: () => void
    const beforeMove = vi.fn(() => new Promise<void>((done) => { resolve = done }))
    const to = useSidebarSectionsStore.getState().createSection('Work')
    render(<LocaleProvider loadSettings={false}><Source beforeMove={beforeMove} /><Sections /></LocaleProvider>)
    const dataTransfer = transfer()

    fireEvent.dragStart(screen.getByTestId('source'), { dataTransfer })
    await drop(target(to), dataTransfer)
    expect(beforeMove).toHaveBeenCalledOnce()
    expect(useSidebarSectionsStore.getState().sections[0].items).toEqual([])

    await act(async () => { resolve() })
    expect(useSidebarSectionsStore.getState().sections[0].items).toEqual([project])
  })

  it('restores a section project to Projects while a cancelled drag leaves placement unchanged', async () => {
    const to = useSidebarSectionsStore.getState().createSection('Work', { items: [project] })
    useUIStore.setState({ projectsSectionCollapsed: true })
    render(<LocaleProvider loadSettings={false}><Source /><Sections />
      <SidebarSectionDropTarget kind="project"><span data-testid="projects">Projects</span></SidebarSectionDropTarget>
    </LocaleProvider>)
    const dataTransfer = transfer()
    const projects = screen.getByTestId('projects').parentElement!

    fireEvent.dragStart(screen.getByTestId('source'), { dataTransfer })
    fireEvent.dragEnd(screen.getByTestId('source'), { dataTransfer })
    await drop(projects, dataTransfer)
    expect(useSidebarSectionsStore.getState().sections[0].items).toEqual([project])

    fireEvent.dragStart(screen.getByTestId('source'), { dataTransfer })
    await drop(projects, dataTransfer)
    expect(useSidebarSectionsStore.getState().sections.find((section) => section.id === to)?.items).toEqual([])
    expect(useUIStore.getState().projectsSectionCollapsed).toBe(false)
  })

  it('keeps the chat reference payload and link effect through manual reorder handling', async () => {
    const to = useSidebarSectionsStore.getState().createSection('Work')
    render(<LocaleProvider loadSettings={false}>
      <ThreadListRow listId="recents" threadId="chat-1" reorderable onMove={() => {}}>
        <Source item={chat} />
      </ThreadListRow>
      <Sections />
    </LocaleProvider>)
    const dataTransfer = transfer()
    fireEvent.dragStart(screen.getByTestId('source'), { dataTransfer })
    expect(dataTransfer.getData(THREAD_DRAG_MIME)).toBe('chat-1')
    expect(dataTransfer.effectAllowed).toBe('linkMove')
    await drop(target(to), dataTransfer)
    expect(useSidebarSectionsStore.getState().sections[0].items).toEqual([chat])
  })
})
