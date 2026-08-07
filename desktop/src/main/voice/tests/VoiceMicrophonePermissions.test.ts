import { beforeEach, describe, expect, it, vi } from 'vitest'

const { fromWebContents } = vi.hoisted(() => ({ fromWebContents: vi.fn() }))

vi.mock('electron', () => ({
  BrowserWindow: { fromWebContents },
  shell: { openExternal: vi.fn() },
  systemPreferences: {
    getMediaAccessStatus: vi.fn(() => 'unknown'),
    askForMediaAccess: vi.fn(async () => false)
  }
}))

import {
  VoiceMicrophonePermissions,
  configureVoiceMediaPermissions,
  normalizePermissionStatus
} from '../VoiceMicrophonePermissions'

describe('VoiceMicrophonePermissions', () => {
  beforeEach(() => fromWebContents.mockReset())

  it('asks macOS only while permission is not determined', async () => {
    const getStatus = vi.fn()
      .mockReturnValueOnce('not-determined')
      .mockReturnValueOnce('granted')
    const ask = vi.fn(async () => true)
    const permissions = new VoiceMicrophonePermissions(
      'darwin',
      { getMediaAccessStatus: getStatus, askForMediaAccess: ask },
      { openExternal: vi.fn() }
    )

    await expect(permissions.request()).resolves.toBe('granted')
    expect(ask).toHaveBeenCalledWith('microphone')
  })

  it('does not fake a Windows permission prompt', async () => {
    const ask = vi.fn(async () => true)
    const permissions = new VoiceMicrophonePermissions(
      'win32',
      { getMediaAccessStatus: () => 'denied', askForMediaAccess: ask },
      { openExternal: vi.fn() }
    )

    await expect(permissions.request()).resolves.toBe('denied')
    expect(ask).not.toHaveBeenCalled()
  })

  it.each([
    ['win32', 'ms-settings:privacy-microphone'],
    ['darwin', 'x-apple.systempreferences:com.apple.preference.security?Privacy_Microphone']
  ])('opens the %s microphone settings destination', async (platform, target) => {
    const openExternal = vi.fn(async () => {})
    const permissions = new VoiceMicrophonePermissions(
      platform,
      { getMediaAccessStatus: () => 'unknown', askForMediaAccess: vi.fn() },
      { openExternal }
    )

    await permissions.openSettings()
    expect(openExternal).toHaveBeenCalledWith(target)
  })

  it('normalizes unsupported platform values', () => {
    expect(normalizePermissionStatus('granted')).toBe('granted')
    expect(normalizePermissionStatus('limited')).toBe('unknown')
  })
})

describe('configureVoiceMediaPermissions', () => {
  it('allows only application-window audio and sanitized clipboard requests', () => {
    let check: ((...args: any[]) => boolean) | undefined
    let request: ((...args: any[]) => void) | undefined
    const applicationSession = {
      setPermissionCheckHandler: vi.fn((handler) => { check = handler }),
      setPermissionRequestHandler: vi.fn((handler) => { request = handler })
    }
    const webContents = { isDestroyed: () => false }
    fromWebContents.mockReturnValue({ isDestroyed: () => false, webContents })

    configureVoiceMediaPermissions(applicationSession as never)

    expect(check?.(webContents, 'media', '', { mediaType: 'audio' })).toBe(true)
    expect(check?.(webContents, 'media', '', { mediaType: 'video' })).toBe(false)
    expect(check?.(webContents, 'clipboard-sanitized-write', '', {})).toBe(true)

    const callback = vi.fn()
    request?.(webContents, 'media', callback, { mediaTypes: ['audio'] })
    expect(callback).toHaveBeenLastCalledWith(true)
    request?.(webContents, 'media', callback, { mediaTypes: ['audio', 'video'] })
    expect(callback).toHaveBeenLastCalledWith(false)

    fromWebContents.mockReturnValue(null)
    request?.(webContents, 'media', callback, { mediaTypes: ['audio'] })
    expect(callback).toHaveBeenLastCalledWith(false)
  })
})
