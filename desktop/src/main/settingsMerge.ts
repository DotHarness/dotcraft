import type { AppSettings } from './settings'
import { normalizeLocale } from '../shared/locales'
import { mergeOpenTargetPreferences } from '../shared/openTargets'

export function mergeUpdatedSettings(current: AppSettings, partial: Partial<AppSettings>): Partial<AppSettings> {
  const next: Partial<AppSettings> = { ...partial }

  if (partial.openTargetPreferences !== undefined) {
    next.openTargetPreferences = mergeOpenTargetPreferences(current.openTargetPreferences, partial.openTargetPreferences)
  }

  if (partial.locale !== undefined) {
    next.locale = normalizeLocale(partial.locale)
  }

  if (partial.webSocket !== undefined) {
    next.webSocket = {
      ...(current.webSocket ?? {}),
      ...partial.webSocket
    }
  }

  if (partial.remote !== undefined) {
    next.remote = {
      ...(current.remote ?? {}),
      ...partial.remote
    }
  }

  if (partial.browserUse !== undefined) {
    next.browserUse = {
      ...(current.browserUse ?? {}),
      ...partial.browserUse
    }
  }

  if (partial.notifications !== undefined) {
    next.notifications = {
      ...(current.notifications ?? {}),
      ...partial.notifications
    }
  }

  if (partial.profile !== undefined) {
    next.profile = {
      ...(current.profile ?? {}),
      ...partial.profile
    }
  }

  if (partial.voice !== undefined) {
    next.voice = {
      ...(current.voice ?? {}),
      ...partial.voice
    }
  }

  if (partial.pinnedThreadIdsByWorkspace !== undefined) {
    next.pinnedThreadIdsByWorkspace = {
      ...(current.pinnedThreadIdsByWorkspace ?? {}),
      ...partial.pinnedThreadIdsByWorkspace
    }
  }

  if (partial.threadOrderByProject !== undefined) {
    next.threadOrderByProject = {
      ...(current.threadOrderByProject ?? {}),
      ...partial.threadOrderByProject
    }
  }

  if (partial.projectActionPreferences !== undefined) {
    next.projectActionPreferences = {
      ...(current.projectActionPreferences ?? {}),
      ...partial.projectActionPreferences
    }
  }

  if (partial.turnBookmarksByThread !== undefined) {
    next.turnBookmarksByThread = {
      ...(current.turnBookmarksByThread ?? {}),
      ...partial.turnBookmarksByThread
    }
  }

  return next
}
