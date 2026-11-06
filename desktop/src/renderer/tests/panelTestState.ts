import { useDetailPanelStore } from '../stores/detailPanelStore'
import { useUIStore } from '../stores/uiStore'
import { useThreadStore } from '../stores/threadStore'

export function getRendererTestState() {
  const ui = useUIStore.getState()
  const detail = useDetailPanelStore.getState()
  const panel = detail.getCurrentPanel()
  return {
    ...ui,
    ...detail,
    ...panel,
    detailPanelVisible: ui.responsiveLayout === 'full' && panel.detailPanelPreferredVisible
  }
}

export function setRendererTestState(value: Partial<ReturnType<typeof getRendererTestState>> | ((state: ReturnType<typeof getRendererTestState>) => Partial<ReturnType<typeof getRendererTestState>>)): void {
  const patch = typeof value === 'function' ? value(getRendererTestState()) : value
  const detail = useDetailPanelStore.getState()
  const threadId = useThreadStore.getState().activeThreadId
  if (threadId && detail.currentThreadId !== threadId) detail.onThreadSwitched(threadId)
  const panel = detail.getCurrentPanel()
  const panelPatch: Record<string, unknown> = {}
  const uiPatch: Record<string, unknown> = {}
  for (const [key, field] of Object.entries(patch)) {
    if (key === 'detailPanelVisible') {
      if (patch.detailPanelPreferredVisible === undefined) panelPatch.detailPanelPreferredVisible = field
    } else if (key in panel) panelPatch[key] = field
    else uiPatch[key] = field
  }
  if (Object.keys(panelPatch).length) detail.patchPanel(panelPatch)
  if (Object.keys(uiPatch).length) useUIStore.setState(uiPatch)
}
