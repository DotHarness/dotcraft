export function applyEmbeddedBrowserSecurity(
  preferences: Electron.WebPreferences,
  browserSession?: Electron.Session
): Electron.WebPreferences {
  delete preferences.preload
  preferences.nodeIntegration = false
  preferences.nodeIntegrationInSubFrames = false
  preferences.nodeIntegrationInWorker = false
  preferences.contextIsolation = true
  preferences.sandbox = true
  preferences.webSecurity = true
  preferences.allowRunningInsecureContent = false
  preferences.webviewTag = false
  preferences.plugins = false
  preferences.devTools = true
  if (browserSession) preferences.session = browserSession
  return preferences
}
