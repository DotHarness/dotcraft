import { create } from 'zustand'
import { activeDetailScopeId } from '../utils/detailPanelScope'

interface ComposerFileReferenceStore {
  pendingByScope: ReadonlyMap<string | null, string[]>
  request(path: string): void
  consume(scopeId: string | null): string[]
}

export const useComposerFileReferenceStore = create<ComposerFileReferenceStore>((set, get) => ({
  pendingByScope: new Map(),
  request(path) {
    if (!path.trim()) return
    const scopeId = activeDetailScopeId()
    set((state) => {
      const pendingByScope = new Map(state.pendingByScope)
      pendingByScope.set(scopeId, [...(pendingByScope.get(scopeId) ?? []), path])
      return { pendingByScope }
    })
  },
  consume(scopeId) {
    const paths = get().pendingByScope.get(scopeId)
    if (!paths) return []
    set((state) => {
      const pendingByScope = new Map(state.pendingByScope)
      pendingByScope.delete(scopeId)
      return { pendingByScope }
    })
    return paths
  }
}))
