import { create } from 'zustand'
import type { DesktopPluginHost } from '@dotcraft/plugin'
import {
  canShowFeatureGuide,
  featureGuideMoment,
  normalizeFeatureGuides,
  recordFeatureGuideClosed,
  recordFeatureGuideShown,
  recordSubagentChat,
  settleFeatureGuide,
  spaceFeatureGuideId,
  type FeatureGuideId,
  type FeatureGuidesSettings
} from '../../shared/featureGuides'

interface FeatureGuideState {
  settings: FeatureGuidesSettings | null
  initializedThisSession: boolean
  active: FeatureGuideId | null
  spaceRequests: ReadonlyMap<FeatureGuideId, DesktopPluginHost>
  load: () => Promise<void>
  request: (id: FeatureGuideId) => boolean
  close: (id: FeatureGuideId, acted: boolean) => void
  settle: (id: FeatureGuideId) => void
  noteSubagentChat: (threadId: string) => void
  requestSpaceGuide: (spaceKey: string, host: DesktopPluginHost) => void
  clearSpaceRequests: (host: DesktopPluginHost) => void
  setEnabled: (enabled: boolean) => Promise<void>
}

function persist(settings: FeatureGuidesSettings): Promise<void> {
  return window.api.settings.set({ featureGuides: settings }).then(() => undefined)
}

export const useFeatureGuideStore = create<FeatureGuideState>((set, get) => {
  const settledBeforeLoad = new Set<FeatureGuideId>()
  const update = (next: FeatureGuidesSettings): void => {
    if (next === get().settings) return
    set({ settings: next })
    void persist(next).catch(() => {})
  }

  return {
    settings: null,
    initializedThisSession: false,
    active: null,
    spaceRequests: new Map(),

    async load() {
      if (get().settings) return
      const stored = (await window.api.settings.get()).featureGuides
      if (get().settings) return
      const initial = normalizeFeatureGuides(stored ?? { initializedAt: Date.now() })
      let settings = initial
      for (const id of settledBeforeLoad) settings = settleFeatureGuide(settings, id)
      settledBeforeLoad.clear()
      set({ settings, initializedThisSession: stored === undefined })
      if (stored === undefined || settings !== initial) void persist(settings).catch(() => {})
    },

    request(id) {
      const { settings, active, initializedThisSession } = get()
      if (!settings || document.hidden) return false
      if (active !== null) return active === id
      const now = Date.now()
      if (!canShowFeatureGuide(settings, id, now, initializedThisSession)) return false
      const spaceRequests = new Map(get().spaceRequests)
      spaceRequests.delete(id)
      set({ active: id, spaceRequests })
      update(recordFeatureGuideShown(settings, id, now))
      return true
    },

    close(id, acted) {
      const { settings, active } = get()
      if (active === id) set({ active: null })
      if (!settings) return
      update(acted || featureGuideMoment(id) === 'entry'
        ? settleFeatureGuide(settings, id)
        : recordFeatureGuideClosed(settings, id, Date.now()))
    },

    settle(id) {
      const { settings, active } = get()
      if (active === id) set({ active: null })
      if (settings) update(settleFeatureGuide(settings, id))
      else settledBeforeLoad.add(id)
    },

    requestSpaceGuide(spaceKey, host) {
      const { spaceRequests, settings, initializedThisSession } = get()
      const id = spaceFeatureGuideId(spaceKey)
      if (spaceRequests.get(id) === host || (settings && !canShowFeatureGuide(settings, id, Date.now(), initializedThisSession))) return
      set({ spaceRequests: new Map(spaceRequests).set(id, host) })
    },

    clearSpaceRequests(host) {
      const { spaceRequests } = get()
      const remaining = new Map([...spaceRequests].filter(([, owner]) => owner !== host))
      if (remaining.size !== spaceRequests.size) set({ spaceRequests: remaining })
    },

    noteSubagentChat(threadId) {
      const { settings } = get()
      if (settings) update(recordSubagentChat(settings, threadId))
    },

    async setEnabled(enabled) {
      const { settings, active } = get()
      if (!settings) return
      let next = { ...settings, enabled }
      if (!enabled && active) next = featureGuideMoment(active) === 'entry'
        ? settleFeatureGuide(next, active)
        : recordFeatureGuideClosed(next, active, Date.now())
      set({ settings: next, ...(enabled ? {} : { active: null }) })
      await persist(next)
    }
  }
})
