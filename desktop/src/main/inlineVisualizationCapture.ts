import { clipboard, type Rectangle, type WebContents } from 'electron'
import type {
  InlineVisualizationCaptureRect,
  InlineVisualizationCaptureResult
} from '../shared/inlineVisualization'

export const INLINE_VISUALIZATION_CAPTURE_MAX_WIDTH = 4096
export const INLINE_VISUALIZATION_CAPTURE_MAX_HEIGHT = 10_000
export const INLINE_VISUALIZATION_CAPTURE_MAX_PIXELS = 32_000_000

export async function copyInlineVisualizationImage(
  webContents: WebContents,
  requestedRect: InlineVisualizationCaptureRect
): Promise<InlineVisualizationCaptureResult> {
  const rect = normalizeCaptureRect(requestedRect)
  const captured = await webContents.capturePage(rect, { stayHidden: true })
  if (captured.isEmpty()) throw new Error('The visualization capture was empty.')

  const image = captured.resize({ width: rect.width, height: rect.height, quality: 'best' })
  if (image.isEmpty()) throw new Error('The visualization image could not be prepared.')
  clipboard.writeImage(image)
  return { width: rect.width, height: rect.height }
}

export function normalizeCaptureRect(requested: InlineVisualizationCaptureRect): Rectangle {
  if (!requested || typeof requested !== 'object') throw new Error('A visualization capture rectangle is required.')
  const values = [requested.x, requested.y, requested.width, requested.height]
  if (values.some(value => !Number.isFinite(value))) throw new Error('The visualization capture rectangle is invalid.')

  const rect = {
    x: Math.round(requested.x),
    y: Math.round(requested.y),
    width: Math.round(requested.width),
    height: Math.round(requested.height)
  }
  if (rect.x < 0 || rect.y < 0 || rect.width <= 0 || rect.height <= 0) {
    throw new Error('The visualization capture rectangle is outside the window.')
  }
  if (rect.width > INLINE_VISUALIZATION_CAPTURE_MAX_WIDTH
    || rect.height > INLINE_VISUALIZATION_CAPTURE_MAX_HEIGHT
    || rect.width * rect.height > INLINE_VISUALIZATION_CAPTURE_MAX_PIXELS) {
    throw new Error('The visualization is too large to copy as an image.')
  }
  return rect
}
