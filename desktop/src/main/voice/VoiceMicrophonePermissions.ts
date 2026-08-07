import {
  BrowserWindow,
  shell,
  systemPreferences,
  type Session,
  type WebContents
} from 'electron'

import type { VoiceMicrophonePermissionStatus } from '../../shared/voice'

interface SystemPreferencesLike {
  getMediaAccessStatus(mediaType: 'microphone'): string
  askForMediaAccess(mediaType: 'microphone'): Promise<boolean>
}

interface ShellLike {
  openExternal(url: string): Promise<void>
}

export class VoiceMicrophonePermissions {
  constructor(
    private readonly platform = process.platform,
    private readonly preferences: SystemPreferencesLike = systemPreferences,
    private readonly systemShell: ShellLike = shell
  ) {}

  getStatus(): VoiceMicrophonePermissionStatus {
    if (this.platform !== 'darwin' && this.platform !== 'win32') return 'unknown'
    return normalizePermissionStatus(this.preferences.getMediaAccessStatus('microphone'))
  }

  async request(): Promise<VoiceMicrophonePermissionStatus> {
    if (this.platform === 'darwin' && this.getStatus() === 'not-determined') {
      await this.preferences.askForMediaAccess('microphone')
    }
    return this.getStatus()
  }

  async openSettings(): Promise<void> {
    const target = this.platform === 'win32'
      ? 'ms-settings:privacy-microphone'
      : this.platform === 'darwin'
        ? 'x-apple.systempreferences:com.apple.preference.security?Privacy_Microphone'
        : null
    if (!target) return
    await this.systemShell.openExternal(target)
  }
}

export const voiceMicrophonePermissions = new VoiceMicrophonePermissions()

export function configureVoiceMediaPermissions(applicationSession: Session): void {
  applicationSession.setPermissionCheckHandler((webContents, permission, _origin, details) => {
    if (!isApplicationWindow(webContents)) return false
    if (permission === 'clipboard-sanitized-write') return true
    return permission === 'media' && details.mediaType === 'audio'
  })

  applicationSession.setPermissionRequestHandler((webContents, permission, callback, details) => {
    if (!isApplicationWindow(webContents)) {
      callback(false)
      return
    }
    if (permission === 'clipboard-sanitized-write') {
      callback(true)
      return
    }
    const mediaTypes = permission === 'media' && 'mediaTypes' in details ? details.mediaTypes : undefined
    callback(permission === 'media' && mediaTypes?.length === 1 && mediaTypes[0] === 'audio')
  })
}

export function normalizePermissionStatus(value: string): VoiceMicrophonePermissionStatus {
  if (value === 'not-determined' || value === 'granted' || value === 'denied' || value === 'restricted') {
    return value
  }
  return 'unknown'
}

function isApplicationWindow(webContents: WebContents | null): boolean {
  if (!webContents || webContents.isDestroyed()) return false
  const window = BrowserWindow.fromWebContents(webContents)
  return window != null && !window.isDestroyed() && window.webContents === webContents
}
