export { BrowserFindBar } from './BrowserFindBar'
import { createPortal } from 'react-dom'
import { Minus, Plus, RotateCcw } from 'lucide-react'
import type { BrowserDownloadRecord, BrowserFindState } from '../../../../shared/viewer/browserFeedback'
import { useT } from '../../../contexts/LocaleContext'
import { useLayerPresence } from '../../../contexts/LayerContext'
import { IconButton } from '../../ui/IconButton'
import { MoreActionsButton } from '../../ui/MoreActionsButton'
import { useUIStore } from '../../../stores/uiStore'
import { useBrowserPopup } from './useBrowserPopup'

export interface BrowserControlsState {
  downloads: BrowserDownloadRecord[]
  find: BrowserFindState
  zoom: number
  error: string
}

export function BrowserFeedbackControls({ tabId, state, run, active = true }: {
  tabId: string
  active?: boolean
  state: BrowserControlsState
  run: (operation: () => Promise<unknown>) => void
}): JSX.Element {
  const t = useT()
  const { popup, setPopup, position, more, content } = useBrowserPopup(tabId, active)
  const api = window.api.workspace.viewer.browser
  useLayerPresence(popup !== null)
  function openSettings(history: boolean) {
    setPopup(null)
    useUIStore.setState({ activeMainView: 'settings' })
    useUIStore.getState().setActiveSettingsTab('browserUse')
    if (history) useUIStore.setState({ browserDownloadHistoryOpen: true })
  }
  return (
    <div className="dc-browser-controls">
      <MoreActionsButton ref={more} label={t('viewer.browser.more')}
        open={popup === 'more'} onClick={() => setPopup(popup === 'more' ? null : 'more')} />
      {popup === 'more' && createPortal(
        <div ref={content} style={position} role="menu" aria-label={t('viewer.browser.more')}
          className="dc-browser-controls__popup dc-browser-controls__menu">
          <button type="button" role="menuitem" className="dc-browser-controls__item" onClick={() => {
            setPopup(null)
            run(() => api.find({ tabId, query: state.find.query }))
          }}>{t('viewer.browser.find')}</button>
          <div role="separator" className="dc-browser-controls__separator" />
          <div role="group" aria-label={t('viewer.browser.zoom')} className="dc-browser-controls__zoom">
            <span>{t('viewer.browser.zoom')}</span>
            <div className="dc-browser-controls__zoom-stepper">
              <IconButton size={24} icon={<Minus size={14} />} label={t('viewer.browser.zoomOut')}
                onClick={() => run(() => api.zoom({ tabId, action: 'out' }))} />
              <span className="dc-browser-controls__zoom-percent">{state.zoom}%</span>
              <IconButton size={24} icon={<Plus size={14} />} label={t('viewer.browser.zoomIn')}
                onClick={() => run(() => api.zoom({ tabId, action: 'in' }))} />
            </div>
            <IconButton size={24} icon={<RotateCcw size={14} />} label={t('viewer.browser.zoomReset')}
              disabled={state.zoom === 100} onClick={() => run(() => api.zoom({ tabId, action: 'reset' }))} />
          </div>
          <div role="separator" className="dc-browser-controls__separator" />
          <button type="button" role="menuitem" className="dc-browser-controls__item"
            onClick={() => openSettings(true)}>{t('viewer.browser.downloads')}</button>
          <button type="button" role="menuitem" className="dc-browser-controls__item" onClick={() => {
            setPopup(null)
            more.current?.focus()
            run(() => api.inspect({ tabId }))
          }}>{t('viewer.browser.inspect')}</button>
          <div role="separator" className="dc-browser-controls__separator" />
          <button type="button" role="menuitem" className="dc-browser-controls__item"
            onClick={() => openSettings(false)}>{t('viewer.browser.browserSettings')}</button>
        </div>, document.body
      )}
    </div>
  )
}
