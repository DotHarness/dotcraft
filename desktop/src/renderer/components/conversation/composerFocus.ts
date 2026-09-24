type ComposerFocusWindow = Window & { __inputComposerFocus?: () => void }

export function focusComposer(): void {
  ;(window as ComposerFocusWindow).__inputComposerFocus?.()
}
