import { ipcRenderer } from 'electron'
import type { PanelCheckpoint, PanelScope, PanelStateApi } from '../shared/viewer/panelState'

export const viewerPanelState: PanelStateApi = {
  read: (scope: PanelScope): Promise<PanelCheckpoint | null> => ipcRenderer.invoke('viewer:panel-state:read', scope),
  update: (scope: PanelScope, checkpoint: PanelCheckpoint | null): Promise<void> =>
    ipcRenderer.invoke('viewer:panel-state:update', scope, checkpoint)
}
