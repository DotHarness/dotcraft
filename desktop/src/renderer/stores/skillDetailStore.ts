import { create } from 'zustand'

interface SkillDetailState {
  name: string | null
  open(name: string): void
  close(): void
}

/** The skill whose details are open over the current surface. */
export const useSkillDetailStore = create<SkillDetailState>((set) => ({
  name: null,
  open: (name) => set({ name }),
  close: () => set({ name: null })
}))
