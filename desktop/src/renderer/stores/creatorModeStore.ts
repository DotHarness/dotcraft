import { useEffect } from 'react'
import { create } from 'zustand'

interface CreatorModeState {
  threads: ReadonlySet<string>
}

const useCreatorModeStore = create<CreatorModeState>(() => ({ threads: new Set() }))

let loaded: Promise<void> | null = null
let changes = 0

function loadCreatorThreads(): Promise<void> {
  if (loaded) return loaded
  const changesAtStart = changes
  loaded = window.api.workspace.listCreatorThreads()
    .then((threadIds) => {
      // A change made while the list loads is already in Main's list but not in this response.
      if (changes === changesAtStart) useCreatorModeStore.setState({ threads: new Set(threadIds) })
    })
    .catch(() => {
      loaded = null
    })
  return loaded
}

export function useThreadCreator(threadId: string): boolean {
  useEffect(() => {
    void loadCreatorThreads()
  }, [])
  return useCreatorModeStore((state) => threadId.length > 0 && state.threads.has(threadId))
}

export async function setThreadCreator(threadId: string, creator: boolean): Promise<void> {
  changes += 1
  const previous = useCreatorModeStore.getState().threads
  const next = new Set(previous)
  if (creator) next.add(threadId)
  else next.delete(threadId)
  useCreatorModeStore.setState({ threads: next })
  try {
    await window.api.workspace.setCreatorThread(threadId, creator)
  } catch (error) {
    useCreatorModeStore.setState({ threads: previous })
    throw error
  }
}
