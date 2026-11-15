import { app, BrowserWindow } from 'electron'
import { join } from 'node:path'
import { THEME_CHANGED_CHANNEL, THEME_CHOICES_CHANNEL, THEME_PREVIEW_CHANNEL } from '../../shared/themes'
import type { WindowIpcMain } from '../windowIpc'
import { ThemeChoiceStore } from './themeChoiceStore'
import { ThemeStore } from './themeStore'
import type { DesktopThemeHost } from './themeTools'

let store: ThemeStore | undefined
let choiceStore: ThemeChoiceStore | undefined

function broadcast(channel: string, payload?: unknown): void {
  for (const win of BrowserWindow.getAllWindows()) {
    if (!win.isDestroyed()) win.webContents.send(channel, payload)
  }
}

function choices(): ThemeChoiceStore {
  if (!choiceStore) {
    choiceStore = new ThemeChoiceStore(join(app.getPath('userData'), 'themes', 'choices.json'))
    choiceStore.onChanged((value) => broadcast(THEME_CHOICES_CHANNEL, value))
  }
  return choiceStore
}

function library(): ThemeStore {
  if (!store) {
    store = new ThemeStore(join(app.getPath('userData'), 'themes', 'library.json'))
    store.onChanged(() => broadcast(THEME_CHANGED_CHANNEL))
  }
  return store
}

export function themeHostFor(win: BrowserWindow): DesktopThemeHost {
  return {
    store: library(),
    preview(themeId, threadId) {
      if (win.isDestroyed()) throw new Error('The theme preview window is closed.')
      win.webContents.send(THEME_PREVIEW_CHANNEL, { themeId, threadId })
    }
  }
}

async function hasGpuCompositing(): Promise<boolean> {
  if (!app.isHardwareAccelerationEnabled()) return false
  await app.getGPUInfo('basic').catch(() => undefined)
  const compositing = app.getGPUFeatureStatus().gpu_compositing
  return !compositing || compositing.startsWith('enabled')
}

export function registerThemeIpc(ipc: WindowIpcMain): void {
  ipc.handle('themes:gpu-compositing', hasGpuCompositing)
  ipc.handle('themes:list', () => library().list())
  ipc.handle('themes:save', (_event, theme: unknown) => library().save(theme))
  ipc.handle('themes:remove', async (_event, id: unknown) => {
    if (typeof id !== 'string') throw new Error('Theme id is required.')
    await library().remove(id)
    await choices().forget(id)
  })
  ipc.handle('themes:choices', () => choices().get())
  ipc.handle('themes:set-choice', (_event, scope: unknown, patch: unknown) => choices().set(scope, patch))
}
