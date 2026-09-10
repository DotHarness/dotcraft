import type { WindowVisibilityState } from '../../preload/api.d'
import { isDesktopPetPresenting } from '../components/desktopPet/petPresentation'

export function isDesktopWindowBackgrounded(state: WindowVisibilityState): boolean {
  return state.minimized || !state.visible
}

/** Conversation updates wait while nobody can see them, unless the desktop pet is showing the thread's work. */
export function conversationRenderPaused(state: WindowVisibilityState): boolean {
  if (isDesktopPetPresenting()) return false
  return document.hidden === true || isDesktopWindowBackgrounded(state)
}
