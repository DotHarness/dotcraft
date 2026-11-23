import { useDetailPanelStore, type PanelSession } from './detailPanelStore'
import { useUIStore, type UIState } from './uiStore'

function detailPanelPresentation(panel: PanelSession, layout: UIState['responsiveLayout']) {
  const compact = layout !== 'full' && panel.openSystemTabs.includes('sources')
  return {
    detailPanelVisible: panel.detailPanelPreferredVisible && (layout === 'full' || compact),
    detailPanelCompact: compact
  }
}

export function isDetailPanelVisible(): boolean {
  const panel = useDetailPanelStore.getState().getCurrentPanel()
  return detailPanelPresentation(panel, useUIStore.getState().responsiveLayout).detailPanelVisible
}

export function useDetailPanelPresentation() {
  const panel = useDetailPanelStore(state => state.getCurrentPanel())
  const layout = useUIStore(state => state.responsiveLayout)
  return { ...panel, ...detailPanelPresentation(panel, layout) }
}
