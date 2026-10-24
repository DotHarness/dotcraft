let dismissed: boolean | null = null
let loading = false
const listeners = new Set<(dismissed: boolean) => void>()

function apply(next: boolean): void {
  if (dismissed === next) return
  dismissed = next
  for (const listener of listeners) listener(next)
}

function load(): void {
  if (loading) return
  loading = true
  window.api.settings.get().then(
    (settings) => {
      if (dismissed === null) apply(settings.environmentSetupTipDismissed === true)
    },
    () => {
      if (dismissed === null) apply(false)
    }
  )
}

export function environmentSetupTipDismissed(): boolean {
  load()
  return dismissed ?? true
}

export function onEnvironmentSetupTipDismissedChange(listener: (dismissed: boolean) => void): () => void {
  load()
  listeners.add(listener)
  return () => {
    listeners.delete(listener)
  }
}

export function dismissEnvironmentSetupTip(): void {
  apply(true)
  void window.api.settings
    .set({ environmentSetupTipDismissed: true })
    .catch((err: unknown) => console.error('settings:set environmentSetupTipDismissed failed:', err))
}
