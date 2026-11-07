import { useEffect } from 'react'
import { useT } from '../contexts/LocaleContext'
import { usePairedComputersStore } from '../stores/pairedComputersStore'
import { addToast } from '../stores/toastStore'
import { useUIStore } from '../stores/uiStore'

export function usePairedComputerNotices(): void {
  const t = useT()

  useEffect(
    () =>
      window.api.pairedComputers.onEvent((event) => {
        if (event.kind === 'revoked') {
          addToast(t('settings.otherDevices.revoked', { name: event.name }), 'warning')
          return
        }
        if (event.kind !== 'pairingLink') return
        const ui = useUIStore.getState()
        ui.setActiveMainView('settings')
        ui.setActiveSettingsTab('connections')
        usePairedComputersStore.getState().applyEvent(event)
      }),
    [t]
  )
}
