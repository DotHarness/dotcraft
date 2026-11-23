import { useContext, useEffect, useRef, type JSX, type ReactNode } from 'react'
import { createPortal } from 'react-dom'

import { useT } from '../../contexts/LocaleContext'
import { LayerBoundary, LayerContext } from '../../contexts/LayerContext'
import { useTransientOverlayStore } from '../../stores/transientOverlayStore'
import { useUIStore } from '../../stores/uiStore'
import { DialogCloseButton } from '../ui/DialogCloseButton'

const FOCUSABLE = 'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'

interface SettingsDialogProps {
  navigation: ReactNode
  children: ReactNode
}

export function SettingsDialog({ navigation, children }: SettingsDialogProps): JSX.Element {
  return createPortal(
    <LayerBoundary blocksNativeViews>
      <SettingsDialogLayer navigation={navigation}>{children}</SettingsDialogLayer>
    </LayerBoundary>,
    document.body
  ) as JSX.Element
}

/** The scrim and frame without the portal; render it inside a `LayerBoundary`. */
export function SettingsDialogLayer({ navigation, children }: SettingsDialogProps): JSX.Element {
  const t = useT()
  const depth = useContext(LayerContext)
  const frameRef = useRef<HTMLDivElement>(null)
  const requestClose = useUIStore((state) => state.requestCloseSettings)

  useEffect(() => {
    const opener = document.activeElement
    frameRef.current?.focus({ preventScroll: true })
    return () => {
      if (opener instanceof HTMLElement && opener.isConnected) opener.focus({ preventScroll: true })
    }
  }, [])

  useEffect(() => {
    function onKeyDown(event: KeyboardEvent): void {
      // A layer opened from a settings page sits above this one and owns the key.
      if (event.defaultPrevented || event.isComposing || useTransientOverlayStore.getState().topDepth > depth) return
      if (event.key === 'Escape') {
        event.preventDefault()
        useUIStore.getState().requestCloseSettings()
      } else if (event.key === 'Tab' && frameRef.current) {
        wrapFocus(event, frameRef.current)
      }
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [depth])

  return (
    <div
      className="dotcraft-settings-dialog-scrim"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) requestClose()
      }}
    >
      <div
        ref={frameRef}
        className="dotcraft-settings-dialog"
        role="dialog"
        aria-modal="true"
        aria-label={t('settings.title')}
        tabIndex={-1}
      >
        <div className="dotcraft-settings-dialog__navigation">{navigation}</div>
        <div className="dotcraft-settings-dialog__page">{children}</div>
        <div className="dotcraft-settings-dialog__close">
          <DialogCloseButton label={t('common.close')} onClose={requestClose} />
        </div>
      </div>
    </div>
  )
}

function wrapFocus(event: KeyboardEvent, frame: HTMLElement): void {
  const focusable = Array.from(frame.querySelectorAll<HTMLElement>(FOCUSABLE))
    .filter((element) => element.offsetParent !== null)
  if (focusable.length === 0) return
  const first = focusable[0]
  const last = focusable[focusable.length - 1]
  const active = document.activeElement
  if (event.shiftKey && (active === first || active === frame)) {
    event.preventDefault()
    last.focus()
  } else if (!event.shiftKey && active === last) {
    event.preventDefault()
    first.focus()
  }
}
