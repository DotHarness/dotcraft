import type { VoiceMicrophonePermissionStatus } from '../../shared/voice'
import { mapMediaError, VoiceCaptureError } from './audioCapture'

export interface MicrophoneProbeResult {
  usedDefaultDevice: boolean
}

export async function requestMicrophoneAccess(): Promise<VoiceMicrophonePermissionStatus> {
  const current = await window.api.voice.getMicrophonePermissionStatus()
  if (isBlocked(current)) throw new VoiceCaptureError('permission-denied')
  const requested = await window.api.voice.requestMicrophonePermission()
  if (isBlocked(requested)) throw new VoiceCaptureError('permission-denied')
  return requested
}

export async function probeMicrophoneAccess(deviceId?: string): Promise<MicrophoneProbeResult> {
  if (!navigator.mediaDevices?.getUserMedia) throw new VoiceCaptureError('device-missing')
  await requestMicrophoneAccess()

  let stream: MediaStream | null = null
  let usedDefaultDevice = false
  try {
    try {
      stream = await navigator.mediaDevices.getUserMedia({
        audio: deviceId ? { deviceId: { exact: deviceId } } : true,
        video: false
      })
    } catch (error) {
      if (!deviceId || !isMissingSelectedDeviceError(error)) throw error
      stream = await navigator.mediaDevices.getUserMedia({ audio: true, video: false })
      usedDefaultDevice = true
    }
    return { usedDefaultDevice }
  } catch (error) {
    throw new VoiceCaptureError(mapMediaError(error))
  } finally {
    for (const track of stream?.getTracks() ?? []) track.stop()
  }
}

export function isBlockedMicrophonePermission(status: VoiceMicrophonePermissionStatus): boolean {
  return isBlocked(status)
}

function isBlocked(status: VoiceMicrophonePermissionStatus): boolean {
  return status === 'denied' || status === 'restricted'
}

function isMissingSelectedDeviceError(error: unknown): boolean {
  const name = typeof error === 'object' && error != null && 'name' in error && typeof error.name === 'string'
    ? error.name
    : ''
  return name === 'NotFoundError' || name === 'OverconstrainedError'
}
