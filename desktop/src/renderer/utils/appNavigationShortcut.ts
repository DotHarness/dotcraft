import { useAppNavigationStore } from '../stores/appNavigationStore'
import { useUIStore } from '../stores/uiStore'

export function handleAppNavigationShortcut(
  event: KeyboardEvent,
  platform = window.api.platform
): boolean {
  if (
    platform === 'darwin'
    || !event.ctrlKey
    || event.metaKey
    || event.shiftKey
    || event.altKey
    || event.isComposing
    || (event.key !== '[' && event.key !== ']')
  ) {
    return false
  }

  const target = event.target
  const blockingOverlay = target instanceof Element
    && target.closest('[role="dialog"], [aria-modal="true"]') != null
    || useUIStore.getState().quickOpenVisible
  if (blockingOverlay) return true

  event.preventDefault()
  const navigation = useAppNavigationStore.getState()
  if (event.key === '[') navigation.goBack()
  else navigation.goForward()
  return true
}
