import { hasFindSurfaces } from './registry'
import { useFindStore } from '../stores/findStore'

export function handleFindShortcut(event: KeyboardEvent): boolean {
  if (event.key !== 'f' && event.key !== 'F') return false
  if (!(event.ctrlKey || event.metaKey) || event.altKey || event.isComposing) return false

  // A window-wide find opened behind a dialog would search content the user cannot see.
  const target = event.target
  if (target instanceof Element && target.closest('[role="dialog"], [aria-modal="true"]') !== null) {
    return false
  }
  if (!hasFindSurfaces()) return false

  event.preventDefault()
  useFindStore.getState().openFind()
  return true
}
