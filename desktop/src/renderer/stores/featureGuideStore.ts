import { create } from 'zustand'
import {
  FEATURE_GUIDE_MOMENTS,
  canShowFeatureGuide,
  normalizeFeatureGuides,
  recordFeatureGuideClosed,
  recordFeatureGuideShown,
  recordSubagentChat,
  settleFeatureGuide,
  type FeatureGuideId,
  type FeatureGuidesSettings
} from '../../shared/featureGuides'

interface FeatureGuideState {
  settings: FeatureGuidesSettings | null
  initializedThisSession: boolean
  active: FeatureGuideId | null
  load: () => Promise<void>
  request: (id: FeatureGuideId) => boolean
  close: (id: FeatureGuideId, acted: boolean) => void
  settle: (id: FeatureGuideId) => void
  noteSubagentChat: (threadId: string) => void
  setEnabled: (enabled: boolean) => Promise<void>
}

function persist(settings: FeatureGuidesSettings): Promise<void> {
  return window.api.settings.set({ featureGuides: settings }).then(() => undefined)
}

export const useFeatureGuideStore = create<FeatureGuideState>((set, get) => {
  const update = (next: FeatureGuidesSettings): void => {
    if (next === get().settings) return
    set({ settings: next })
    void persist(next).catch(() => {})
  }

  return {
    settings: null,
    initializedThisSession: false,
    active: null,

    async load() {
      if (get().settings) return
      const stored = (await window.api.settings.get()).featureGuides
      if (get().settings) return
      if (stored === undefined) {
        const settings = normalizeFeatureGuides({ initializedAt: Date.now() })
        set({ settings, initializedThisSession: true })
        void persist(settings).catch(() => {})
        return
      }
      set({ settings: normalizeFeatureGuides(stored) })
    },

    request(id) {
      const { settings, active, initializedThisSession } = get()
      if (!settings || document.hidden) return false
      if (active !== null) return active === id
      const now = Date.now()
      if (!canShowFeatureGuide(settings, id, now, initializedThisSession)) return false
      set({ active: id })
      update(recordFeatureGuideShown(settings, id, now))
      return true
    },

    close(id, acted) {
      const { settings, active } = get()
      if (active === id) set({ active: null })
      if (!settings) return
      update(acted || FEATURE_GUIDE_MOMENTS[id] === 'entry'
        ? settleFeatureGuide(settings, id)
        : recordFeatureGuideClosed(settings, id, Date.now()))
    },

    settle(id) {
      const { settings, active } = get()
      if (active === id) set({ active: null })
      if (settings) update(settleFeatureGuide(settings, id))
    },

    noteSubagentChat(threadId) {
      const { settings } = get()
      if (settings) update(recordSubagentChat(settings, threadId))
    },

    async setEnabled(enabled) {
      const { settings } = get()
      if (!settings) return
      const next = { ...settings, enabled }
      set({ settings: next, ...(enabled ? {} : { active: null }) })
      await persist(next)
    }
  }
})
