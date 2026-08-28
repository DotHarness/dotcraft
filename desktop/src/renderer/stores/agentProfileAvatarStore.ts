import { useEffect } from 'react'
import { create } from 'zustand'
import { avatarForProfile, normalizeAvatar, type AvatarSpec } from '../components/agents/agentAvatar'

/**
 * Caches each agent profile's *configured* avatar so surfaces that only know a
 * profile's id can render it instead of a fresh name-hash. Only profiles with an
 * explicit stored avatar land in the map; the rest fall back to `avatarForProfile`.
 */

interface ProfileListEntry {
  id: string
  avatar?: number | AvatarSpec
}

interface AgentProfileAvatarState {
  byId: Record<string, AvatarSpec>
  loadedWorkspace: string | null
  loading: boolean
  /** Monotonic guard so a superseded in-flight fetch never clobbers newer data. */
  reqSeq: number
  /** Fetch the workspace's profile avatars unless already loaded for it. */
  ensureFor(workspacePath: string): Promise<void>
  /** Force a refetch (after a profile mutation, or a workspace change). */
  refresh(workspacePath: string): Promise<void>
  /** Populate from an already-fetched list (the builder reuses its own fetch). */
  setFromList(workspacePath: string, profiles: ProfileListEntry[]): void
}

function buildById(profiles: ProfileListEntry[]): Record<string, AvatarSpec> {
  const byId: Record<string, AvatarSpec> = {}
  for (const profile of profiles) {
    const spec = normalizeAvatar(profile.avatar)
    if (spec) byId[profile.id] = spec
  }
  return byId
}

export const useAgentProfileAvatarStore = create<AgentProfileAvatarState>((set, get) => ({
  byId: {},
  loadedWorkspace: null,
  loading: false,
  reqSeq: 0,
  async ensureFor(workspacePath) {
    if (get().loadedWorkspace === workspacePath) return
    await get().refresh(workspacePath)
  },
  async refresh(workspacePath) {
    const seq = get().reqSeq + 1
    set({ loading: true, reqSeq: seq })
    try {
      const res = (await window.api.appServer.sendRequest('agent/profiles/list', {
        includeInvalid: true
      })) as { profiles?: ProfileListEntry[] }
      if (get().reqSeq !== seq) return
      set({ byId: buildById(res.profiles ?? []), loadedWorkspace: workspacePath, loading: false })
    } catch {
      if (get().reqSeq === seq) set({ loading: false })
    }
  },
  setFromList(workspacePath, profiles) {
    set({
      byId: buildById(profiles),
      loadedWorkspace: workspacePath,
      loading: false,
      reqSeq: get().reqSeq + 1
    })
  }
}))

/**
 * Resolves a profile's mascot avatar, honoring the configured (stored) avatar
 * and falling back to the derived one — and lazily loads the workspace's avatars
 * so the stored value is available. Returns undefined when there is no profile.
 */
export function useResolvedProfileAvatar(
  profileId: string | undefined,
  workspacePath: string
): AvatarSpec | undefined {
  const byId = useAgentProfileAvatarStore((s) => s.byId)
  const ensureFor = useAgentProfileAvatarStore((s) => s.ensureFor)
  useEffect(() => {
    if (profileId) void ensureFor(workspacePath)
  }, [profileId, workspacePath, ensureFor])
  return profileId ? (byId[profileId] ?? avatarForProfile(profileId)) : undefined
}
