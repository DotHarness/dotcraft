import { ipcRenderer } from 'electron'
import {
  THEME_CHANGED_CHANNEL, THEME_CHOICES_CHANNEL, THEME_PREVIEW_CHANNEL,
  type ThemesApi, type ThemeChoices, type ThemePreviewRequest
} from '../shared/themes'

export const themes: ThemesApi = {
  list: () => ipcRenderer.invoke('themes:list'),
  hasGpuCompositing: () => ipcRenderer.invoke('themes:gpu-compositing'),
  save: (theme) => ipcRenderer.invoke('themes:save', theme),
  remove: (id) => ipcRenderer.invoke('themes:remove', id),
  choices: () => ipcRenderer.invoke('themes:choices'),
  setChoice: (scope, patch) => ipcRenderer.invoke('themes:set-choice', scope, patch),
  onChoicesChanged(listener) {
    const handle = (_event: Electron.IpcRendererEvent, value: ThemeChoices): void => listener(value)
    ipcRenderer.on(THEME_CHOICES_CHANNEL, handle)
    return () => { ipcRenderer.removeListener(THEME_CHOICES_CHANNEL, handle) }
  },
  onChanged(listener) {
    const handle = (): void => listener()
    ipcRenderer.on(THEME_CHANGED_CHANNEL, handle)
    return () => { ipcRenderer.removeListener(THEME_CHANGED_CHANNEL, handle) }
  },
  onPreview(listener) {
    const handle = (_event: Electron.IpcRendererEvent, value: ThemePreviewRequest): void => listener(value)
    ipcRenderer.on(THEME_PREVIEW_CHANNEL, handle)
    return () => { ipcRenderer.removeListener(THEME_PREVIEW_CHANNEL, handle) }
  }
}
