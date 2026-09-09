import { contextBridge, ipcRenderer } from 'electron'
import type { PetCommand, PetEvent } from '../shared/desktopPet'

// Sandboxed preloads cannot require Rollup's shared CommonJS chunks. Keep this
// bridge self-contained so the packaged companion window can always initialize.
const desktopPet = {
  command: (command: PetCommand): Promise<void> => ipcRenderer.invoke('desktop-pet:command', command),
  onEvent: (callback: (event: PetEvent) => void): (() => void) => {
    const listener = (_event: Electron.IpcRendererEvent, payload: PetEvent): void => callback(payload)
    ipcRenderer.on('desktop-pet:event', listener)
    return () => ipcRenderer.removeListener('desktop-pet:event', listener)
  }
}

contextBridge.exposeInMainWorld('api', { desktopPet })
