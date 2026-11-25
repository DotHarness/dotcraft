import { create } from 'zustand'
import type { ItemId } from '@dotcraft/avatar'
import { showFindToast } from './findToast'

interface Gift {
  key: number
  id: ItemId
}

interface GiftState {
  /** Unopened finds. The item is already in the bag, so a restart simply drops them. */
  gifts: Gift[]
  opened: Gift | null
  openRevision: number
  stages: number
  open(key: number): void
  dismiss(): void
}

let nextKey = 1

export const useGiftStore = create<GiftState>((set, get) => ({
  gifts: [],
  opened: null,
  openRevision: 0,
  stages: 0,
  open(key) {
    const gift = get().gifts.find((candidate) => candidate.key === key)
    if (!gift) return
    set((state) => ({
      gifts: state.gifts.filter((candidate) => candidate.key !== key),
      opened: gift,
      openRevision: state.openRevision + 1
    }))
  },
  dismiss() {
    set({ opened: null })
  }
}))

export function presentFind(id: ItemId): void {
  if (useGiftStore.getState().stages === 0) {
    showFindToast(id)
    return
  }
  const key = nextKey++
  useGiftStore.setState((state) => ({ gifts: [...state.gifts, { key, id }] }))
}

export function registerGiftStage(): () => void {
  useGiftStore.setState((state) => ({ stages: state.stages + 1 }))
  return () => useGiftStore.setState((state) => ({ stages: state.stages - 1 }))
}
