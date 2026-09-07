import { useEffect } from 'react'
import { create } from 'zustand'

interface ProfileListEntry {
  id: string
  name?: string
}

interface AgentProfileNameState {
  byId: Record<string, string>
  loadedWorkspace: string | null
  loading: boolean
  reqSeq: number
  ensureFor(workspacePath: string): Promise<void>
  refresh(workspacePath: string): Promise<void>
  setFromList(workspacePath: string, profiles: ProfileListEntry[]): void
}

function buildById(profiles: ProfileListEntry[]): Record<string, string> {
  return Object.fromEntries(profiles.map((profile) => [profile.id, profile.name?.trim() || profile.id]))
}

export const useAgentProfileNameStore = create<AgentProfileNameState>((set, get) => ({
  byId: {}, loadedWorkspace: null, loading: false, reqSeq: 0,
  async ensureFor(workspacePath) {
    if (get().loadedWorkspace !== workspacePath) await get().refresh(workspacePath)
  },
  async refresh(workspacePath) {
    const seq = get().reqSeq + 1
    set({ loading: true, reqSeq: seq })
    try {
      const res = await window.api.appServer.sendRequest('agent/profiles/list', { includeInvalid: true }) as { profiles?: ProfileListEntry[] }
      if (get().reqSeq === seq) set({ byId: buildById(res.profiles ?? []), loadedWorkspace: workspacePath, loading: false })
    } catch {
      if (get().reqSeq === seq) set({ loading: false })
    }
  },
  setFromList(workspacePath, profiles) {
    set({ byId: buildById(profiles), loadedWorkspace: workspacePath, loading: false, reqSeq: get().reqSeq + 1 })
  }
}))

/** Resolves a profile id to the visible profile name used by the avatar package. */
export function useResolvedProfileName(profileId: string | undefined, workspacePath: string): string | undefined {
  const byId = useAgentProfileNameStore((state) => state.byId)
  const ensureFor = useAgentProfileNameStore((state) => state.ensureFor)
  useEffect(() => {
    if (profileId) void ensureFor(workspacePath)
  }, [profileId, workspacePath, ensureFor])
  return profileId ? (byId[profileId] ?? profileId) : undefined
}
