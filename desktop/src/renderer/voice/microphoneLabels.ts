const WINDOWS_USB_DEVICE_SUFFIX = /\s+\([0-9a-f]{4}:[0-9a-f]{4}\)$/i

/**
 * Chromium appends a Windows USB VID:PID pair to some media-device labels.
 * Keep that transport identifier out of the UI while preserving the real
 * deviceId separately for capture constraints.
 */
export function formatMicrophoneLabel(label: string): string {
  return label.replace(WINDOWS_USB_DEVICE_SUFFIX, '').trim()
}
