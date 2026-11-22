import { create } from 'zustand'
import { setOwnedThreadIds, type OwnedThreadRecord } from '../../shared/ownedThreads'

interface OwnedThreadsState {
  records: readonly OwnedThreadRecord[]
  setRecords(records: readonly OwnedThreadRecord[]): void
}

export const useOwnedThreadsStore = create<OwnedThreadsState>((set) => ({
  records: [],
  setRecords(records) {
    setOwnedThreadIds(records.map((record) => record.threadId))
    set({ records })
  }
}))

export function startOwnedThreadsSync(): () => void {
  const api = window.api?.threadTasks
  if (!api?.listOwned) return () => {}
  let active = true
  void api.listOwned().then((records) => {
    if (active) useOwnedThreadsStore.getState().setRecords(records)
  }, () => {})
  const stop = api.onOwnedChanged?.((records) => useOwnedThreadsStore.getState().setRecords(records))
  return () => {
    active = false
    stop?.()
  }
}

export function findOwnedThread(threadId: string | null | undefined): OwnedThreadRecord | undefined {
  return threadId ? useOwnedThreadsStore.getState().records.find((record) => record.threadId === threadId) : undefined
}

export function useOwnedThread(threadId: string | null | undefined): OwnedThreadRecord | undefined {
  return useOwnedThreadsStore((state) => (threadId ? state.records.find((record) => record.threadId === threadId) : undefined))
}
