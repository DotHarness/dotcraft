import { app } from 'electron'

export const DOTCRAFT_DESKTOP_APP_NAME = 'DotCraft'
export const DOTCRAFT_DESKTOP_APP_ID = 'com.dotcraft.desktop'

export function resolveWindowsAppUserModelId(): string {
  return app.isPackaged ? DOTCRAFT_DESKTOP_APP_ID : DOTCRAFT_DESKTOP_APP_NAME
}

export function configureAppIdentity(): void {
  app.setName(DOTCRAFT_DESKTOP_APP_NAME)

  if (process.platform === 'win32') {
    app.setAppUserModelId(resolveWindowsAppUserModelId())
  }
}
