import { create } from 'zustand'
import {
  MAX_SIDEBAR_SECTION_NAME_LENGTH,
  normalizeSidebarSections,
  sidebarSectionItemKey,
  type SidebarSection,
  type SidebarSectionItem
} from '../../shared/sidebarSections'

interface SidebarSectionsState {
  sections: SidebarSection[]
  hydrate(settings: { sidebarSections?: unknown }): void
  createSection(name: string, options?: { afterSectionId?: string; items?: SidebarSectionItem[] }): string
  renameSection(sectionId: string, name: string): void
  removeSection(sectionId: string): void
  setSectionCollapsed(sectionId: string, collapsed: boolean): void
  moveItems(sectionId: string, items: SidebarSectionItem[]): void
  removeItems(items: SidebarSectionItem[]): void
}

function persist(sections: SidebarSection[]): void {
  void window.api?.settings
    ?.set({ sidebarSections: sections })
    .catch((err: unknown) => console.error('settings:set sidebarSections failed:', err))
}

function sectionName(name: string): string {
  return name.trim().slice(0, MAX_SIDEBAR_SECTION_NAME_LENGTH)
}

function withoutItems(sections: SidebarSection[], items: SidebarSectionItem[]): SidebarSection[] {
  const keys = new Set(items.map(sidebarSectionItemKey))
  return sections.map((section) => ({
    ...section,
    items: section.items.filter((item) => !keys.has(sidebarSectionItemKey(item)))
  }))
}

function newSectionId(): string {
  return globalThis.crypto?.randomUUID?.() ?? `section-${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`
}

export const useSidebarSectionsStore = create<SidebarSectionsState>((set, get) => {
  const commit = (sections: SidebarSection[]): void => {
    set({ sections })
    persist(sections)
  }
  return {
    sections: [],

    hydrate(settings) {
      set({ sections: normalizeSidebarSections(settings.sidebarSections) ?? [] })
    },

    createSection(name, options) {
      const id = newSectionId()
      const items = options?.items ?? []
      const section: SidebarSection = { id, name: sectionName(name), items }
      const sections = withoutItems(get().sections, items)
      const anchor = options?.afterSectionId ? sections.findIndex((entry) => entry.id === options.afterSectionId) : -1
      commit(anchor < 0 ? [...sections, section] : [...sections.slice(0, anchor + 1), section, ...sections.slice(anchor + 1)])
      return id
    },

    renameSection(sectionId, name) {
      const next = sectionName(name)
      if (!next) return
      commit(get().sections.map((section) => (section.id === sectionId ? { ...section, name: next } : section)))
    },

    removeSection(sectionId) {
      commit(get().sections.filter((section) => section.id !== sectionId))
    },

    setSectionCollapsed(sectionId, collapsed) {
      commit(get().sections.map((section) => {
        if (section.id !== sectionId) return section
        const { collapsed: _previous, ...rest } = section
        return collapsed ? { ...rest, collapsed: true } : rest
      }))
    },

    moveItems(sectionId, items) {
      if (!get().sections.some((section) => section.id === sectionId)) return
      commit(withoutItems(get().sections, items).map((section) =>
        section.id === sectionId ? { ...section, items: [...section.items, ...items] } : section
      ))
    },

    removeItems(items) {
      commit(withoutItems(get().sections, items))
    }
  }
})
