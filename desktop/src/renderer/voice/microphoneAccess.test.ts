// @vitest-environment jsdom

import { beforeEach, describe, expect, it, vi } from 'vitest'

import { VoiceCaptureError } from './audioCapture'
import { probeMicrophoneAccess, requestMicrophoneAccess } from './microphoneAccess'

const getPermissionStatus = vi.fn()
const requestPermission = vi.fn()

beforeEach(() => {
  getPermissionStatus.mockReset().mockResolvedValue('not-determined')
  requestPermission.mockReset().mockResolvedValue('granted')
  Object.defineProperty(window, 'api', {
    configurable: true,
    value: {
      voice: {
        getMicrophonePermissionStatus: getPermissionStatus,
        requestMicrophonePermission: requestPermission
      }
    }
  })
})

describe('microphone access', () => {
  it('stops before requesting when the operating system reports a block', async () => {
    getPermissionStatus.mockResolvedValue('denied')

    await expect(requestMicrophoneAccess()).rejects.toMatchObject({ code: 'permission-denied' })
    expect(requestPermission).not.toHaveBeenCalled()
  })

  it('requests permission and continues in the same action', async () => {
    await expect(requestMicrophoneAccess()).resolves.toBe('granted')
    expect(requestPermission).toHaveBeenCalledTimes(1)
  })

  it('falls back to the system default and closes the probe stream', async () => {
    const stop = vi.fn()
    const getUserMedia = vi.fn()
      .mockRejectedValueOnce(new DOMException('missing', 'OverconstrainedError'))
      .mockResolvedValueOnce({ getTracks: () => [{ stop }] })
    Object.defineProperty(navigator, 'mediaDevices', {
      configurable: true,
      value: { getUserMedia }
    })

    await expect(probeMicrophoneAccess('removed-device')).resolves.toEqual({ usedDefaultDevice: true })
    expect(getUserMedia).toHaveBeenCalledTimes(2)
    expect(stop).toHaveBeenCalledTimes(1)
  })

  it('maps a microphone held by another application separately', async () => {
    Object.defineProperty(navigator, 'mediaDevices', {
      configurable: true,
      value: { getUserMedia: vi.fn().mockRejectedValue(new DOMException('busy', 'NotReadableError')) }
    })

    await expect(probeMicrophoneAccess()).rejects.toEqual(new VoiceCaptureError('device-unavailable'))
  })
})
