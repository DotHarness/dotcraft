export interface ChromeSetupCheckStatus {
  ok: boolean
  code: string
  message: string
  action?: string
  safeDetails?: Record<string, string | number | boolean>
}

export interface ChromeSetupStatus {
  extension: ChromeSetupCheckStatus
  nativeHost: ChromeSetupCheckStatus
  chromeRunning: ChromeSetupCheckStatus
  installedBrowsers: ChromeSetupCheckStatus
  backend: ChromeSetupCheckStatus
  bridge: ChromeSetupCheckStatus
}
