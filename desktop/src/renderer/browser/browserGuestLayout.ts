import type { BrowserHostDescriptor } from '../../shared/viewer/browserHost'
import type { Size } from './cursorPath'

export interface BrowserGuestLayout {
  box: Size & { left: number; top: number }
  page?: Size & { scale: number }
  captureScale: number
}

const GUTTER = 20

function fitScale(content: Size, available: Size): number {
  return Math.min(1, available.width / content.width, available.height / content.height)
}

export function layoutBrowserGuest(host: BrowserHostDescriptor, screen: Size): BrowserGuestLayout {
  const { bounds, viewport, captureSurfaceSize } = host
  if (captureSurfaceSize) {
    return {
      box: { left: 0, top: 0, ...captureSurfaceSize },
      captureScale: fitScale(captureSurfaceSize, screen)
    }
  }
  if (!host.visible) {
    const { width, height } = viewport ?? bounds
    return { box: { left: 0, top: 0, width, height }, page: viewport && { ...viewport, scale: 1 }, captureScale: 1 }
  }
  if (!viewport) {
    return { box: { left: bounds.x, top: bounds.y, width: bounds.width, height: bounds.height }, captureScale: 1 }
  }
  const scale = fitScale(viewport, {
    width: Math.max(0, bounds.width - 2 * GUTTER),
    height: Math.max(0, bounds.height - GUTTER)
  })
  const width = Math.round(viewport.width * scale)
  return {
    box: {
      left: Math.round(bounds.x + Math.max(GUTTER, (bounds.width - width) / 2)),
      top: bounds.y,
      width,
      height: Math.round(viewport.height * scale)
    },
    page: { ...viewport, scale },
    captureScale: 1
  }
}
