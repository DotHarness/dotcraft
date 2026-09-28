import { createContext, useContext } from 'react'
import { create } from 'zustand'
import { useThreadStore } from '../../stores/threadStore'

export interface ThreadRowPlacement {
  listId: string | null
  home: boolean
}

export const ThreadRowPlacementContext = createContext<ThreadRowPlacement>({ listId: null, home: true })

interface SelectionOrigin {
  threadId: string
  listId: string | null
}

const useSelectionOriginStore = create<{ origin: SelectionOrigin | null }>(() => ({ origin: null }))

export function useThreadRowSelected(threadId: string): boolean {
  const { listId, home } = useContext(ThreadRowPlacementContext)
  const active = useThreadStore((s) => s.activeThreadId === threadId)
  const origin = useSelectionOriginStore((s) => (s.origin?.threadId === threadId ? s.origin : null))
  if (!active) return false
  return origin ? origin.listId === listId : home
}

export function useRememberRowSelection(): (threadId: string) => void {
  const { listId } = useContext(ThreadRowPlacementContext)
  return (threadId) => useSelectionOriginStore.setState({ origin: { threadId, listId } })
}
