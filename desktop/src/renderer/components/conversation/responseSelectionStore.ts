import { create } from 'zustand'

export interface ResponseSelection {
  owner: string
  threadId: string
  turnId: string
  itemId: string
  text: string
  range: Range
  container: HTMLElement
  draftKey: string
  editing: boolean
}

interface ResponseSelectionState {
  active: ResponseSelection | null
  drafts: Record<string, string>
  open: (selection: ResponseSelection) => void
  dismiss: () => void
  cancel: () => void
  edit: () => void
  setDraft: (comment: string) => void
}

export const useResponseSelectionStore = create<ResponseSelectionState>((set) => ({
  active: null,
  drafts: {},
  open: (active) => set({ active }),
  dismiss: () => set({ active: null }),
  cancel: () => set((state) => {
    const drafts = { ...state.drafts }
    if (state.active) delete drafts[state.active.draftKey]
    return { active: null, drafts }
  }),
  edit: () => set((state) => ({ active: state.active ? { ...state.active, editing: true } : null })),
  setDraft: (comment) => set((state) => state.active
    ? { drafts: { ...state.drafts, [state.active.draftKey]: comment } }
    : state),
}))

export function selectionDraftKey(container: HTMLElement, range: Range, source: string): string {
  const prefix = container.ownerDocument.createRange()
  prefix.selectNodeContents(container)
  prefix.setEnd(range.startContainer, range.startOffset)
  return JSON.stringify([source, prefix.toString().length, range.toString()])
}
