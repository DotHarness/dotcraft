import { useDetailPanelStore } from './detailPanelStore'
import { useUIStore } from './uiStore'

export function isDetailPanelVisible(): boolean {
  return useUIStore.getState().responsiveLayout === 'full' && useDetailPanelStore.getState().getCurrentPanel().detailPanelPreferredVisible
}

export function useDetailPanelPresentation() {
  const panel = useDetailPanelStore(state => state.getCurrentPanel())
  const layout = useUIStore(state => state.responsiveLayout)
  return {
    ...panel,
    detailPanelVisible: layout === 'full' && panel.detailPanelPreferredVisible
  }
}
